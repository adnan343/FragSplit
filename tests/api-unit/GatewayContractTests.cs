using System.Net;
using System.Net.Http.Json;
using FragSplit;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Xunit;

public class GatewayContractTests
{
    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        ) => send(request);
    }

    private static IConfiguration Configuration(string mode) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SSLCOMMERZ_MODE"] = mode,
                    ["CALLBACK_BASE_URL"] = "https://store.example.test",
                }
            )
            .Build();

    private static HttpResponseMessage Json(object data) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(
                data,
                options: new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = null }
            ),
        };

    [Theory]
    [InlineData("sandbox", "sandbox.sslcommerz.com")]
    [InlineData("production", "securepay.sslcommerz.com")]
    public async Task CreatesHostedSessionWithPurchaseSnapshot(string mode, string host)
    {
        using var http = new HttpClient(
            new Stub(async req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal(host, req.RequestUri!.Host);
                Assert.Equal("/gwprocess/v4/api.php", req.RequestUri.AbsolutePath);
                var f = QueryHelpers.ParseQuery(await req.Content!.ReadAsStringAsync());
                Assert.Equal("300.00", f["total_amount"].ToString());
                Assert.Equal("BDT", f["currency"].ToString());
                Assert.Equal("FS-example", f["tran_id"].ToString());
                Assert.Equal("Courier", f["shipping_method"].ToString());
                Assert.Equal("2", f["num_of_item"].ToString());
                Assert.False(f.ContainsKey("multi_card_name"));
                Assert.Equal(
                    "https://store.example.test/api/v1/payments/sslcommerz/ipn",
                    f["ipn_url"].ToString()
                );
                return Json(
                    new
                    {
                        status = "SUCCESS",
                        sessionkey = "session",
                        GatewayPageURL = "https://" + host + "/checkout",
                    }
                );
            })
        );
        var adapter = new SslCommerz(http, Configuration(mode));
        var session = await adapter.Create(
            new Order
            {
                Reference = "FS-example",
                Total = 300,
                Lines = [new OrderLine { Quantity = 2 }],
            }
        );
        Assert.Equal("session", session.Key);
    }

    [Fact]
    public async Task ValidatesGrossAmountInsteadOfMerchantNetSettlement()
    {
        using var http = new HttpClient(
            new Stub(req =>
            {
                Assert.Equal(
                    "/validator/api/validationserverAPI.php",
                    req.RequestUri!.AbsolutePath
                );
                Assert.Equal(
                    "a+b",
                    QueryHelpers.ParseQuery(req.RequestUri.Query)["val_id"].ToString()
                );
                return Task.FromResult(
                    Json(
                        new
                        {
                            status = "VALIDATED",
                            tran_id = "FS-example",
                            val_id = "a+b",
                            amount = "300.00",
                            store_amount = "290.00",
                            currency = "BDT",
                            currency_type = "BDT",
                            currency_amount = "300.00",
                            risk_level = 0,
                            APIConnect = "DONE",
                        }
                    )
                );
            })
        );
        var r = await new SslCommerz(http, Configuration("sandbox")).Validate("a+b");
        Assert.True(Settlement.Matches("FS-example", "a+b", 300, r));
        Assert.Equal("0", r.Risk);
    }

    [Fact]
    public async Task ReadsAllAttemptsWhenLookingUpAnAmbiguousTransaction()
    {
        using var http = new HttpClient(
            new Stub(req =>
            {
                Assert.Equal(
                    "/validator/api/merchantTransIDvalidationAPI.php",
                    req.RequestUri!.AbsolutePath
                );
                Assert.Equal(
                    "FS-example",
                    QueryHelpers.ParseQuery(req.RequestUri.Query)["tran_id"].ToString()
                );
                return Task.FromResult(
                    Json(
                        new
                        {
                            APIConnect = "DONE",
                            element = new[]
                            {
                                new
                                {
                                    status = "FAILED",
                                    tran_id = "FS-example",
                                    val_id = "",
                                },
                                new
                                {
                                    status = "VALID",
                                    tran_id = "FS-example",
                                    val_id = "verified",
                                },
                            },
                        }
                    )
                );
            })
        );
        var rows = await new SslCommerz(http, Configuration("sandbox")).Query(
            new Order { Reference = "FS-example" }
        );
        Assert.Equal(new[] { "FAILED", "VALID" }, rows.Select(r => r.Status));
    }
}
