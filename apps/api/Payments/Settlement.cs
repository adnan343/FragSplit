using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public record PaymentRequest(string Reference);

public sealed class Settlement(
    ShopDb db,
    Checkout checkout,
    IPaymentGateway gateway,
    TimeProvider clock
)
{
    DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<string> Initiate(string reference)
    {
        Order o;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            o = await checkout.LockOrder(reference);
            if (
                o.Method != "Online"
                || o.Payment != "Pending"
                || o.StockState != "Reserved"
                || o.ExpiresAt <= Now
                || o.NeedsReview
            )
                throw new ShopError(
                    "This payment is no longer available. Review your order status.",
                    409
                );
            if (o.GatewayUrl != null)
                return o.GatewayUrl;
            if (o.Verification != "")
                throw new ShopError(
                    "A payment request is already in progress. Refresh status or contact the store.",
                    409
                );
            o.Verification = "Initiation requested";
            o.History.Add(new History { Event = o.Verification, Actor = "gateway" });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        // Persist the attempt before contacting the provider. An ambiguous timeout must never create a second session.
        HostedSession session;
        try
        {
            session = await gateway.Create(o);
        }
        catch (ShopError)
        {
            db.ChangeTracker.Clear();
            await using var failed = await db.Database.BeginTransactionAsync();
            o = await checkout.LockOrder(reference);
            if (o.Payment == "Pending" && o.SessionKey == null)
            {
                o.NeedsReview = true;
                o.Verification = "Initiation unresolved";
                o.History.Add(new History { Event = o.Verification, Actor = "gateway" });
            }
            await db.SaveChangesAsync();
            await failed.CommitAsync();
            throw;
        }
        db.ChangeTracker.Clear();
        await using var persist = await db.Database.BeginTransactionAsync();
        o = await checkout.LockOrder(reference);
        o.SessionKey = session.Key;
        o.GatewayUrl = session.Url;
        if (o.Payment == "Pending")
            o.Verification = "Session created";
        await db.SaveChangesAsync();
        await persist.CommitAsync();
        if (o.StockState != "Reserved" || o.ExpiresAt <= Now)
            throw new ShopError("Reservation expired. Review the order.", 409);
        return session.Url;
    }

    public async Task Notify(string reference, string val)
    {
        var snapshot = await db
            .Orders.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Reference == reference);
        if (snapshot == null || snapshot.Method != "Online" || snapshot.Verification == "")
            throw new ShopError("Unknown payment attempt.", 404);
        if (val != "")
        {
            if (val.Length > 100)
                throw new ShopError("Invalid validation identifier.");
            await Apply(reference, val, await gateway.Validate(val));
            return;
        }
        var records = await gateway.Query(snapshot);
        var successes = records
            .Where(r =>
                r.Reference == reference
                && r.Status is "VALID" or "VALIDATED"
                && r.ValidationId != ""
            )
            .ToList();
        foreach (var r in successes)
            await Apply(reference, r.ValidationId, await gateway.Validate(r.ValidationId));
        if (
            successes.Count == 0
            && records.Length == 1
            && snapshot.SessionKey != null
            && records[0] is { Status: "FAILED" or "CANCELLED", ApiConnect: "DONE" } failed
            && failed.Reference == reference
            && failed.SessionKey == snapshot.SessionKey
        )
            await Release(reference, "Provider confirmed " + failed.Status, false);
    }

    public static bool Matches(string reference, string val, decimal amount, GatewayResult r) =>
        r.Status is "VALID" or "VALIDATED"
        && r.ApiConnect is "" or "DONE"
        && r.Reference == reference
        && r.ValidationId == val
        && r.Amount == amount
        && r.Currency == "BDT"
        && (r.OriginalCurrency == "" || r.OriginalCurrency == "BDT" && r.OriginalAmount == amount)
        && (r.OriginalAmount == null || r.OriginalAmount == amount);

    async Task Apply(string reference, string val, GatewayResult result)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await checkout.LockOrder(reference);
        if (!Matches(reference, val, o.Total, result))
            throw new ShopError("Payment validation did not match the order.", 422);
        if (o.ValidationId == val)
            return;
        if (o.ValidationId != null)
        {
            var eventName = "Additional verified charge";
            if (
                !await db.History.AnyAsync(h =>
                    h.OrderId == o.Id && h.Event == eventName && h.Reason == val
                )
            )
            {
                o.NeedsReview = true;
                o.History.Add(
                    new History
                    {
                        Actor = "gateway",
                        Event = eventName,
                        Reason = val,
                    }
                );
            }
        }
        else
        {
            var fulfill =
                o.Payment == "Pending"
                && o.Fulfillment == "Pending"
                && o.StockState == "Reserved"
                && o.ExpiresAt > Now
                && result.Risk == "0";
            o.Payment = "Paid";
            o.ValidationId = val;
            o.PaidAt = Now;
            o.NeedsReview = !fulfill;
            o.Verification = fulfill ? "Verified" : "Verified charge requires reconciliation";
            if (fulfill)
            {
                await checkout.Move(o, "Consume", "Verified payment", "gateway");
                o.Fulfillment = "Confirmed";
                await ConfirmationMail.Queue(db, o);
            }
            else
            {
                await checkout.Move(o, "Release", "Payment requires review", "gateway");
                o.Fulfillment = "Cancelled";
            }
            o.History.Add(
                new History
                {
                    Event = o.Verification,
                    Actor = "gateway",
                    Reason = val,
                }
            );
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task Release(string reference, string reason, bool expired)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await checkout.LockOrder(reference);
        if (o.Method != "Online" || o.Payment != "Pending" || expired && o.ExpiresAt > Now)
            return;
        await checkout.Move(o, "Release", reason, "system");
        o.Payment = "Failed";
        o.Fulfillment = "Cancelled";
        o.Verification = reason;
        o.History.Add(new History { Event = reason, Actor = "system" });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task ExpireDue()
    {
        var now = Now;
        var refs = await db
            .Orders.AsNoTracking()
            .Where(o => o.Method == "Online" && o.Payment == "Pending" && o.ExpiresAt <= now)
            .OrderBy(o => o.ExpiresAt)
            .Take(100)
            .Select(o => o.Reference)
            .ToListAsync();
        foreach (var reference in refs)
            await Release(reference, "Reservation expired", true);
    }

    public static void Map(RouteGroupBuilder api, RouteGroupBuilder admin)
    {
        var group = api.MapGroup("/payments/sslcommerz");
        group
            .MapPost(
                "/initiate",
                async (
                    PaymentRequest request,
                    HttpContext ctx,
                    Checkout checkout,
                    Settlement service
                ) =>
                {
                    await checkout.AuthorizeOrder(request.Reference, ctx);
                    return new { url = await service.Initiate(request.Reference) };
                }
            )
            .RequireRateLimiting("purchase");
        foreach (var name in new[] { "ipn", "success", "fail", "cancel" })
        {
            group.MapPost(
                "/" + name,
                async (HttpRequest req, Settlement service, IConfiguration config) =>
                {
                    if (!req.HasFormContentType)
                        throw new ShopError("Expected a provider form.");
                    var form = await req.ReadFormAsync();
                    var reference = Catalog.Field(form["tran_id"], 30, true);
                    try
                    {
                        await service.Notify(reference, form["val_id"].ToString());
                    }
                    catch (ShopError) when (name != "ipn")
                    { /* Guest can inspect the protected, authoritative order state. */
                    }
                    if (name == "ipn")
                        return Results.Ok(new { received = true });
                    return Results.Redirect(
                        (config["PUBLIC_ORIGIN"] ?? "http://localhost:3000").TrimEnd('/')
                            + "/orders/"
                            + Uri.EscapeDataString(reference)
                    );
                }
            );
        }
        admin.MapPost("/orders/{reference}/reconcile", async (string reference, Settlement s) =>
        {
            await s.Notify(reference, "");
            return Results.NoContent();
        });
    }
}

public sealed class ExpiryWorker(IServiceScopeFactory scopes, ILogger<ExpiryWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(token))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<Settlement>().ExpireDue();
            }
            catch (Exception)
            {
                log.LogError("Reservation expiry pass failed. Retrying on next interval.");
            }
        }
    }
}
