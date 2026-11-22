using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public record DeliveryProfile(
    string Name,
    string Phone,
    string Address,
    string City,
    string Area,
    string Postcode
);

public static class CustomerAccounts
{
    public static int? CustomerId(ClaimsPrincipal user) =>
        user.IsInRole("Customer")
        && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out int id)
            ? id
            : null;

    public static Task<Account> Lock(ShopDb db, int id) =>
        db
            .Accounts.FromSqlInterpolated(
                $"SELECT * FROM \"Accounts\" WHERE \"Id\"={id} FOR UPDATE"
            )
            .SingleAsync();

    public static Task SignIn(HttpContext ctx, Account user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("sessionVersion", user.SessionVersion.ToString()),
        };
        return ctx.SignInAsync(
            "session",
            new ClaimsPrincipal(new ClaimsIdentity(claims, "session"))
        );
    }

    public static async Task ValidateSession(CookieValidatePrincipalContext context)
    {
        var user = context.Principal!;
        if (!user.IsInRole("Customer"))
            return;
        var id = CustomerId(user);
        var version = await context
            .HttpContext.RequestServices.GetRequiredService<ShopDb>()
            .Accounts.Where(a => a.Id == id && a.Role == "Customer")
            .Select(a => (int?)a.SessionVersion)
            .SingleOrDefaultAsync();
        if (version == null || version.ToString() != user.FindFirstValue("sessionVersion"))
            context.RejectPrincipal();
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/auth/session", (HttpContext ctx) =>
            new
            {
                id = CustomerId(ctx.User),
                email = ctx.User.Identity?.Name,
                role = ctx.User.FindFirstValue(ClaimTypes.Role),
            }
        );
        EmailAccountAccess.Map(api);
        var account = api.MapGroup("/account").RequireAuthorization("customer");
        account.MapGet("/profile", (HttpContext ctx, ShopDb db) =>
            db
                .Accounts.Where(a => a.Id == CustomerId(ctx.User))
                .Select(a => new DeliveryProfile(
                    a.DisplayName,
                    a.Phone,
                    a.Street,
                    a.City,
                    a.Area,
                    a.Postcode
                ))
                .SingleAsync()
        );
        account.MapPut("/profile", async (DeliveryProfile input, HttpContext ctx, ShopDb db) =>
        {
            var a = await db.Accounts.SingleAsync(a => a.Id == CustomerId(ctx.User));
            a.DisplayName = Catalog.Field(input.Name, 50);
            a.Phone = Catalog.Field(input.Phone, 30);
            a.Street = Catalog.Field(input.Address, 50);
            a.City = Catalog.Field(input.City, 50);
            a.Area = Catalog.Field(input.Area, 50);
            a.Postcode = Catalog.Field(input.Postcode, 12);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        account.MapPost("/sign-out-all", async (HttpContext ctx, ShopDb db) =>
        {
            var id = CustomerId(ctx.User);
            await db
                .Accounts.Where(a => a.Id == id)
                .ExecuteUpdateAsync(set =>
                    set.SetProperty(a => a.SessionVersion, a => a.SessionVersion + 1)
                );
            await ctx.SignOutAsync("session");
            return Results.NoContent();
        });
        account.MapGet("/orders", async (HttpContext ctx, ShopDb db, int? before) =>
        {
            var id = CustomerId(ctx.User);
            return await db
                .Orders.Where(o => o.AccountId == id && (before == null || o.Id < before))
                .OrderByDescending(o => o.Id)
                .Take(25)
                .Select(o => new
                {
                    o.Id,
                    o.Reference,
                    o.CreatedAt,
                    o.Total,
                    o.Payment,
                    o.Fulfillment,
                    o.Tracking,
                })
                .ToListAsync();
        });
        account.MapPost(
            "/orders/{reference}/claim",
            async (string reference, HttpContext ctx, Checkout service, ShopDb db) =>
            {
                var id = CustomerId(ctx.User);
                service.Authorize(reference, ctx.Request.Headers["X-Order-Token"]);
                var count = await db
                    .Orders.Where(o =>
                        o.Reference == reference && (o.AccountId == null || o.AccountId == id)
                    )
                    .ExecuteUpdateAsync(set => set.SetProperty(o => o.AccountId, id));
                if (count != 1)
                    throw new ShopError("Order is unavailable.", 404);
                return Results.NoContent();
            }
        );
        AccountCart.Map(account);
    }
}
