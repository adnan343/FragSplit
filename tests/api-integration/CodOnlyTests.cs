using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

[Collection("shop")]
public class CodOnlyTests(ShopFixture f)
{
    [Fact]
    public async Task StoreOnlyAcceptsCashOnDelivery()
    {
        using var client = f.CreateClient();
        var settings = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        Assert.False(settings.GetProperty("online").GetBoolean());
        var id = await OrderTests.Product(f, 20);
        var cod = await OrderTests.Prepare(client, id);
        var online = cod with { Basket = cod.Basket with { Method = "Online" } };
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/v1/checkout/quote", online.Basket)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/v1/orders", online)).StatusCode
        );
        foreach (var route in new[] { "initiate", "ipn", "success", "fail", "cancel" })
            Assert.Equal(
                HttpStatusCode.NotFound,
                (
                    await client.PostAsJsonAsync(
                        "/api/v1/payments/sslcommerz/" + route,
                        new { reference = "FS-example" }
                    )
                ).StatusCode
            );
    }
}
