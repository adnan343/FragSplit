using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FragSplit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[CollectionDefinition("shop", DisableParallelization = true)]
public sealed class ShopCollection : ICollectionFixture<ShopFixture> { }

public class ShopFixture : WebApplicationFactory<Program>
{
    sealed class TestClock : TimeProvider
    {
        readonly DateTimeOffset start = DateTimeOffset.UtcNow;
        long advance;

        public override DateTimeOffset GetUtcNow() =>
            start.AddSeconds(Interlocked.Read(ref advance));

        public void NextStep() => Interlocked.Add(ref advance, 30);
    }

    readonly TestClock clock = new();
    int adminClients;
    public CapturedMail Mail { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IAccountEmail>(Mail);
            services.AddSingleton<TimeProvider>(clock);
        });

    public ShopFixture()
    {
        if (
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__Shop"))
        )
            throw new InvalidOperationException(
                "Set ConnectionStrings__Shop to an isolated PostgreSQL test database."
            );
    }

    public async Task<T> Db<T>(Func<ShopDb, Task<T>> run)
    {
        using var scope = Services.CreateScope();
        return await run(scope.ServiceProvider.GetRequiredService<ShopDb>());
    }

    public HttpClient NewAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Shop-Request", "1");
        client.DefaultRequestHeaders.Add(
            "X-VialMuse-Ingress",
            Environment.GetEnvironmentVariable("INGRESS_SECRET")
        );
        client.DefaultRequestHeaders.Add(
            "X-VialMuse-Client-IP",
            $"198.18.0.{Interlocked.Increment(ref adminClients)}"
        );
        return client;
    }

    public AdminLogin AdminCredentials()
    {
        // Use the RFC 6238 test key seeded by scripts/test-api.sh.
        clock.NextStep();
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, clock.GetUtcNow().ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData("12345678901234567890"u8, counter);
        var number =
            BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(hash[^1] & 15)) & int.MaxValue;
        return new(
            Environment.GetEnvironmentVariable("ADMIN_EMAIL")!,
            Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!,
            (number % 1_000_000).ToString("D6", CultureInfo.InvariantCulture)
        );
    }

    public async Task<HttpClient> LoginAdmin()
    {
        var c = NewAdminClient();
        (
            await c.PostAsJsonAsync("/api/v1/auth/admin/login", AdminCredentials())
        ).EnsureSuccessStatusCode();
        return c;
    }
}

public class CapturedMail : IAccountEmail
{
    public ConcurrentDictionary<string, string> Messages { get; } = new();

    public Task Send(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellation,
        MailFile? attachment = null
    )
    {
        Messages[recipient] = body;
        return Task.CompletedTask;
    }

    public string Token(string email) => Messages[email].Split("#token=")[1].Split('\n')[0];
}
