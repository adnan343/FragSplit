using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

[Collection("shop")]
public class CatalogTests(ShopFixture fixture)
{
    [Fact]
    public async Task AnonymousCannotEditInventory()
    {
        using var client = fixture.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/admin/inventory")).StatusCode
        );
    }

    [Theory]
    [InlineData("Middle Eastern")]
    [InlineData("Designer")]
    [InlineData("Niche")]
    public async Task SeededCollectionCanBeDiscovered(string category)
    {
        using var client = fixture.CreateClient();
        var data = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/perfumes?sort=price-asc&category=" + Uri.EscapeDataString(category)
        );
        Assert.NotEmpty(data.GetProperty("items").EnumerateArray());
        Assert.All(data.GetProperty("items").EnumerateArray(), p =>
            Assert.Equal(category, p.GetProperty("category").GetString())
        );
    }
}
