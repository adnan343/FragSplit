using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FragSplit;
using Xunit;

[Collection("shop")]
public class CustomerTests(ShopFixture fixture)
{
    const string Root = "/api/v1";

    static Login Credentials() =>
        new($"{Guid.NewGuid():N}@example.test", "Aa1!" + Guid.NewGuid().ToString("N"));

    HttpClient Device()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Shop-Request", "1");
        return client;
    }

    static async Task<JsonElement> Success(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    async Task Register(HttpClient client, Login login)
    {
        var r = await client.PostAsJsonAsync(
            Root + "/auth/register",
            new AccountEmailRequest(login.Email)
        );
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var finish = new FinishAccountAccess(fixture.Mail.Token(login.Email), login.Password);
        (
            await client.PostAsJsonAsync(Root + "/auth/verify-email", finish)
        ).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DevicesShareBagAndPrivateOrders()
    {
        using var first = Device();
        using var second = Device();
        using var other = Device();
        var login = Credentials();
        await Register(first, login);
        await Success(await second.PostAsJsonAsync(Root + "/auth/login", login));
        await Register(other, Credentials());
        var session = await first.GetFromJsonAsync<JsonElement>(Root + "/auth/session");
        int owner = session.GetProperty("id").GetInt32();
        var product = await OrderTests.Product(fixture, 100);
        var submission = await OrderTests.Prepare(first, product);
        var entry = submission.Basket.Items;
        var update = new CartUpdate(owner, Guid.Empty, entry);
        var saved = await Success(await first.PutAsJsonAsync(Root + "/account/cart", update));
        var version = saved.GetProperty("cartVersion").GetGuid();
        Assert.Equal(
            version,
            (await second.GetFromJsonAsync<JsonElement>(Root + "/account/cart"))
                .GetProperty("cartVersion")
                .GetGuid()
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await second.PutAsJsonAsync(Root + "/account/cart", update)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await other.PutAsJsonAsync(Root + "/account/cart", update)).StatusCode
        );
        var receipt = await Success(
            await second.PostAsJsonAsync(
                Root + "/orders",
                submission with
                {
                    AccountId = owner,
                    CartVersion = version,
                }
            )
        );
        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("token").ValueKind);
        var reference = receipt.GetProperty("reference").GetString();
        var history = await first.GetFromJsonAsync<JsonElement>(Root + "/account/orders");
        Assert.Equal(reference, history[0].GetProperty("reference").GetString());
        var path = Root + "/orders/" + reference + "/status";
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);
        Assert.Equal(
            0,
            (await other.GetFromJsonAsync<JsonElement>(Root + "/account/orders")).GetArrayLength()
        );
        Assert.Equal(
            0,
            (await first.GetFromJsonAsync<JsonElement>(Root + "/account/cart"))
                .GetProperty("lines")
                .GetArrayLength()
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await first.GetAsync(Root + "/admin/orders")).StatusCode
        );
    }

    [Fact]
    public async Task EmailVerificationAndResetAreSingleUseAndRevokeSessions()
    {
        using var first = Device();
        using var second = Device();
        var login = Credentials();
        await first.PostAsJsonAsync(Root + "/auth/register", new AccountEmailRequest(login.Email));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await first.GetAsync(Root + "/account/orders")).StatusCode
        );
        var registration = new FinishAccountAccess(fixture.Mail.Token(login.Email), login.Password);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await first.PostAsJsonAsync(Root + "/auth/reset-password", registration)).StatusCode
        );
        foreach (
            var weak in new[]
            {
                "Aa1!abc",
                "abcdef1!",
                "ABCDEF1!",
                "Abcdefg!",
                "Abcdefg1",
                "Abcdef1 ",
            }
        )
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (
                    await first.PostAsJsonAsync(
                        Root + "/auth/verify-email",
                        registration with
                        {
                            Password = weak,
                        }
                    )
                ).StatusCode
            );
        registration = registration with { Password = "Abcdef1!" };
        login = login with { Password = registration.Password };
        (
            await first.PostAsJsonAsync(Root + "/auth/verify-email", registration)
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await first.PostAsJsonAsync(Root + "/auth/verify-email", registration)).StatusCode
        );
        var newLogin = login with { Password = "Newpass1!" };
        (
            await second.PostAsJsonAsync(
                Root + "/auth/forgot-password",
                new AccountEmailRequest(login.Email)
            )
        ).EnsureSuccessStatusCode();
        var reset = new FinishAccountAccess(fixture.Mail.Token(login.Email), newLogin.Password);
        (
            await second.PostAsJsonAsync(Root + "/auth/reset-password", reset)
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await first.GetAsync(Root + "/account/orders")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await second.PostAsJsonAsync(Root + "/auth/reset-password", reset)).StatusCode
        );
        // Use a separate host rate-limiter budget for credential assertions, preserving the shared database.
        using var host = fixture.WithWebHostBuilder(_ => { });
        using var fresh = host.CreateClient();
        fresh.DefaultRequestHeaders.Add("X-Shop-Request", "1");
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fresh.PostAsJsonAsync(Root + "/auth/login", login)).StatusCode
        );
        await Success(await fresh.PostAsJsonAsync(Root + "/auth/login", newLogin));
        (await fresh.PostAsync(Root + "/account/sign-out-all", null)).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fresh.GetAsync(Root + "/account/profile")).StatusCode
        );
    }

    [Fact]
    public async Task LegacyRecoveryRequiresProofOnce()
    {
        using var client = Device();
        var login = Credentials();
        const string savedCode = "0123456789ABCDEF0123456789ABCDEF01234567";
        await fixture.Db(async db =>
        {
            var account = new Account
            {
                Email = login.Email,
                RecoveryHash = Checkout.Hash(savedCode),
            };
            account.Hash = new Microsoft.AspNetCore.Identity.PasswordHasher<Account>().HashPassword(
                account,
                login.Password
            );
            db.Accounts.Add(account);
            return await db.SaveChangesAsync();
        });
        var request = new AccountEmailRequest(login.Email);
        (
            await client.PostAsJsonAsync(Root + "/auth/forgot-password", request)
        ).EnsureSuccessStatusCode();
        var reset = new FinishAccountAccess(
            fixture.Mail.Token(login.Email),
            "New password for legacy1!"
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(Root + "/auth/reset-password", reset)).StatusCode
        );
        (
            await client.PostAsJsonAsync(
                Root + "/auth/reset-password",
                reset with
                {
                    PreviousCredential = savedCode,
                }
            )
        ).EnsureSuccessStatusCode();
        (
            await client.PostAsJsonAsync(Root + "/auth/forgot-password", request)
        ).EnsureSuccessStatusCode();
        (
            await client.PostAsJsonAsync(
                Root + "/auth/reset-password",
                reset with
                {
                    Token = fixture.Mail.Token(login.Email),
                }
            )
        ).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GuestOrderRequiresReceiptToClaim()
    {
        using var guest = Device();
        using var owner = Device();
        using var other = Device();
        var product = await OrderTests.Product(fixture, 100);
        var s = await OrderTests.Prepare(guest, product);
        var login = Credentials();
        var receipt = await Success(
            await guest.PostAsJsonAsync(
                Root + "/orders",
                s with
                {
                    Customer = s.Customer with { Email = login.Email },
                }
            )
        );
        var reference = receipt.GetProperty("reference").GetString();
        var token = receipt.GetProperty("token").GetString();
        await Register(owner, login);
        await Register(other, Credentials());
        var path = Root + "/account/orders/" + reference + "/claim";
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync(path, null)).StatusCode);
        owner.DefaultRequestHeaders.Add("X-Order-Token", token);
        (await owner.PostAsync(path, null)).EnsureSuccessStatusCode();
        other.DefaultRequestHeaders.Add("X-Order-Token", token);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync(path, null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.GetAsync(Root + "/orders/" + reference + "/status")).StatusCode
        );
    }
}
