using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public record CartItem(int PerfumeId, int SizeMl, int Quantity);

public record Basket(CartItem[] Items, int ZoneId, string Method);

public record Customer(
    string Name,
    string Phone,
    string? Email,
    string Address,
    string City,
    string Area,
    string? Postcode,
    string? Notes
);

public record Submission(
    Basket Basket,
    Customer Customer,
    bool Accepted,
    string Quote,
    Guid RequestKey,
    int? AccountId = null,
    Guid? CartVersion = null
);

public record Quote(
    decimal Subtotal,
    decimal Shipping,
    decimal Total,
    string Region,
    List<OrderLine> Lines,
    string Signature
);

public record Recovery(Guid Key);

public record Change(string Action, string? Reason, string? Tracking, bool RecoverStock = false);

public class Checkout(
    ShopDb db,
    IDataProtectionProvider dp,
    IConfiguration config,
    TimeProvider clock,
    IPaymentAvailability payments
)
{
    readonly IDataProtector access = dp.CreateProtector("guest-order-status-v1");

    public static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    public static int RequiredMl(IEnumerable<CartItem> items) =>
        items.Sum(l => checked(l.Quantity * l.SizeMl));

    public async Task<Quote> Price(Basket b, bool lockStock = false)
    {
        if (
            b?.Items == null
            || b.Items.Length is < 1 or > 100
            || b.Items.Any(l =>
                l == null || l.Quantity is < 1 or > 99 || !Seed.Sizes.Contains(l.SizeMl)
            )
            || b.Items.DistinctBy(l => (l.PerfumeId, l.SizeMl)).Count() != b.Items.Length
        )
            throw new ShopError("Check cart quantities and sizes.");
        var zone = await db
            .Zones.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == b.ZoneId && x.Active)
            ?? throw new ShopError("Choose an available delivery region.");
        if (
            b.Method == "COD" && !zone.Cod
            || b.Method == "Online" && !payments.Enabled
            || b.Method is not ("COD" or "Online")
        )
            throw new ShopError("Payment method is unavailable for this region.");
        var lines = new List<OrderLine>();
        foreach (var group in b.Items.GroupBy(l => l.PerfumeId).OrderBy(g => g.Key))
        {
            var p = lockStock
                ? await Catalog.Lock(db, group.Key)
                : await db.Perfumes.FindAsync(group.Key)
                    ?? throw new ShopError("Fragrance is unavailable.", 409);
            await db.Entry(p).Collection(x => x.Variants).LoadAsync();
            if (!p.Active || RequiredMl(group) > p.OnHandMl - p.ReservedMl)
                throw new ShopError($"Insufficient stock for {p.Name}. Update your bag.", 409);
            foreach (var item in group.OrderBy(l => l.SizeMl))
            {
                var v = p.Variants.SingleOrDefault(x => x.SizeMl == item.SizeMl && x.Enabled)
                    ?? throw new ShopError($"{p.Name} is unavailable in {item.SizeMl} ml.", 409);
                lines.Add(
                    new OrderLine
                    {
                        PerfumeId = p.Id,
                        Name = p.Name,
                        Brand = p.Brand,
                        Category = p.Category,
                        SizeMl = v.SizeMl,
                        UnitPrice = v.Price,
                        Quantity = item.Quantity,
                    }
                );
            }
        }
        var subtotal = lines.Sum(l => l.UnitPrice * l.Quantity);
        var total = subtotal + zone.Charge;
        if (b.Method == "Online" && total is < 10 or > 500000)
            throw new ShopError("Online checkout supports totals between ৳10 and ৳500,000.");
        return new(
            subtotal,
            zone.Charge,
            total,
            zone.Name,
            lines,
            Hash(
                new
                {
                    lines,
                    zone.Charge,
                    zone.Name,
                    b.Method,
                }
            )
        );
    }

    public async Task<object> Submit(Submission s, int? accountId = null)
    {
        if (s.RequestKey == Guid.Empty || !s.Accepted || s.Customer == null || s.Basket == null)
            throw new ShopError("Review the order and accept the store policies.");
        var c = s.Customer;
        var phone = Regex.Replace(Catalog.Field(c.Phone, 30, true), "[ ()-]", "");
        if (!Regex.IsMatch(phone, @"^(?:\+?88)?01[3-9][0-9]{8}$"))
            throw new ShopError("Enter a Bangladesh mobile number.");
        await using var tx = await db.Database.BeginTransactionAsync();
        // Serialize concurrent submissions sharing the same key even before an order row exists.
        var key = Hash(s.RequestKey);
        if (s.AccountId != accountId)
            throw new ShopError("Your session changed. Refresh checkout and review your bag.", 409);
        var fingerprint =
            accountId == null
                ? Hash(
                    new
                    {
                        s.Basket,
                        s.Customer,
                        s.Accepted,
                        s.Quote,
                        s.RequestKey,
                    }
                )
                : Hash(s);
        long lockId = BitConverter.ToInt64(s.RequestKey.ToByteArray());
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})");
        if (await db.Orders.SingleOrDefaultAsync(o => o.RequestKey == key) is Order prior)
        {
            if (prior.Fingerprint != fingerprint || prior.AccountId != accountId)
                throw new ShopError("This checkout key belongs to a different submission.", 409);
            return Receipt(prior);
        }
        var email = Catalog.Field(c.Email, s.Basket.Method == "Online" ? 50 : 254, true);
        if (
            !System.Net.Mail.MailAddress.TryCreate(email, out var recipient)
            || recipient.Address != email
        )
            throw new ShopError("Enter a valid email address.");
        Account? account = null;
        if (accountId.HasValue)
        {
            account = await CustomerAccounts.Lock(db, accountId.Value);
            var saved = await db
                .SavedCartLines.Where(l => l.AccountId == accountId)
                .OrderBy(l => l.PerfumeId)
                .ThenBy(l => l.SizeMl)
                .Select(l => new CartItem(l.PerfumeId, l.SizeMl, l.Quantity))
                .ToListAsync();
            if (
                s.CartVersion != account.CartVersion
                || s.Basket.Items == null
                || s.Basket.Items.Any(l => l == null)
                || !saved.SequenceEqual(
                    s.Basket.Items.OrderBy(l => l.PerfumeId).ThenBy(l => l.SizeMl)
                )
            )
                throw new ShopError(
                    "Your saved bag changed. Refresh checkout and review your order.",
                    409
                );
        }
        var quote = await Price(s.Basket, true);
        if (quote.Signature != s.Quote)
            throw new ShopError(
                "Prices or delivery charges changed. Review your updated quote.",
                409
            );
        var online = s.Basket.Method == "Online";
        var order = new Order
        {
            AccountId = accountId,
            Reference = "FS-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10)),
            RequestKey = key,
            Fingerprint = fingerprint,
            Name = Catalog.Field(c.Name, 50, true),
            Phone = phone,
            Email = email,
            Address = Catalog.Field(c.Address, 50, true),
            City = Catalog.Field(c.City, 50, true),
            Area = Catalog.Field(c.Area, 50, true),
            Postcode = Catalog.Field(c.Postcode, 12, online),
            Notes = Catalog.Field(c.Notes, 600),
            Region = quote.Region,
            Method = s.Basket.Method,
            Subtotal = quote.Subtotal,
            Shipping = quote.Shipping,
            Total = quote.Total,
            Lines = quote.Lines,
            Payment = online ? "Pending" : "Unpaid",
            ExpiresAt = online
                ? clock
                    .GetUtcNow()
                    .UtcDateTime.AddMinutes(
                        Math.Clamp(config.GetValue("RESERVATION_MINUTES", 15), 1, 120)
                    )
                : null,
        };
        order.History.Add(new History { Event = "Order submitted", Actor = "customer" });
        if (account != null)
        {
            await db.SavedCartLines.Where(l => l.AccountId == account.Id).ExecuteDeleteAsync();
            account.DisplayName = order.Name;
            account.Phone = order.Phone;
            account.Street = order.Address;
            account.Area = order.Area;
            account.City = order.City;
            account.Postcode = order.Postcode;
            account.CartVersion = Guid.NewGuid();
        }
        db.Add(order);
        await db.SaveChangesAsync();
        await Move(order, "Reserve", "Checkout", "customer");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Receipt(order);
    }

    public async Task<object> Recover(Guid key, int? accountId = null)
    {
        if (key == Guid.Empty)
            throw new ShopError("Missing checkout key.");
        var hash = Hash(key);
        var order = await db
            .Orders.AsNoTracking()
            .SingleOrDefaultAsync(o => o.RequestKey == hash && o.AccountId == accountId)
            ?? throw new ShopError("No submitted order for this checkout.", 404);
        return Receipt(order);
    }

    object Receipt(Order o) =>
        new
        {
            o.Reference,
            token = o.AccountId == null ? access.Protect(o.Reference) : null,
            o.Method,
        };

    public void Authorize(string reference, string? token)
    {
        try
        {
            if (!string.IsNullOrEmpty(token) && access.Unprotect(token) == reference)
                return;
        }
        catch (CryptographicException) { }
        throw new ShopError("Order access link is invalid.", 404);
    }

    public async Task AuthorizeOrder(string reference, HttpContext ctx)
    {
        var order = await db
            .Orders.AsNoTracking()
            .Where(o => o.Reference == reference)
            .Select(o => new { o.AccountId })
            .SingleOrDefaultAsync();
        if (order == null)
            throw new ShopError("Order unavailable.", 404);
        if (order.AccountId == null)
        {
            Authorize(reference, ctx.Request.Headers["X-Order-Token"]);
            return;
        }
        if (order.AccountId != CustomerAccounts.CustomerId(ctx.User))
            throw new ShopError("Order unavailable.", 404);
    }

    public async Task<Order> LockOrder(string reference)
    {
        var o = await db
            .Orders.FromSqlInterpolated(
                $"SELECT * FROM \"Orders\" WHERE \"Reference\"={reference} FOR UPDATE"
            )
            .SingleOrDefaultAsync()
            ?? throw new ShopError("Order was not found.", 404);
        await db.Entry(o).Collection(x => x.Lines).LoadAsync();
        return o;
    }

    public async Task Move(Order o, string type, string reason, string actor)
    {
        var next = type switch
        {
            "Reserve" when o.StockState == "Reserved" => "Reserved",
            "Release" when o.StockState == "Reserved" => "Released",
            "Consume" when o.StockState == "Reserved" => "Consumed",
            "Restore" when o.StockState == "Consumed" => "Restored",
            _ => null,
        };
        if (next == null)
            return;
        foreach (var group in o.Lines.GroupBy(l => l.PerfumeId).OrderBy(g => g.Key))
        {
            int ml = group.Sum(l => l.Quantity * l.SizeMl);
            var p = await Catalog.Lock(db, group.Key);
            int reserved = type switch
            {
                "Reserve" => ml,
                "Consume" or "Release" => -ml,
                _ => 0,
            };
            int hand = type switch
            {
                "Consume" => -ml,
                "Restore" => ml,
                _ => 0,
            };
            p.OnHandMl += hand;
            p.ReservedMl += reserved;
            db.Add(
                new Movement
                {
                    PerfumeId = p.Id,
                    OrderId = o.Id,
                    Hand = hand,
                    Reserved = reserved,
                    Type = type,
                    Reason = reason,
                    Actor = actor,
                    Key = $"{o.Reference}:{p.Id}:{type}",
                }
            );
        }
        o.StockState = next;
    }

    public async Task Update(string reference, Change change, string actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await LockOrder(reference);
        var action = change.Action;
        var reason = Catalog.Field(
            change.Reason,
            500,
            action is "Cancelled" or "Returned" or "Refund"
        );
        if (
            action == "Collect"
            && o is { Method: "COD", Fulfillment: "Delivered", Payment: "Unpaid" }
        )
        {
            o.Payment = "Paid";
            o.PaidAt = clock.GetUtcNow().UtcDateTime;
        }
        else if (action == "Refund" && o.Payment == "Paid")
        {
            if (o.Fulfillment is "Pending" or "Confirmed" or "Processing")
            {
                if (o.StockState == "Reserved")
                    await Move(o, "Release", reason, actor);
                else if (change.RecoverStock)
                    await Move(o, "Restore", reason, actor);
                o.Fulfillment = "Cancelled";
                o.History.Add(
                    new History
                    {
                        Event = "Cancelled",
                        Reason = reason,
                        Actor = actor,
                    }
                );
            }
            o.Payment = "Refunded";
            o.NeedsReview = false;
        }
        else
        {
            bool valid = (o.Fulfillment, action) switch
            {
                ("Pending", "Confirmed") => o.Method == "COD"
                    || o.Payment == "Paid" && !o.NeedsReview,


                ("Confirmed", "Processing")
                or
                ("Processing", "Shipped")
                or
                ("Shipped", "Delivered")
                or ("Delivered", "Returned") => true,
                ("Pending" or "Confirmed" or "Processing" or "Shipped", "Cancelled") => true,
                _ => false,
            };
            if (!valid)
                throw new ShopError("This action is unavailable in the current order state.", 409);
            if (action == "Confirmed" && o.Method == "COD")
                await Move(o, "Consume", "COD accepted", actor);
            if (action is "Cancelled" or "Returned")
            {
                if (o.StockState == "Reserved")
                    await Move(o, "Release", reason, actor);
                else if (change.RecoverStock)
                    await Move(o, "Restore", reason, actor);
                if (o.Payment == "Paid")
                    o.NeedsReview = true;
            }
            o.Fulfillment = action;
            if (action == "Confirmed")
                await ConfirmationMail.Queue(db, o);
            if (action == "Shipped")
                o.Tracking = Catalog.Field(change.Tracking, 120);
        }
        o.History.Add(
            new History
            {
                Event = action,
                Reason = reason,
                Actor = actor,
            }
        );
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public static object View(Order o, bool admin = true) =>
        new
        {
            o.Reference,
            accountOrder = o.AccountId != null,
            o.Name,
            o.Phone,
            o.Email,
            o.Address,
            o.Area,
            o.City,
            o.Postcode,
            o.Notes,
            o.Region,
            o.Method,
            o.Payment,
            o.Fulfillment,
            o.StockState,
            o.Tracking,
            o.Shipping,
            o.Subtotal,
            o.Total,
            o.ExpiresAt,
            o.CreatedAt,
            o.NeedsReview,
            o.Verification,
            o.Lines,
            history = o
                .History.OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.Event,
                    x.At,
                    actor = admin ? x.Actor : "store",
                    reason = admin ? x.Reason : "",
                }),
        };

    public static void Map(RouteGroupBuilder api, RouteGroupBuilder admin)
    {
        api.MapPost("/checkout/quote", (Basket basket, Checkout service) => service.Price(basket))
            .RequireRateLimiting("purchase");
        api.MapPost("/orders/recover", (Recovery input, Checkout service, HttpContext ctx) =>
                service.Recover(input.Key, CustomerAccounts.CustomerId(ctx.User))
            )
            .RequireRateLimiting("purchase");
        api.MapPost("/orders", (Submission input, Checkout service, HttpContext ctx) =>
                service.Submit(input, CustomerAccounts.CustomerId(ctx.User))
            )
            .RequireRateLimiting("purchase");
        api.MapGet(
                "/orders/{reference}/status",
                async (string reference, HttpRequest req, Checkout service, ShopDb db) =>
                {
                    await service.AuthorizeOrder(reference, req.HttpContext);
                    return View(
                        await db
                            .Orders.AsNoTracking()
                            .Include(o => o.Lines)
                            .Include(o => o.History)
                            .SingleAsync(o => o.Reference == reference),
                        false
                    );
                }
            )
            .RequireRateLimiting("purchase");
        admin.MapGet(
            "/orders",
            async (
                ShopDb db,
                string? search,
                string? status,
                string? method,
                DateOnly? from,
                DateOnly? to,
                int? page
            ) =>
            {
                var q = db.Orders.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.ToLower();
                    q = q.Where(o =>
                        (o.Reference + " " + o.Name + " " + o.Phone).ToLower().Contains(s)
                    );
                }
                if (status != null)
                    q = q.Where(o => o.Fulfillment == status);
                if (method != null)
                    q = q.Where(o => o.Method == method);
                if (from != null)
                {
                    var start = Metrics.Start(from.Value);
                    q = q.Where(o => o.CreatedAt >= start);
                }
                if (to != null)
                {
                    var end = Metrics.Start(to.Value.AddDays(1));
                    q = q.Where(o => o.CreatedAt < end);
                }
                return await q.OrderByDescending(o => o.Id)
                    .Skip((Math.Clamp(page ?? 1, 1, 100000) - 1) * 40)
                    .Take(40)
                    .Select(o => new
                    {
                        o.Reference,
                        o.Name,
                        o.Total,
                        o.Payment,
                        o.Fulfillment,
                        o.Method,
                        o.NeedsReview,
                    })
                    .ToListAsync();
            }
        );
        admin.MapGet("/orders/{reference}", async (string reference, ShopDb db) =>
            await db
                .Orders.Include(o => o.Lines)
                .Include(o => o.History)
                .SingleOrDefaultAsync(o => o.Reference == reference)
                is Order o
                ? Results.Ok(View(o))
                : Results.NotFound()
        );
        admin.MapPatch(
            "/orders/{reference}/status",
            async (string reference, Change change, Checkout service, HttpContext ctx) =>
            {
                await service.Update(reference, change, Catalog.Actor(ctx));
                return Results.NoContent();
            }
        );
    }
}
