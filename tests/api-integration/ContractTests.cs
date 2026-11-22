using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FragSplit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

[Collection("shop")]
public class ContractTests(ShopFixture f)
{
    [Fact]
    public async Task OpenApiIncludesPublicAndProtectedRoutes()
    {
        using var c = f.CreateClient();
        var doc = await c.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = doc.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/checkout/quote", out _));
        Assert.True(paths.TryGetProperty("/api/v1/admin/orders", out _));
    }

    [Fact]
    public async Task RepricedQuoteMustBeReviewedBeforeReservation()
    {
        using var c = f.CreateClient();
        var id = await OrderTests.Product(f, 20);
        var s = await OrderTests.Prepare(c, id);
        await f.Db(db =>
            db.Variants.Where(v => v.PerfumeId == id)
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.Price, 250))
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await c.PostAsJsonAsync("/api/v1/orders", s)).StatusCode
        );
        Assert.Equal(
            0,
            await f.Db(db =>
                db.Perfumes.Where(p => p.Id == id).Select(p => p.ReservedMl).SingleAsync()
            )
        );
    }

    [Fact]
    public async Task ConcurrentSameKeyCreatesOneReceiptAndMovement()
    {
        using var c = f.CreateClient();
        var id = await OrderTests.Product(f, 10);
        var s = await OrderTests.Prepare(c, id);
        var attempts = await Task.WhenAll(
            c.PostAsJsonAsync("/api/v1/orders", s),
            c.PostAsJsonAsync("/api/v1/orders", s)
        );
        foreach (var r in attempts)
            r.EnsureSuccessStatusCode();
        var receipt = await attempts[0].Content.ReadFromJsonAsync<JsonElement>();
        var recovery = await c.PostAsJsonAsync(
            "/api/v1/orders/recover",
            new { key = s.RequestKey }
        );
        recovery.EnsureSuccessStatusCode();
        Assert.Equal(
            receipt.GetProperty("reference").GetString(),
            (await recovery.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("reference")
                .GetString()
        );
        Assert.Equal(1, await f.Db(db => db.Movements.CountAsync(m => m.PerfumeId == id)));
        await Assert.ThrowsAsync<PostgresException>(() =>
            f.Db(db =>
                db.Movements.Where(m => m.PerfumeId == id)
                    .ExecuteUpdateAsync(u => u.SetProperty(m => m.Hand, 1))
            )
        );
    }
}
