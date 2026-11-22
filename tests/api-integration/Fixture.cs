using System.Collections.Concurrent;
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
    public CapturedMail Mail { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IAccountEmail>(Mail));

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
