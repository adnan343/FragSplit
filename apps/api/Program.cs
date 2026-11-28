using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
    o.AddPolicy("admin", p => p.RequireRole("Admin").RequireClaim("amr", "mfa"));
    o.AddPolicy("customer", p => p.RequireRole("Customer"));
});
var ingressSecret = Encoding.UTF8.GetBytes(config["INGRESS_SECRET"] ?? "");
var catalogSecret = Encoding.UTF8.GetBytes(config["CATALOG_ORIGIN_SECRET"] ?? "");
bool IsInternalCatalog(HttpContext context) =>
    catalogSecret.Length >= 32
    && context.Request.Headers.TryGetValue("X-VialMuse-Catalog-Origin", out var credential)
    && credential.Count == 1
    && CryptographicOperations.FixedTimeEquals(
        catalogSecret,
        Encoding.UTF8.GetBytes(credential.ToString())
    );
string ClientAddress(HttpContext context)
{
    var address = context.Connection.RemoteIpAddress;
    if (
        ingressSecret.Length >= 32
        && context.Request.Headers.TryGetValue("X-VialMuse-Ingress", out var credential)
        && credential.Count == 1
        && CryptographicOperations.FixedTimeEquals(
            ingressSecret,
            Encoding.UTF8.GetBytes(credential.ToString())
        )
        && context.Request.Headers.TryGetValue("X-VialMuse-Client-IP", out var client)
        && client.Count == 1
        && IPAddress.TryParse(client[0], out var forwarded)
    )
        address = forwarded;
    if (address?.IsIPv4MappedToIPv6 == true)
        address = address.MapToIPv4();
    return address?.ToString() ?? "local";
}
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    foreach (
        var (name, max) in new[]
        {
            ("login", 10),
            ("admin-login", 6),
            ("registration", 10),
            ("recovery", 8),
            ("email-confirm", 30),
            ("checkout", 45),
            ("quote", 90),
            ("order-status", 90),
            ("receipt-link", 8),
            ("browse", 240),
        }
    )
        o.AddPolicy(name, ctx =>
        {
            var internalCatalog = name == "browse" && IsInternalCatalog(ctx);
            return RateLimitPartition.GetFixedWindowLimiter(
                internalCatalog ? "internal-catalog" : ClientAddress(ctx),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = internalCatalog ? 1800 : max,
                    Window = TimeSpan.FromMinutes(1),
                }
            );
        });
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
builder.Services.AddScoped<Reservations>();
builder.Services.AddScoped<ReceiptRecovery>();
builder.Services.AddScoped<EmailAccountAccess>();
builder.Services.AddSingleton<IAccountEmail, SmtpAccountEmail>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<ExpiryWorker>();
builder.Services.AddHostedService<ConfirmationMailer>();
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
                    && path.StartsWithSegments("/api/v1/orders")
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
api.MapGet("/settings", async (ShopDb db) =>
    new
    {
        store = await db.Settings.SingleAsync(),
        zones = await db.Zones.Where(z => z.Active).OrderBy(z => z.Id).ToListAsync(),
        online = false,
    }
);
api.MapPost("/auth/login", async (Login input, ShopDb db, HttpContext ctx) =>
    {
        var user = await db.Accounts.SingleOrDefaultAsync(u =>
            u.Email == (input.Email ?? "").Trim().ToLowerInvariant()
            && u.Role == "Customer"
            && u.Enabled
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
api.MapPost("/auth/admin/login", AdminAccess.Login).RequireRateLimiting("admin-login");
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
var migrate = args.FirstOrDefault() is "migrate" or "reset-admin";
if (migrate || config.GetValue("MIGRATE_ON_STARTUP", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ShopDb>();
    await db.Database.MigrateAsync();
    await Seed.Run(
        db,
        config,
        scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
        args.FirstOrDefault() == "reset-admin"
    );
    if (config.GetValue<bool>("GRANT_RUNTIME_ACCESS"))
        await RuntimeAccess.Grant(db);
}
if (!migrate)
    app.Run();

public record Login(string Email, string Password);

public sealed class ShopError(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

public partial class Program { }
