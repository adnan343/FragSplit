using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FragSplit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
                "017" + Random.Shared.Next(100_000_000).ToString("D8"),
                Guid.NewGuid() + "@example.test",
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

    [Fact]
    public async Task CodDeliveryRegionMustMatchTheAddress()
    {
        using var client = f.CreateClient();
        var id = await Product(f, 20);
        var submission = await Prepare(client, id);
        var customer = submission.Customer with { City = "Chattogram" };
        var rejected = await client.PostAsJsonAsync(
            "/api/v1/orders",
            submission with
            {
                Customer = customer,
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(
            0,
            await f.Db(db =>
                db.Perfumes.Where(p => p.Id == id).Select(p => p.ReservedMl).SingleAsync()
            )
        );

        var zones = await f.Db(db =>
            db.Zones.Where(z => z.Active && z.Cod && !z.DhakaCity).Select(z => z.Id).ToListAsync()
        );
        var basket = submission.Basket with { ZoneId = Assert.Single(zones) };
        var quote = await client.PostAsJsonAsync("/api/v1/checkout/quote", basket);
        quote.EnsureSuccessStatusCode();
        var priced = await quote.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(130m, priced.GetProperty("shipping").GetDecimal());
        var accepted = await client.PostAsJsonAsync(
            "/api/v1/orders",
            submission with
            {
                Customer = customer,
                Basket = basket,
                Quote = priced.GetProperty("signature").GetString()!,
            }
        );
        accepted.EnsureSuccessStatusCode();
        Assert.StartsWith(
            ReceiptRecovery.Prefix,
            (await accepted.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("token")
                .GetString()
        );
    }

    [Fact]
    public async Task ConcurrentCodRequestsShareTheContactCapAndExpiredHoldsReleaseStock()
    {
        int id = await Product(f, 100);
        using var client = f.CreateClient();
        var first = await Prepare(client, id);
        var outcomes = await Task.WhenAll(
            Enumerable
                .Range(0, 3)
                .Select(i =>
                    client.PostAsJsonAsync(
                        "/api/v1/orders",
                        first with
                        {
                            RequestKey = Guid.NewGuid(),
                            Customer = first.Customer with
                            {
                                Phone =
                                    i == 0 ? "+88 " + first.Customer.Phone : first.Customer.Phone,
                                Email = $"{i}-{first.Customer.Email}",
                            },
                        }
                    )
                )
        );
        Assert.Equal(2, outcomes.Count(r => r.IsSuccessStatusCode));
        Assert.Single(outcomes, r => r.StatusCode == HttpStatusCode.Conflict);
        var expired = await f.Db(async db =>
        {
            Assert.Equal(20, (await db.Perfumes.FindAsync(id))!.ReservedMl);
            var order = await db
                .Orders.Where(o => o.Lines.Any(l => l.PerfumeId == id))
                .OrderBy(o => o.Id)
                .FirstAsync();
            order.ExpiresAt = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
            return order.Reference;
        });
        using var admin = await f.LoginAdmin();
        var tooLate = await admin.PatchAsJsonAsync(
            $"/api/v1/admin/orders/{expired}/status",
            new Change("Confirmed", null, null)
        );
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);
        using var scope = f.Services.CreateScope();
        var reservations = scope.ServiceProvider.GetRequiredService<Reservations>();
        await reservations.ExpireDue();
        await reservations.ExpireDue();
        await f.Db(async db =>
        {
            var order = await db.Orders.SingleAsync(o => o.Reference == expired);
            Assert.Equal("Cancelled", order.Fulfillment);
            Assert.Equal("Unpaid", order.Payment);
            Assert.Equal("Released", order.StockState);
            Assert.Equal(
                1,
                await db.Movements.CountAsync(m => m.OrderId == order.Id && m.Type == "Release")
            );
            Assert.Equal(10, (await db.Perfumes.FindAsync(id))!.ReservedMl);
            return 0;
        });
        (await client.PostAsJsonAsync("/api/v1/orders", first)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GuestReceiptRecoveryOnlyEmailsTheOrderAddressAndExpires()
    {
        int id = await Product(f, 30);
        using var buyer = f.CreateClient();
        using var another = f.CreateClient();
        var submission = await Prepare(buyer, id);
        var response = await buyer.PostAsJsonAsync("/api/v1/orders", submission);
        var reference = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("reference")
            .GetString()!;
        var status = $"/api/v1/orders/{reference}/status";
        Assert.Equal(HttpStatusCode.NotFound, (await another.GetAsync(status)).StatusCode);
        const string wrong = "wrong-address@example.test";
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await another.PostAsJsonAsync(
                    "/api/v1/orders/receipt-link",
                    new ReceiptLink(reference, wrong)
                )
            ).StatusCode
        );
        Assert.False(f.Mail.Messages.ContainsKey(wrong));
        var email = submission.Customer.Email!;
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await another.PostAsJsonAsync(
                    "/api/v1/orders/receipt-link",
                    new ReceiptLink(reference, email.ToUpperInvariant())
                )
            ).StatusCode
        );
        var token = f.Mail.Token(email);
        Assert.StartsWith(ReceiptRecovery.Prefix, token);
        another.DefaultRequestHeaders.Add("X-Order-Token", token);
        (await another.GetAsync(status)).EnsureSuccessStatusCode();
        var expired = f
            .Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("vialmuse.guest-receipt.v1")
            .ToTimeLimitedDataProtector()
            .Protect(reference, DateTimeOffset.UtcNow.AddMinutes(-1));
        another.DefaultRequestHeaders.Remove("X-Order-Token");
        another.DefaultRequestHeaders.Add("X-Order-Token", ReceiptRecovery.Prefix + expired);
        Assert.Equal(HttpStatusCode.NotFound, (await another.GetAsync(status)).StatusCode);
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
        using var admin = await f.LoginAdmin();
        foreach (var action in new[] { "Confirmed", "Processing", "Shipped", "Delivered" })
            (
                await admin.PatchAsJsonAsync(
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
            await admin.PatchAsJsonAsync(
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
