using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FragSplit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Collection("shop")]
public class PaymentTests(ShopFixture f)
{
    sealed class TestGateway : IPaymentGateway, IPaymentAvailability
    {
        public bool Enabled => true;
        public GatewayResult Result = new("INVALID", "", "", null, "", "", null, "", "", "");

        public Task<HostedSession> Create(Order o) =>
            Task.FromResult(
                new HostedSession("session-" + o.Reference, "https://gateway.invalid/test")
            );

        public Task<GatewayResult> Validate(string id) => Task.FromResult(Result);

        public Task<GatewayResult[]> Query(Order order) => Task.FromResult(new[] { Result });
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("declined")]
    [InlineData("late")]
    [InlineData("risk")]
    [InlineData("unverified")]
    public async Task SettlementChangesStockOnce(string scenario)
    {
        var gateway = new TestGateway();
        using var host = f.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IPaymentGateway>(gateway);
                services.AddSingleton<IPaymentAvailability>(gateway);
            })
        );
        using var client = host.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        int id = await OrderTests.Product(f, 20);
        var submission = await OrderTests.Prepare(client, id, 5, 2, "Online");
        var made = await client.PostAsJsonAsync("/api/v1/orders", submission);
        made.EnsureSuccessStatusCode();
        var receipt = await made.Content.ReadFromJsonAsync<JsonElement>();
        var reference = receipt.GetProperty("reference").GetString()!;
        client.DefaultRequestHeaders.Add("X-Order-Token", receipt.GetProperty("token").GetString());
        var start = await client.PostAsJsonAsync(
            "/api/v1/payments/sslcommerz/initiate",
            new { reference }
        );
        start.EnsureSuccessStatusCode();
        var val = Guid.NewGuid().ToString("N");
        Task<HttpResponseMessage> Post(string endpoint, string validation) =>
            client.PostAsync(
                "/api/v1/payments/sslcommerz/" + endpoint,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        { "tran_id", reference },
                        { "val_id", validation },
                        { "status", "VALID" },
                    }
                )
            );
        if (scenario == "unverified")
        {
            Assert.Equal(HttpStatusCode.Redirect, (await Post("success", val)).StatusCode);
            Assert.Equal(
                "Pending",
                await f.Db(db =>
                    db.Orders.Where(o => o.Reference == reference)
                        .Select(o => o.Payment)
                        .SingleAsync()
                )
            );
            return;
        }
        if (scenario == "late")
        {
            await f.Db(async db =>
            {
                var o = await db.Orders.SingleAsync(o => o.Reference == reference);
                o.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
                return await db.SaveChangesAsync();
            });
            using var scope = host.Services.CreateScope();
            var settlement = scope.ServiceProvider.GetRequiredService<Settlement>();
            await settlement.ExpireDue();
            await settlement.ExpireDue();
        }
        gateway.Result = new(
            scenario == "declined" ? "FAILED" : "VALID",
            reference,
            val,
            300,
            "BDT",
            "BDT",
            300,
            scenario == "risk" ? "1" : "0",
            "session-" + reference,
            "DONE"
        );
        var calls = await Task.WhenAll(
            Post("ipn", scenario == "declined" ? "" : val),
            Post("ipn", scenario == "declined" ? "" : val)
        );
        foreach (var call in calls)
            call.EnsureSuccessStatusCode();
        await f.Db(async db =>
        {
            var o = await db.Orders.SingleAsync(o => o.Reference == reference);
            var p = await db.Perfumes.FindAsync(id);
            Assert.Equal(scenario == "declined" ? "Failed" : "Paid", o.Payment);
            var mails = await db.ConfirmationMails.Where(m => m.OrderId == o.Id).ToListAsync();
            if (scenario == "duplicate")
            {
                var mail = Assert.Single(mails);
                Assert.Contains("Paid: BDT 300.00", mail.Slip);
                Assert.Contains("Balance due: BDT 0.00", mail.Slip);
            }
            else
                Assert.Empty(mails);
            Assert.Equal(0, p!.ReservedMl);
            Assert.Equal(scenario == "duplicate" ? 10 : 20, p.OnHandMl);
            Assert.Equal(scenario is "late" or "risk", o.NeedsReview);
            Assert.Equal(2, await db.Movements.CountAsync(m => m.OrderId == o.Id));
            return 0;
        });
    }
}
