using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public record StoreInput(
    string Name,
    string? Logo,
    string? Contact,
    string ShippingPolicy,
    string ReturnPolicy,
    Zone[] Zones
);

public static class SettingsRoutes
{
    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapGet("/analytics/overview", Metrics.Overview);
        admin.MapGet("/settings", async (ShopDb db) =>
            new
            {
                store = await db.Settings.SingleAsync(),
                zones = await db.Zones.OrderBy(z => z.Id).ToListAsync(),
            }
        );
        admin.MapPatch("/settings", async (StoreInput input, ShopDb db, HttpContext ctx) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var store = await db
                .Settings.FromSqlRaw("SELECT * FROM \"Settings\" FOR UPDATE")
                .SingleAsync();
            store.Name = Catalog.Field(input.Name, 80, true);
            store.Contact = Catalog.Field(input.Contact, 200);
            store.Logo = Catalog.Field(input.Logo, 400);
            if (
                store.Logo != ""
                && !(store.Logo.StartsWith("/art/") || store.Logo.StartsWith("/api/v1/media/"))
            )
                throw new ShopError("Use an uploaded media path or bundled artwork for the logo.");
            store.ShippingPolicy = Catalog.Field(input.ShippingPolicy, 4000, true);
            store.ReturnPolicy = Catalog.Field(input.ReturnPolicy, 4000, true);
            if (
                input.Zones == null
                || input.Zones.Length is < 1 or > 100
                || !input.Zones.Any(z => z.Active)
                || input.Zones.Where(z => z.Id != 0).GroupBy(z => z.Id).Any(g => g.Count() > 1)
            )
                throw new ShopError(
                    "Configure at least one active delivery region and unique IDs."
                );
            var zones = await db.Zones.ToListAsync();
            foreach (var z in zones)
                z.Active = false;
            foreach (var entry in input.Zones)
            {
                var z =
                    entry.Id == 0
                        ? new Zone()
                        : zones.SingleOrDefault(x => x.Id == entry.Id)
                            ?? throw new ShopError("Unknown region.");
                if (
                    entry.Charge is < 0 or > 100000
                    || decimal.Round(entry.Charge, 2) != entry.Charge
                )
                    throw new ShopError("Enter a valid delivery charge.");
                z.Name = Catalog.Field(entry.Name, 120, true);
                z.Charge = entry.Charge;
                z.Active = entry.Active;
                z.Cod = entry.Cod;
                if (entry.Id == 0)
                    db.Add(z);
            }
            Catalog.Log(db, Catalog.Actor(ctx), "Updated branding, policies and delivery rules");
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
    }
}
