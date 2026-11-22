using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public record SizeInput(int SizeMl, decimal Price, bool Enabled);

public record ProductInput(
    string Name,
    string Slug,
    string Brand,
    string Category,
    string Description,
    string Top,
    string Heart,
    string Base,
    string Concentration,
    string Tags,
    string SeoTitle,
    string SeoDescription,
    bool Active,
    bool Featured,
    int Threshold,
    SizeInput[] Variants,
    int? OpeningStockMl = null
);

public record Adjustment(int Delta, string Type, string Reason, Guid Key);

public record ImageEdit(int Id, string Alt);

public static class Catalog
{
    public static void Map(RouteGroupBuilder api, RouteGroupBuilder admin)
    {
        api.MapGet("/categories", () => Seed.Categories);
        api.MapGet("/brands", (ShopDb db) =>
            db.Perfumes.Where(p => p.Active).Select(p => p.Brand).Distinct().Order().ToListAsync()
        );
        api.MapGet("/perfumes", Search).RequireRateLimiting("browse");
        api.MapGet("/perfumes/{slug}", async (string slug, ShopDb db) =>
            await db
                .Perfumes.AsNoTracking()
                .Include(p => p.Variants)
                .Include(p => p.Images)
                .SingleOrDefaultAsync(p => p.Active && p.Slug == slug)
                is Perfume p
                ? Results.Ok(View(p))
                : Results.NotFound()
        );
        admin.MapGet("/perfumes", async (ShopDb db) =>
            (
                await db
                    .Perfumes.AsNoTracking()
                    .Include(p => p.Images)
                    .Include(p => p.Variants)
                    .OrderBy(p => p.Name)
                    .ToListAsync()
            ).Select(View)
        );
        admin.MapGet("/perfumes/{id:int}", async (int id, ShopDb db) =>
            await db
                .Perfumes.Include(p => p.Images)
                .Include(p => p.Variants)
                .SingleOrDefaultAsync(p => p.Id == id)
                is Perfume p
                ? Results.Ok(View(p))
                : Results.NotFound()
        );
        admin.MapPost("/perfumes", (ProductInput input, ShopDb db, HttpContext ctx) =>
            Save(null, input, db, ctx)
        );
        admin.MapPatch(
            "/perfumes/{id:int}",
            (int id, ProductInput input, ShopDb db, HttpContext ctx) => Save(id, input, db, ctx)
        );
        admin.MapGet("/inventory", (ShopDb db) =>
            db
                .Perfumes.OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.OnHandMl,
                    p.ReservedMl,
                    availableMl = p.OnHandMl - p.ReservedMl,
                    p.Threshold,
                })
                .ToListAsync()
        );
        admin.MapGet("/inventory/{id:int}/movements", (int id, ShopDb db) =>
            db
                .Movements.Where(m => m.PerfumeId == id)
                .OrderByDescending(m => m.Id)
                .Take(200)
                .ToListAsync()
        );
        admin.MapPost("/inventory/{id:int}/movements", Adjust);
    }

    public static object View(Perfume p) =>
        new
        {
            p.Id,
            p.Name,
            p.Slug,
            p.Brand,
            p.Category,
            p.Description,
            p.Top,
            p.Heart,
            p.Base,
            p.Concentration,
            p.Tags,
            p.SeoTitle,
            p.SeoDescription,
            p.Active,
            p.Featured,
            p.Threshold,
            availableMl = p.OnHandMl - p.ReservedMl,
            variants = p
                .Variants.OrderBy(v => v.SizeMl)
                .Select(v => new
                {
                    v.SizeMl,
                    v.Price,
                    v.Enabled,
                }),
            images = p
                .Images.OrderBy(i => i.Position)
                .ThenBy(i => i.Id)
                .Select(i => new
                {
                    i.Id,
                    i.Path,
                    i.Alt,
                }),
        };

    static async Task<IResult> Search(
        ShopDb db,
        string? search,
        string? category,
        string? brand,
        string? sort,
        int? page,
        int? pageSize,
        bool? available,
        bool? featured,
        bool? soldOnly,
        decimal? minPrice,
        decimal? maxPrice
    )
    {
        var q = db.Perfumes.AsNoTracking().Where(p => p.Active);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            q = q.Where(p =>
                (p.Name + " " + p.Brand + " " + p.Tags + " " + p.Top + " " + p.Heart + " " + p.Base)
                    .ToLower()
                    .Contains(term)
            );
        }
        if (category != null)
            q = q.Where(p => p.Category == category);
        if (brand != null)
            q = q.Where(p => p.Brand == brand);
        if (featured == true)
            q = q.Where(p => p.Featured);
        var rows = q.Select(p => new
        {
            p,
            price = p
                .Variants.Where(v => v.Enabled && v.SizeMl <= p.OnHandMl - p.ReservedMl)
                .Min(v => (decimal?)v.Price),
            sales = db.Lines.Where(l =>
                    l.PerfumeId == p.Id
                    && db.Orders.Any(o =>
                        o.Id == l.OrderId
                        && o.Payment == "Paid"
                        && o.Fulfillment != "Cancelled"
                        && o.Fulfillment != "Returned"
                    )
                )
                .Sum(l => (int?)l.Quantity)
                ?? 0,
        });
        if (available == true)
            rows = rows.Where(r => r.price != null);
        if (minPrice != null)
            rows = rows.Where(r => r.price >= minPrice);
        if (maxPrice != null)
            rows = rows.Where(r => r.price <= maxPrice);
        if (soldOnly == true)
            rows = rows.Where(r => r.sales > 0);
        var ordered = sort switch
        {
            "price-asc" => rows.OrderBy(r => r.price == null).ThenBy(r => r.price),
            "price-desc" => rows.OrderBy(r => r.price == null).ThenByDescending(r => r.price),
            "name" => rows.OrderBy(r => r.p.Name),
            "best" => rows.OrderByDescending(r => r.sales),
            _ => rows.OrderByDescending(r => r.p.CreatedAt),
        };
        int size = Math.Clamp(pageSize ?? 12, 1, 60),
            number = Math.Clamp(page ?? 1, 1, 100000);
        var ids = await ordered
            .ThenBy(r => r.p.Id)
            .Skip((number - 1) * size)
            .Take(size)
            .Select(r => r.p.Id)
            .ToListAsync();
        var products = await db
            .Perfumes.AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);
        return Results.Ok(
            new
            {
                items = ids.Where(products.ContainsKey).Select(id => View(products[id])),
                page = number,
                pageSize = size,
                total = await rows.CountAsync(),
            }
        );
    }

    public static string Field(string? value, int max, bool required = false)
    {
        var text = value?.Trim() ?? "";
        if (text.Length > max || (required && text.Length == 0))
            throw new ShopError(
                $"Enter a value between {(required ? 1 : 0)} and {max} characters."
            );
        return text;
    }

    public static async Task<Perfume> Lock(ShopDb db, int id) =>
        await db
            .Perfumes.FromSqlInterpolated(
                $"SELECT * FROM \"Perfumes\" WHERE \"Id\"={id} FOR UPDATE"
            )
            .SingleOrDefaultAsync()
        ?? throw new ShopError("Fragrance was not found.", 404);

    public static string Actor(HttpContext ctx) => ctx.User.Identity?.Name ?? "system";

    public static void Log(ShopDb db, string actor, string action) =>
        db.Add(new Audit { Actor = actor, Action = action });

    static async Task<IResult> Save(int? id, ProductInput input, ShopDb db, HttpContext ctx)
    {
        if (id.HasValue && input.OpeningStockMl.HasValue)
            throw new ShopError(
                "Use an inventory adjustment to change stock on an existing fragrance."
            );
        int openingStock = input.OpeningStockMl ?? 0;
        if (openingStock is < 0 or > 1_000_000)
            throw new ShopError("Opening stock must be between 0 and 1,000,000 ml.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = id.HasValue ? await Lock(db, id.Value) : new Perfume { OnHandMl = openingStock };
        if (id.HasValue)
            await db.Entry(p).Collection(x => x.Variants).LoadAsync();
        else
            db.Add(p);
        p.Name = Field(input.Name, 100, true);
        p.Brand = Field(input.Brand, 100, true);
        p.Slug = Field(input.Slug, 120, true);
        if (
            !Regex.IsMatch(p.Slug, "^[a-z0-9]+(-[a-z0-9]+)*$")
            || !Seed.Categories.Contains(input.Category)
        )
            throw new ShopError("Choose a valid category and lowercase URL slug.");
        p.Category = input.Category;
        p.Description = Field(input.Description, 5000, true);
        p.Top = Field(input.Top, 300);
        p.Heart = Field(input.Heart, 300);
        p.Base = Field(input.Base, 300);
        p.Concentration = Field(input.Concentration, 100);
        p.Tags = Field(input.Tags, 500);
        p.SeoTitle = Field(input.SeoTitle, 150);
        p.SeoDescription = Field(input.SeoDescription, 400);
        p.Active = input.Active;
        p.Featured = input.Featured;
        if (
            input.Threshold < 0
            || input.Threshold > 1000000
            || input.Variants == null
            || !input.Variants.Select(v => v.SizeMl).Order().SequenceEqual(Seed.Sizes)
        )
            throw new ShopError("Supply a stock threshold and all five decant sizes.");
        p.Threshold = input.Threshold;
        foreach (var v in input.Variants)
        {
            if (v.Price <= 0 || v.Price > 1000000 || decimal.Round(v.Price, 2) != v.Price)
                throw new ShopError("Enter a positive BDT price with at most two decimal places.");
            var item = p.Variants.SingleOrDefault(x => x.SizeMl == v.SizeMl);
            if (item == null)
            {
                item = new Variant { SizeMl = v.SizeMl };
                p.Variants.Add(item);
            }
            item.Enabled = v.Enabled;
            item.Price = v.Price;
        }
        Log(db, Actor(ctx), $"Save listing {p.Slug}");
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e)
            when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw new ShopError("This slug is already taken.", 409);
        }
        if (openingStock > 0)
        {
            db.Movements.Add(
                new Movement
                {
                    PerfumeId = p.Id,
                    Hand = openingStock,
                    Type = "Restock",
                    Reason = "Opening stock",
                    Actor = Actor(ctx),
                    Key = $"opening:{p.Id}",
                }
            );
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return Results.Ok(new { p.Id });
    }

    static async Task<IResult> Adjust(int id, Adjustment a, ShopDb db, HttpContext ctx)
    {
        if (
            a.Key == Guid.Empty
            || a.Delta == 0
            || Math.Abs((long)a.Delta) > 1000000
            || !new[] { "Restock", "Correction", "Spillage" }.Contains(a.Type)
            || a.Type == "Restock" && a.Delta < 0
            || a.Type == "Spillage" && a.Delta > 0
        )
            throw new ShopError("Check movement type and signed milliliter quantity.");
        var reason = Field(a.Reason, 500, true);
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = await Lock(db, id);
        var key = $"adjust:{id}:{a.Key}";
        if (await db.Movements.SingleOrDefaultAsync(m => m.Key == key) is Movement previous)
        {
            if (previous.Hand != a.Delta || previous.Type != a.Type || previous.Reason != reason)
                throw new ShopError("Movement key already used.", 409);
            return Results.NoContent();
        }
        if ((long)p.OnHandMl + a.Delta < p.ReservedMl || (long)p.OnHandMl + a.Delta > int.MaxValue)
            throw new ShopError("Adjustment exceeds available stock.", 409);
        p.OnHandMl += a.Delta;
        db.Add(
            new Movement
            {
                PerfumeId = id,
                Hand = a.Delta,
                Type = a.Type,
                Reason = reason,
                Actor = Actor(ctx),
                Key = key,
            }
        );
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.NoContent();
    }
}
