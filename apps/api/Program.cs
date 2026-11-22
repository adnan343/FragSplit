using System.Security.Claims;
using System.Threading.RateLimiting;
using FragSplit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Services.AddDbContext<ShopDb>(o =>
    o.UseNpgsql(config.GetConnectionString("Shop") ?? "Host=localhost;Database=fragsplit")
);
var protection = builder.Services.AddDataProtection().SetApplicationName("FragSplit");
if (config["KEY_DIRECTORY"] is string keyDir)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyDir));
builder
    .Services.AddAuthentication("session")
    .AddCookie("session", o =>
    {
        o.Cookie.Name = "fs.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy =
            config["PUBLIC_ORIGIN"]?.StartsWith("https://") == true
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(4);
        o.Events.OnValidatePrincipal = CustomerAccounts.ValidateSession;
        o.Events.OnRedirectToLogin = c =>
        {
            c.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = c =>
        {
            c.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("admin", p => p.RequireRole("Admin"));
    o.AddPolicy("customer", p => p.RequireRole("Customer"));
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    foreach (
        var (name, max) in new[]
        {
            ("login", 10),
            ("registration", 10),
            ("recovery", 8),
            ("email-confirm", 30),
            ("purchase", 45),
            ("browse", 240),
        }
    )
        o.AddPolicy(name, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = max,
                    Window = TimeSpan.FromMinutes(1),
                }
            )
        );
});
builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 6 * 1024 * 1024;
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 6 * 1024 * 1024;
    o.ValueLengthLimit = 8192;
});
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IImageStore>(s =>
    string.IsNullOrWhiteSpace(config["S3_BUCKET"]) ? new LocalImages() : new BucketImages(config)
);
builder.Services.AddScoped<Checkout>();
builder.Services.AddScoped<EmailAccountAccess>();
builder.Services.AddSingleton<IAccountEmail, SmtpAccountEmail>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPaymentAvailability, PaymentAvailability>();
builder.Services.AddScoped<Settlement>();
builder.Services.AddHostedService<ExpiryWorker>();
builder.Services.AddHostedService<ConfirmationMailer>();
builder
    .Services.AddHttpClient<IPaymentGateway, SslCommerz>(http =>
    {
        http.Timeout = TimeSpan.FromSeconds(12);
        http.MaxResponseContentBufferSize = 1024 * 1024;
    })
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var app = builder.Build();
app.UseExceptionHandler();
app.Use(
    async (ctx, next) =>
    {
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["X-Request-Id"] = ctx.TraceIdentifier;
        ctx.Response.Headers.CacheControl = "no-store";
        var path = ctx.Request.Path;
        if (
            !HttpMethods.IsGet(ctx.Request.Method)
            && (
                path.StartsWithSegments("/api/v1/admin")
                || path.StartsWithSegments("/api/v1/auth")
                || path.StartsWithSegments("/api/v1/account")
                || (
                    ctx.Request.Cookies.ContainsKey("fs.session")
                    && (
                        path.StartsWithSegments("/api/v1/orders")
                        || path == "/api/v1/payments/sslcommerz/initiate"
                    )
                )
            )
            && (
                ctx.Request.Headers["X-Shop-Request"] != "1"
                || (
                    ctx.Request.Headers.Origin.Count > 0
                    && ctx.Request.Headers.Origin != config["PUBLIC_ORIGIN"]
                )
            )
        )
        {
            ctx.Response.StatusCode = 403;
            return;
        }
        try
        {
            await next();
        }
        catch (ShopError e) when (!ctx.Response.HasStarted)
        {
            await Results.Problem(statusCode: e.Status, detail: e.Message).ExecuteAsync(ctx);
        }
    }
);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapGet("/health", async (ShopDb db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "healthy" })
        : Results.StatusCode(503)
);
var api = app.MapGroup("/api/v1");
api.MapGet("/settings", async (ShopDb db, IPaymentAvailability payments) =>
    new
    {
        store = await db.Settings.SingleAsync(),
        zones = await db.Zones.Where(z => z.Active).OrderBy(z => z.Id).ToListAsync(),
        online = payments.Enabled,
    }
);
api.MapPost("/auth/login", async (Login input, ShopDb db, HttpContext ctx) =>
    {
        var user = await db.Accounts.SingleOrDefaultAsync(u =>
            u.Email == (input.Email ?? "").Trim().ToLower()
        );
        if (
            user == null
            || string.IsNullOrEmpty(input.Password)
            || new PasswordHasher<Account>().VerifyHashedPassword(user, user.Hash, input.Password)
                == PasswordVerificationResult.Failed
        )
            return Results.Unauthorized();
        await CustomerAccounts.SignIn(ctx, user);
        return Results.Ok(new { user.Role });
    })
    .RequireRateLimiting("login");
api.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync("session");
    return Results.NoContent();
});
var admin = api.MapGroup("/admin").RequireAuthorization("admin");
admin.MapGet("/me", (HttpContext ctx) => new { email = ctx.User.Identity!.Name });
CustomerAccounts.Map(api);
Catalog.Map(api, admin);
Media.Map(api, admin);
Checkout.Map(api, admin);
SettingsRoutes.Map(admin);
Settlement.Map(api, admin);
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDb>();
    await db.Database.MigrateAsync();
    await Seed.Run(db, config);
}
app.Run();

public record Login(string Email, string Password);

public sealed class ShopError(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

public interface IPaymentAvailability
{
    bool Enabled { get; }
}

public sealed class PaymentAvailability(IConfiguration c) : IPaymentAvailability
{
    public bool Enabled =>
        !string.IsNullOrWhiteSpace(c["SSLCOMMERZ_STORE_ID"])
        && !string.IsNullOrWhiteSpace(c["SSLCOMMERZ_STORE_PASSWORD"])
        && Uri.TryCreate(c["CALLBACK_BASE_URL"], UriKind.Absolute, out var u)
        && u.Scheme == "https";
}

public partial class Program { }
