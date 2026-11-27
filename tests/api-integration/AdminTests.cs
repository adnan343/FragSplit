using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FragSplit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Collection("shop")]
public class AdminTests(ShopFixture f)
{
    [Fact]
    public async Task AdminRequiresAnAuthenticatorAndRejectsCodeReplay()
    {
        using var client = f.NewAdminClient();
        using var another = f.NewAdminClient();
        var credentials = f.AdminCredentials();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/auth/login", credentials)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await client.PostAsJsonAsync(
                    "/api/v1/auth/admin/login",
                    credentials with
                    {
                        Code = "",
                    }
                )
            ).StatusCode
        );
        (
            await client.PostAsJsonAsync("/api/v1/auth/admin/login", credentials)
        ).EnsureSuccessStatusCode();
        (await client.GetAsync("/api/v1/admin/me")).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await another.PostAsJsonAsync("/api/v1/auth/admin/login", credentials)).StatusCode
        );
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("role")]
    [InlineData("version")]
    public async Task ExistingAdminCookieIsRevokedWhenAccountChanges(string change)
    {
        using var client = await f.LoginAdmin();
        var email = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
        try
        {
            await f.Db(async db =>
            {
                var account = await db.Accounts.SingleAsync(a => a.Email == email);
                if (change == "disabled")
                    account.Enabled = false;
                if (change == "role")
                    account.Role = "Customer";
                if (change == "version")
                    account.SessionVersion++;
                return await db.SaveChangesAsync();
            });
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.GetAsync("/api/v1/admin/me")).StatusCode
            );
        }
        finally
        {
            await f.Db(db =>
                db.Accounts.Where(a => a.Email == email)
                    .ExecuteUpdateAsync(set =>
                        set.SetProperty(a => a.Enabled, true).SetProperty(a => a.Role, "Admin")
                    )
            );
        }
    }

    [Fact]
    public async Task CustomerCannotReadAdminData()
    {
        var email = Guid.NewGuid() + "@example.test";
        var password = Guid.NewGuid().ToString();
        await f.Db(async db =>
        {
            var a = new Account { Email = email };
            a.Hash = new PasswordHasher<Account>().HashPassword(a, password);
            db.Add(a);
            return await db.SaveChangesAsync();
        });
        using var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Shop-Request", "1");
        (
            await c.PostAsJsonAsync("/api/v1/auth/login", new { email, password })
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await c.GetAsync("/api/v1/admin/orders")).StatusCode
        );
    }

    [Fact]
    public async Task OpeningStockIsRecordedOnceWithTheNewListing()
    {
        using var client = await f.LoginAdmin();
        const string path = "/api/v1/admin/perfumes";
        var input = new ProductInput(
            "Stock test",
            $"stock-{Guid.NewGuid():N}",
            "Test brand",
            "Niche",
            "Stock test listing",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            true,
            false,
            10,
            Seed.Sizes.Select(size => new SizeInput(size, 100, true)).ToArray()
        );
        foreach (var invalid in new[] { -1, 1_000_001 })
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (
                    await client.PostAsJsonAsync(path, input with { OpeningStockMl = invalid })
                ).StatusCode
            );

        var stocked = input with { OpeningStockMl = 75 };
        var created = await client.PostAsJsonAsync(path, stocked);
        created.EnsureSuccessStatusCode();
        int id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetInt32();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(path, stocked)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PatchAsJsonAsync($"{path}/{id}", stocked)).StatusCode
        );
        (
            await client.PatchAsJsonAsync($"{path}/{id}", input with { Threshold = 20 })
        ).EnsureSuccessStatusCode();

        var empty = await client.PostAsJsonAsync(path, input with { Slug = input.Slug + "-empty" });
        empty.EnsureSuccessStatusCode();
        int emptyId = (await empty.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetInt32();
        await f.Db(async db =>
        {
            var product = await db.Perfumes.SingleAsync(p => p.Id == id);
            Assert.Equal(75, product.OnHandMl);
            Assert.Equal(0, product.ReservedMl);
            var movement = await db.Movements.SingleAsync(m => m.PerfumeId == id);
            Assert.Equal(75, movement.Hand);
            Assert.Equal("Restock", movement.Type);
            Assert.Equal("Opening stock", movement.Reason);
            Assert.Equal(Environment.GetEnvironmentVariable("ADMIN_EMAIL"), movement.Actor);
            Assert.Equal(0, (await db.Perfumes.SingleAsync(p => p.Id == emptyId)).OnHandMl);
            Assert.False(await db.Movements.AnyAsync(m => m.PerfumeId == emptyId));
            return 0;
        });
    }

    [Fact]
    public async Task RejectsCrossOriginAdminWrite()
    {
        using var c = await f.LoginAdmin();
        c.DefaultRequestHeaders.Add("Origin", "https://unrelated.invalid");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await c.PostAsJsonAsync("/api/v1/admin/perfumes", new { })).StatusCode
        );
    }

    [Theory]
    [InlineData(true, true, 20)]
    [InlineData(true, false, 10)]
    [InlineData(false, false, 20)]
    public async Task CancellationHonorsPhysicalRecovery(bool confirm, bool restore, int remaining)
    {
        using var c = await f.LoginAdmin();
        int id = await OrderTests.Product(f, 20);
        var s = await OrderTests.Prepare(c, id);
        var response = await c.PostAsJsonAsync("/api/v1/orders", s);
        response.EnsureSuccessStatusCode();
        var reference = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("reference")
            .GetString();
        var path = "/api/v1/admin/orders/" + reference + "/status";
        if (confirm)
            (
                await c.PatchAsJsonAsync(path, new Change("Confirmed", null, null))
            ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await c.PatchAsJsonAsync(path, new Change("Cancelled", "", null, restore))).StatusCode
        );
        (
            await c.PatchAsJsonAsync(
                path,
                new Change("Cancelled", "Customer requested cancellation", null, restore)
            )
        ).EnsureSuccessStatusCode();
        await f.Db(async db =>
        {
            var p = await db.Perfumes.FindAsync(id);
            Assert.Equal(remaining, p!.OnHandMl);
            Assert.Equal(0, p.ReservedMl);
            return 0;
        });
    }

    [Fact]
    public async Task AnalyticsUsesPaymentAndRefundDatesInDhaka()
    {
        using var c = await f.LoginAdmin();
        int id = await OrderTests.Product(f, 50);
        var orders = new List<Order>();
        // Isolate this run's dates from earlier test executions.
        var day = new DateOnly(2000, 1, 1).AddDays(Random.Shared.Next(7000));
        var start = Metrics.Start(day);
        await f.Db(async db =>
        {
            for (int i = 0; i < 3; i++)
            {
                var guid = Guid.NewGuid().ToString("N");
                var o = new Order
                {
                    Reference = guid,
                    RequestKey = guid,
                    Total = 300,
                    Subtotal = 230,
                    Shipping = 70,
                    Method = i == 0 ? "Online" : "COD",
                    Payment =
                        i == 0 ? "Refunded"
                        : i == 1 ? "Unpaid"
                        : "Paid",
                    PaidAt = i == 1 ? null : start.AddMinutes(2),
                    CreatedAt = start.AddMinutes(1),
                    Fulfillment = "Delivered",
                    Lines =
                    [
                        new OrderLine
                        {
                            PerfumeId = id,
                            Name = "Metrics sample",
                            Category = "Niche",
                            SizeMl = 5,
                            UnitPrice = 115,
                            Quantity = 2,
                        },
                    ],
                };
                if (i == 0)
                    o.History.Add(new History { At = start.AddDays(1), Event = "Refund" });
                db.Add(o);
                orders.Add(o);
            }
            return await db.SaveChangesAsync();
        });
        try
        {
            var r = await c.GetFromJsonAsync<JsonElement>(
                $"/api/v1/admin/analytics/overview?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}"
            );
            Assert.Equal(600, r.GetProperty("netSales").GetDecimal());
            Assert.Equal(300, r.GetProperty("awaitingCod").GetDecimal());
            Assert.Equal(4, r.GetProperty("units").GetInt32());
            Assert.Equal(460, r.GetProperty("categories")[0].GetProperty("sales").GetDecimal());
            Assert.Equal(4, r.GetProperty("sizes")[0].GetProperty("units").GetInt32());
            Assert.Equal(3, r.GetProperty("days")[0].GetProperty("orders").GetInt32());
            var next = day.AddDays(1);
            r = await c.GetFromJsonAsync<JsonElement>(
                $"/api/v1/admin/analytics/overview?from={next:yyyy-MM-dd}&to={next:yyyy-MM-dd}"
            );
            Assert.Equal(-300, r.GetProperty("netSales").GetDecimal());
            Assert.Equal(-2, r.GetProperty("units").GetInt32());
        }
        finally
        {
            var ids = orders.Select(o => o.Id).ToArray();
            await f.Db(db => db.Orders.Where(o => ids.Contains(o.Id)).ExecuteDeleteAsync());
        }
    }
}
