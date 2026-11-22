using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FragSplit;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Collection("shop")]
public class OrderTests(ShopFixture f)
{
    public static async Task<int> Product(ShopFixture f, int stock)
    {
        return await f.Db(async db =>
        {
            var p = new Perfume
            {
                Name = "Test fragrance",
                Brand = "Integration",
                Category = "Niche",
                Slug = "test-" + Guid.NewGuid(),
                OnHandMl = stock,
                Variants =
                [
                    new Variant
                    {
                        SizeMl = 5,
                        Enabled = true,
                        Price = 115,
                    },
                    new Variant
                    {
                        SizeMl = 10,
                        Enabled = true,
                        Price = 205,
                    },
                ],
            };
            db.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        });
    }

    public static async Task<Submission> Prepare(
        HttpClient client,
        int id,
        int size = 5,
        int qty = 2,
        string method = "COD"
    )
    {
        var basket = new Basket([new CartItem(id, size, qty)], 1, method);
        var quote = await client.PostAsJsonAsync("/api/v1/checkout/quote", basket);
        quote.EnsureSuccessStatusCode();
        var q = await quote.Content.ReadFromJsonAsync<JsonElement>();
        return new(
            basket,
            new Customer(
                "Test Customer",
                "01700000000",
                "shopper@example.test",
                "19 Sample Road",
                "Dhaka",
                "Dhanmondi",
                "1209",
                null
            ),
            true,
            q.GetProperty("signature").GetString()!,
            Guid.NewGuid()
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing-domain@")]
    public async Task GuestCodRequiresAValidEmail(string email)
    {
        using var client = f.CreateClient();
        var id = await Product(f, 10);
        var submission = await Prepare(client, id);
        var response = await client.PostAsJsonAsync(
            "/api/v1/orders",
            submission with
            {
                Customer = submission.Customer with { Email = email },
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            0,
            await f.Db(db =>
                db.Perfumes.Where(p => p.Id == id).Select(p => p.ReservedMl).SingleAsync()
            )
        );
    }

    [Fact]
    public async Task ConcurrentCheckoutsCannotOversell()
    {
        var id = await Product(f, 10);
        using var client = f.CreateClient();
        var a = await Prepare(client, id);
        var b = a with { RequestKey = Guid.NewGuid() };
        var outcomes = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/orders", a),
            client.PostAsJsonAsync("/api/v1/orders", b)
        );
        Assert.Single(outcomes, r => r.IsSuccessStatusCode);
        Assert.Single(outcomes, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(
            10,
            await f.Db(db =>
                db.Perfumes.Where(p => p.Id == id).Select(p => p.ReservedMl).SingleAsync()
            )
        );
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(10, 1)]
    public async Task CodStockSnapshotsAndCollection(int size, int qty)
    {
        var id = await Product(f, 20);
        using var client = f.CreateClient();
        var s = await Prepare(client, id, size, qty);
        var result = await client.PostAsJsonAsync("/api/v1/orders", s);
        result.EnsureSuccessStatusCode();
        var receipt = await result.Content.ReadFromJsonAsync<JsonElement>();
        var reference = receipt.GetProperty("reference").GetString();
        var repeat = await client.PostAsJsonAsync("/api/v1/orders", s);
        repeat.EnsureSuccessStatusCode();
        Assert.Equal(
            reference,
            (await repeat.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("reference")
                .GetString()
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/orders/" + reference + "/status")).StatusCode
        );
        client.DefaultRequestHeaders.Add("X-Shop-Request", "1");
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = Environment.GetEnvironmentVariable("ADMIN_EMAIL"),
                password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD"),
            }
        );
        login.EnsureSuccessStatusCode();
        foreach (var action in new[] { "Confirmed", "Processing", "Shipped", "Delivered" })
            (
                await client.PatchAsJsonAsync(
                    "/api/v1/admin/orders/" + reference + "/status",
                    new Change(action, "test", null)
                )
            ).EnsureSuccessStatusCode();
        Assert.Equal(
            "Unpaid",
            await f.Db(db =>
                db.Orders.Where(o => o.Reference == reference).Select(o => o.Payment).SingleAsync()
            )
        );
        (
            await client.PatchAsJsonAsync(
                "/api/v1/admin/orders/" + reference + "/status",
                new Change("Collect", null, null)
            )
        ).EnsureSuccessStatusCode();
        await f.Db(async db =>
        {
            var p = await db.Perfumes.FindAsync(id);
            Assert.Equal(10, p!.OnHandMl);
            Assert.Equal(0, p.ReservedMl);
            p.Name = "Updated after purchase";
            await db.SaveChangesAsync();
            return 0;
        });
        client.DefaultRequestHeaders.Add("X-Order-Token", receipt.GetProperty("token").GetString());
        var o = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/orders/" + reference + "/status"
        );
        Assert.Equal("Paid", o.GetProperty("payment").GetString());
        Assert.Equal("Test fragrance", o.GetProperty("lines")[0].GetProperty("name").GetString());
        Assert.Equal((size == 5 ? 230 : 205) + 70, o.GetProperty("total").GetDecimal());
    }
}
