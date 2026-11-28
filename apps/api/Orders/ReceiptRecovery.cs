using System.Net.Mail;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public record ReceiptLink(string Reference, string Email);

public sealed class ReceiptRecovery(
    ShopDb db,
    IDataProtectionProvider protection,
    IConfiguration config,
    TimeProvider clock,
    IAccountEmail mail,
    ILogger<ReceiptRecovery> log
)
{
    public const string Prefix = "r1.";
    public const int ValidMinutes = 30;

    static ITimeLimitedDataProtector Protector(IDataProtectionProvider provider) =>
        provider.CreateProtector("vialmuse.guest-receipt.v1").ToTimeLimitedDataProtector();

    public static string Unprotect(IDataProtectionProvider provider, string token) =>
        Protector(provider).Unprotect(token[Prefix.Length..]);

    public static string Protect(
        IDataProtectionProvider provider,
        string reference,
        DateTimeOffset expires
    ) => Prefix + Protector(provider).Protect(reference, expires);

    public async Task<IResult> Request(ReceiptLink input, CancellationToken ct)
    {
        var reference = input.Reference?.Trim().ToUpperInvariant();
        var email = Catalog.Field(input.Email, 254, true);
        if (!Checkout.IsReference(reference))
            throw new ShopError("Enter the order reference from your confirmation.");
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new ShopError("Enter a valid email address.");
        var origin = config["PUBLIC_ORIGIN"]?.TrimEnd('/');
        if (
            !Uri.TryCreate(origin, UriKind.Absolute, out var url)
            || !(url.Scheme == "https" || (url.Scheme == "http" && url.IsLoopback))
        )
            throw new ShopError("Receipt recovery is unavailable. Please contact the store.", 503);

        var lower = email.ToLowerInvariant();
        var order = await db
            .Orders.AsNoTracking()
            .Where(o =>
                o.Reference == reference && o.AccountId == null && o.Email.ToLower() == lower
            )
            .Select(o => new { o.Id, o.Email })
            .SingleOrDefaultAsync(ct);
        if (order == null)
            return Results.NoContent();

        var now = clock.GetUtcNow().UtcDateTime;
        var next = await db
            .Orders.Where(o =>
                o.Id == order.Id
                && o.AccountId == null
                && (
                    o.ReceiptLinkRequestedAt == null
                    || o.ReceiptLinkRequestedAt <= now.AddMinutes(-2)
                )
            )
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReceiptLinkRequestedAt, now), ct);
        if (next == 1)
        {
            var token = Protect(
                protection,
                reference!,
                DateTimeOffset.UtcNow.AddMinutes(ValidMinutes)
            );
            try
            {
                await mail.Send(
                    order.Email,
                    "Your VialMuse receipt link",
                    $"Open your receipt for order {reference}. This link expires in {ValidMinutes} minutes.\n\n{origin}/orders/{reference}#token={token}\n\nIf you did not request this email, you can ignore it.",
                    ct
                );
            }
            catch (ShopError e) when (e.Status == 503)
            {
                log.LogWarning("Sending a guest receipt link failed.");
            }
        }
        return Results.NoContent();
    }
}
