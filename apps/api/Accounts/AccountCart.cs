using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public record CartUpdate(int AccountId, Guid Version, CartItem[] Items, bool Merge = false);

public static class AccountCart
{
    static async Task<object> View(ShopDb db, Account a)
    {
        var lines = await (
            from l in db.SavedCartLines
            where l.AccountId == a.Id
            join p in db.Perfumes on l.PerfumeId equals p.Id
            join v in db.Variants
                on new { l.PerfumeId, l.SizeMl } equals new { v.PerfumeId, v.SizeMl }
            orderby l.PerfumeId, l.SizeMl
            select new
            {
                l.PerfumeId,
                l.SizeMl,
                l.Quantity,
                p.Name,
                p.Slug,
                v.Price,
            }
        ).ToListAsync();
        return new
        {
            accountId = a.Id,
            a.CartVersion,
            lines,
        };
    }

    static async Task<object> Read(HttpContext ctx, ShopDb db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var account = await CustomerAccounts.Lock(db, CustomerAccounts.CustomerId(ctx.User)!.Value);
        return await View(db, account);
    }

    static async Task<object> Write(CartUpdate input, HttpContext ctx, ShopDb db)
    {
        if (input.AccountId != CustomerAccounts.CustomerId(ctx.User))
            throw new ShopError("Account changed. Reload your bag.", 409);
        if (
            input.Items == null
            || input.Items.Length > 100
            || input.Items.Any(l =>
                l == null || l.Quantity is < 1 or > 99 || !Seed.Sizes.Contains(l.SizeMl)
            )
            || input.Items.DistinctBy(l => (l.PerfumeId, l.SizeMl)).Count() != input.Items.Length
        )
            throw new ShopError("Invalid bag contents.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await CustomerAccounts.Lock(db, input.AccountId);
        if (a.CartVersion != input.Version)
            throw new ShopError(
                "Your bag changed on another device. Review the refreshed bag.",
                409
            );
        var saved = await db
            .SavedCartLines.Where(l => l.AccountId == a.Id)
            .ToDictionaryAsync(l => (l.PerfumeId, l.SizeMl));
        var requested = input.Items.ToDictionary(l => (l.PerfumeId, l.SizeMl));
        var ids = input.Items.Select(l => l.PerfumeId).Distinct().ToArray();
        var variants = (
            await db
                .Variants.Where(v => ids.Contains(v.PerfumeId))
                .Select(v => new { v.PerfumeId, v.SizeMl })
                .ToListAsync()
        )
            .Select(v => (v.PerfumeId, v.SizeMl))
            .ToHashSet();
        if (!requested.Keys.All(variants.Contains))
            throw new ShopError("A selected fragrance size no longer exists.");
        if (input.Merge && saved.Keys.Union(requested.Keys).Count() > 100)
            throw new ShopError("The combined bag exceeds 100 lines.");
        foreach (var (key, line) in saved)
            if (!input.Merge && !requested.ContainsKey(key))
                db.Remove(line);
        foreach (var (key, item) in requested)
        {
            if (saved.TryGetValue(key, out var line))
                line.Quantity = input.Merge
                    ? Math.Max(line.Quantity, item.Quantity)
                    : item.Quantity;
            else
                db.Add(
                    new SavedCartLine
                    {
                        AccountId = a.Id,
                        PerfumeId = item.PerfumeId,
                        SizeMl = item.SizeMl,
                        Quantity = item.Quantity,
                    }
                );
        }
        a.CartVersion = Guid.NewGuid();
        await db.SaveChangesAsync();
        var result = await View(db, a);
        await tx.CommitAsync();
        return result;
    }

    public static void Map(RouteGroupBuilder account)
    {
        account.MapGet("/cart", Read);
        account.MapPut("/cart", Write);
    }
}
