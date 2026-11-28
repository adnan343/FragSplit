using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public static class Seed
{
    public static readonly string[] Categories = ["Middle Eastern", "Designer", "Niche"];
    public static readonly int[] Sizes = [3, 5, 10, 15, 30];

    public static async Task Run(
        ShopDb db,
        IConfiguration config,
        IDataProtectionProvider protection,
        bool resetAdmin
    )
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(8493021)");
        if (!await db.Settings.AnyAsync())
        {
            db.Settings.Add(new Settings());
            db.Zones.AddRange(
                new Zone
                {
                    Name = "Dhaka city",
                    DhakaCity = true,
                    Charge = 70,
                    Cod = true,
                },
                new Zone
                {
                    Name = "Outside Dhaka",
                    Charge = 130,
                    Cod = true,
                }
            );
        }
        var email = config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant();
        if (resetAdmin && string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Set ADMIN_EMAIL to reset an administrator.");
        if (!string.IsNullOrEmpty(email))
        {
            var existing = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email);
            if (existing != null && existing.Role != "Admin")
                throw new InvalidOperationException("The bootstrap email belongs to a customer.");
            if (resetAdmin && existing == null)
                throw new InvalidOperationException("No administrator has the configured email.");
            var user = existing ?? new Account { Email = email, Role = "Admin" };
            if (existing == null || resetAdmin)
            {
                var password = config["ADMIN_PASSWORD"];
                if (password is not { Length: >= 14 and <= 128 })
                    throw new InvalidOperationException(
                        "Supply an admin password of 14 to 128 characters."
                    );
                user.Hash = new PasswordHasher<Account>().HashPassword(user, password);
            }
            if (user.AdminTotpKey == null || resetAdmin)
                AdminAccess.Enroll(user, config["ADMIN_TOTP_SECRET"], protection);
            if (existing == null)
                db.Add(user);
        }
        if (config.GetValue<bool>("SEED_DEMO") && !await db.Perfumes.AnyAsync())
        {
            var samples = new[]
            {
                (
                    "Amber Dusk",
                    "Maison Serein",
                    "Middle Eastern",
                    "amber",
                    "Saffron",
                    "Labdanum",
                    "Amber"
                ),
                (
                    "Bergamot Veil",
                    "Atelier Clair",
                    "Designer",
                    "citrus",
                    "Bergamot",
                    "Neroli",
                    "White musk"
                ),
                ("Cedar Archive", "Studio Sylve", "Niche", "woods", "Cardamom", "Cedar", "Vetiver"),
                ("Petal Hour", "Atelier Clair", "Designer", "floral", "Pear", "Rose", "Iris"),
                (
                    "Saffron Smoke",
                    "Maison Serein",
                    "Middle Eastern",
                    "amber",
                    "Saffron",
                    "Incense",
                    "Oud"
                ),
                ("Green Reverie", "Studio Sylve", "Niche", "citrus", "Petitgrain", "Tea", "Moss"),
            };
            int i = 0;
            foreach (var (name, brand, category, art, top, heart, baseNote) in samples)
            {
                var p = new Perfume
                {
                    Name = name,
                    Slug = name.ToLowerInvariant().Replace(' ', '-'),
                    Brand = brand,
                    Category = category,
                    Description =
                        $"An imagined composition opening with {top.ToLowerInvariant()}, settling into {heart.ToLowerInvariant()} and {baseNote.ToLowerInvariant()}. Fictional demonstration fragrance.",
                    Top = top,
                    Heart = heart,
                    Base = baseNote,
                    Concentration = "Eau de parfum",
                    Featured = i < 3,
                    OnHandMl = 180,
                    Tags = "Demo collection",
                };
                var prices = new decimal[] { 360, 540, 980, 1390, 2600 };
                p.Variants = Sizes
                    .Select(
                        (s, n) =>
                            new Variant
                            {
                                SizeMl = s,
                                Price = prices[n] + i * 35,
                                Enabled = true,
                            }
                    )
                    .ToList();
                p.Images.Add(
                    new ProductImage
                    {
                        Path = $"/art/demo-{art}.webp",
                        Alt = $"Illustrated {art} study, demo artwork",
                    }
                );
                db.Add(p);
                await db.SaveChangesAsync();
                db.Add(
                    new Movement
                    {
                        PerfumeId = p.Id,
                        Hand = 180,
                        Type = "Restock",
                        Reason = "Demo opening stock",
                        Actor = "seed",
                        Key = $"seed:{p.Id}",
                    }
                );
                i++;
            }
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
