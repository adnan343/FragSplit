using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public sealed class Reservations(ShopDb db, Checkout checkout, TimeProvider clock)
{
    public async Task ExpireDue(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db
            .Orders.AsNoTracking()
            .Where(o =>
                o.Fulfillment == "Pending" && o.StockState == "Reserved" && o.ExpiresAt <= now
            )
            .OrderBy(o => o.ExpiresAt)
            .Take(100)
            .Select(o => o.Reference)
            .ToListAsync(ct);
        foreach (var reference in due)
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var order = await checkout.LockOrder(reference);
            if (
                order.Fulfillment != "Pending"
                || order.StockState != "Reserved"
                || !(order.ExpiresAt <= now)
            )
                continue;
            const string reason = "Reservation expired";
            await checkout.Move(order, "Release", reason, "system");
            order.Fulfillment = "Cancelled";
            if (order.Method == "Online" && order.Payment == "Pending")
            {
                order.Payment = "Failed";
                order.Verification = reason;
            }
            order.History.Add(new History { Event = reason, Actor = "system" });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}

public sealed class ExpiryWorker(IServiceScopeFactory scopes, ILogger<ExpiryWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<Reservations>().ExpireDue(ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                log.LogError("Reservation expiry pass failed ({Failure}).", e.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
