using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FragSplit;

public sealed class DesignFactory : IDesignTimeDbContextFactory<ShopDb>
{
    public ShopDb CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<ShopDb>()
                .UseNpgsql(
                    Environment.GetEnvironmentVariable("ConnectionStrings__Shop")
                        ?? "Host=localhost;Database=fragsplit"
                )
                .Options
        );
}
