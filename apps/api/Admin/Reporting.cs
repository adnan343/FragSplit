using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public sealed record MetricBucket(string Name, decimal Sales, int Units);

public static partial class Metrics
{
    public static async Task<object> Overview(ShopDb db, DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Dhaka));
        var first = from ?? today.AddDays(-29);
        var last = to ?? today;
        if (first > last || last.DayNumber - first.DayNumber > 365 || last == DateOnly.MaxValue)
            throw new ShopError("Choose a range of up to 366 days.");
        var start = Start(first);
        var end = Start(last.AddDays(1));
        var placed = db.Orders.AsNoTracking().Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        var counts = await placed
            .GroupBy(o => 1)
            .Select(g => new
            {
                orders = g.Count(),
                cod = g.Count(x => x.Method == "COD"),
                pending = g.Count(x => x.Fulfillment == "Pending"),
                cancelled = g.Count(x => x.Fulfillment == "Cancelled"),
                awaitingCod = g.Where(x =>
                        x.Method == "COD"
                        && x.Payment == "Unpaid"
                        && x.Fulfillment != "Cancelled"
                        && x.Fulfillment != "Returned"
                    )
                    .Sum(x => x.Total),
            })
            .SingleOrDefaultAsync();
        // Risky and unfulfillable gateway receipts are held for reconciliation.
        var eligible = db.Orders.Where(o =>
            !o.History.Any(h => h.Event == "Verified charge requires reconciliation")
        );
        var paid = eligible.Where(o => o.PaidAt >= start && o.PaidAt < end);
        var refunded = eligible.Where(o =>
            o.History.Any(h => h.Event == "Refund" && h.At >= start && h.At < end)
        );
        var gross = await paid.SumAsync(o => o.Total);
        var refunds = await refunded.SumAsync(o => o.Total);
        // Bangladesh has UTC+06:00 throughout supported reporting dates. Group calendar days in PostgreSQL.
        var receipts = await paid.GroupBy(o => o.PaidAt!.Value.AddHours(6).Date)
            .Select(g => new { date = g.Key, value = g.Sum(o => o.Total) })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.date), x => x.value);
        var reversals = await db
            .History.Where(h => h.Event == "Refund" && h.At >= start && h.At < end)
            .Join(eligible, h => h.OrderId, o => o.Id, (h, o) => new { h.At, o.Total })
            .GroupBy(x => x.At.AddHours(6).Date)
            .Select(g => new { date = g.Key, value = g.Sum(x => x.Total) })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.date), x => x.value);
        var placements = await placed
            .GroupBy(o => o.CreatedAt.AddHours(6).Date)
            .Select(g => new { date = g.Key, n = g.Count() })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.date), x => x.n);
        var days = Enumerable
            .Range(0, last.DayNumber - first.DayNumber + 1)
            .Select(n => first.AddDays(n))
            .Select(d => new
            {
                date = d,
                revenue = receipts.GetValueOrDefault(d) - reversals.GetValueOrDefault(d),
                orders = placements.GetValueOrDefault(d),
            });
        var sold = paid.SelectMany(o => o.Lines)
            .Select(l => new
            {
                l.PerfumeId,
                l.Name,
                l.Category,
                l.SizeMl,
                l.Quantity,
                sales = l.Quantity * l.UnitPrice,
            });
        var reversed = refunded
            .SelectMany(o => o.Lines)
            .Select(l => new
            {
                l.PerfumeId,
                l.Name,
                l.Category,
                l.SizeMl,
                Quantity = -l.Quantity,
                sales = -l.Quantity * l.UnitPrice,
            });
        var net = sold.Concat(reversed);
        var categories = await net.GroupBy(l => l.Category)
            .Select(g => new MetricBucket(g.Key, g.Sum(x => x.sales), g.Sum(x => x.Quantity)))
            .ToListAsync();
        var sizes = await net.GroupBy(l => l.SizeMl)
            .OrderBy(g => g.Key)
            .Select(g => new MetricBucket(
                g.Key + " ml",
                g.Sum(x => x.sales),
                g.Sum(x => x.Quantity)
            ))
            .ToListAsync();
        var products = await net.GroupBy(l => new { l.PerfumeId, l.Name })
            .Select(g => new
            {
                g.Key.Name,
                Sales = g.Sum(x => x.sales),
                Units = g.Sum(x => x.Quantity),
            })
            .OrderByDescending(x => x.Units)
            .ThenBy(x => x.Name)
            .Take(10)
            .ToListAsync();
        return new
        {
            from = first,
            to = last,
            gross,
            refunds,
            netSales = gross - refunds,
            online = await paid.Where(o => o.Method == "Online").SumAsync(o => o.Total),
            paidOrders = await paid.CountAsync(),
            orders = counts?.orders ?? 0,
            cod = counts?.cod ?? 0,
            pending = counts?.pending ?? 0,
            cancelled = counts?.cancelled ?? 0,
            awaitingCod = counts?.awaitingCod ?? 0,
            units = categories.Sum(x => x.Units),
            lowStock = await db.Perfumes.CountAsync(p =>
                p.Active && p.OnHandMl - p.ReservedMl <= p.Threshold
            ),
            days,
            products,
            categories,
            sizes,
        };
    }
}
