using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Perfume> Perfumes => Set<Perfume>();
    public DbSet<Variant> Variants => Set<Variant>();
    public DbSet<ProductImage> Images => Set<ProductImage>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<ConfirmationMail> ConfirmationMails => Set<ConfirmationMail>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> Lines => Set<OrderLine>();
    public DbSet<History> History => Set<History>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Settings> Settings => Set<Settings>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<SavedCartLine> SavedCartLines => Set<SavedCartLine>();
    public DbSet<Audit> Audits => Set<Audit>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Perfume>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Perfume>()
            .HasIndex(x => new
            {
                x.Active,
                x.Category,
                x.Brand,
            });
        b.Entity<Perfume>()
            .ToTable(t =>
                t.HasCheckConstraint(
                    "stock_bounds",
                    "\"ReservedMl\" >= 0 AND \"OnHandMl\" >= \"ReservedMl\" AND \"Threshold\" >= 0"
                )
            );
        b.Entity<Variant>().HasKey(x => new { x.PerfumeId, x.SizeMl });
        b.Entity<Variant>()
            .ToTable(t =>
                t.HasCheckConstraint("size_price", "\"SizeMl\" IN (3,5,10,15,30) AND \"Price\" > 0")
            );
        b.Entity<Order>().HasIndex(x => x.PaidAt);
        b.Entity<Order>().HasIndex(x => x.CreatedAt);
        b.Entity<History>().HasIndex(x => new { x.Event, x.At });
        b.Entity<ConfirmationMail>().HasIndex(m => m.OrderId).IsUnique();
        b.Entity<ConfirmationMail>().HasIndex(m => m.NextAttempt).HasFilter("\"SentAt\" IS NULL");
        b.Entity<ConfirmationMail>().HasOne<Order>().WithMany().HasForeignKey(m => m.OrderId);
        b.Entity<Order>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<Order>().HasIndex(x => x.RequestKey).IsUnique();
        b.Entity<Order>().HasIndex(x => new { x.StockState, x.ExpiresAt });
        b.Entity<Order>().HasIndex(x => x.ValidationId).IsUnique();
        b.Entity<Order>()
            .HasOne<Account>()
            .WithMany()
            .HasForeignKey(o => o.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>().HasIndex(o => new { o.AccountId, o.Id });
        b.Entity<SavedCartLine>()
            .HasKey(l => new
            {
                l.AccountId,
                l.PerfumeId,
                l.SizeMl,
            });
        b.Entity<SavedCartLine>().HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId);
        b.Entity<SavedCartLine>()
            .HasOne<Variant>()
            .WithMany()
            .HasForeignKey(l => new { l.PerfumeId, l.SizeMl });
        b.Entity<Account>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Movement>().HasIndex(x => x.Key).IsUnique();
        b.Entity<Movement>().HasOne<Perfume>().WithMany().HasForeignKey(x => x.PerfumeId);
        b.Entity<Movement>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        b.Entity<OrderLine>().HasOne<Perfume>().WithMany().HasForeignKey(x => x.PerfumeId);
        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties().Where(x => x.ClrType == typeof(decimal)))
        {
            property.SetPrecision(12);
            property.SetScale(2);
        }
    }
}

public class Perfume
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Top { get; set; } = "";
    public string Heart { get; set; } = "";
    public string Base { get; set; } = "";
    public string Concentration { get; set; } = "";
    public string Tags { get; set; } = "";
    public string SeoTitle { get; set; } = "";
    public string SeoDescription { get; set; } = "";
    public bool Active { get; set; } = true;
    public bool Featured { get; set; }
    public int OnHandMl { get; set; }
    public int ReservedMl { get; set; }
    public int Threshold { get; set; } = 30;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Variant> Variants { get; set; } = [];
    public List<ProductImage> Images { get; set; } = [];
}

public class Variant
{
    public int PerfumeId { get; set; }
    public int SizeMl { get; set; }
    public decimal Price { get; set; }
    public bool Enabled { get; set; }
}

public class ProductImage
{
    public int Id { get; set; }
    public int PerfumeId { get; set; }
    public string Path { get; set; } = "";
    public string Alt { get; set; } = "";
    public int Position { get; set; }
}

public class Order
{
    public int? AccountId { get; set; }
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string Area { get; set; } = "";
    public string Postcode { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Region { get; set; } = "";
    public string Method { get; set; } = "";
    public string Payment { get; set; } = "Unpaid";
    public string Fulfillment { get; set; } = "Pending";
    public string StockState { get; set; } = "Reserved";
    public string Tracking { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? SessionKey { get; set; }
    public string? GatewayUrl { get; set; }
    public string? ValidationId { get; set; }
    public string Verification { get; set; } = "";
    public bool NeedsReview { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
    public List<History> History { get; set; } = [];
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int PerfumeId { get; set; }
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Category { get; set; } = "";
    public int SizeMl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Movement
{
    public long Id { get; set; }
    public int PerfumeId { get; set; }
    public int? OrderId { get; set; }
    public int Hand { get; set; }
    public int Reserved { get; set; }
    public string Type { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Key { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class History
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Event { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class Zone
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Charge { get; set; }
    public bool Cod { get; set; }
    public bool Active { get; set; } = true;
}

public class Settings
{
    public int Id { get; set; }
    public string Name { get; set; } = "FragSplit";
    public string Logo { get; set; } = "";
    public string Contact { get; set; } = "";
    public string ShippingPolicy { get; set; } = "Delivery charges are shown at checkout.";
    public string ReturnPolicy { get; set; } =
        "Contact the store with your order reference for return requests.";
}

public class SavedCartLine
{
    public int AccountId { get; set; }
    public int PerfumeId { get; set; }
    public int SizeMl { get; set; }
    public int Quantity { get; set; }
}

public class Account
{
    public Guid CartVersion { get; set; }
    public int SessionVersion { get; set; }
    public string? RecoveryHash { get; set; }
    public string DisplayName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Street { get; set; } = "";
    public string Area { get; set; } = "";
    public string City { get; set; } = "";
    public string Postcode { get; set; } = "";
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Role { get; set; } = "Customer";
}

public class Audit
{
    public long Id { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}
