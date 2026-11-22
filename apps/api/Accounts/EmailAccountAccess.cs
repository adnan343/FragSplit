using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public record AccountEmailRequest(string Email, string? Next = null);

public record FinishAccountAccess(string Token, string Password, string? PreviousCredential = null);

public sealed class EmailAccountAccess(
    ShopDb db,
    IDataProtectionProvider protection,
    IAccountEmail mail,
    IConfiguration config
)
{
    sealed record Ticket(string Email, int Id, int Version);

    const string Create = "register";
    const string Reset = "reset";

    static ShopError InvalidLink() =>
        new("This link is invalid, expired or already used. Request a new email.");

    ITimeLimitedDataProtector Protector(string purpose) =>
        protection
            .CreateProtector("FragSplit.CustomerEmail.v1", purpose)
            .ToTimeLimitedDataProtector();

    async Task<IResult> Request(AccountEmailRequest input, string purpose, CancellationToken ct)
    {
        var email = Catalog.Field(input.Email, 254, true).ToLowerInvariant();
        if (
            !MailAddress.TryCreate(email, out var address)
            || address.Address != email
            || !email.Contains('@')
        )
            throw new ShopError("Enter a valid email address.");
        var account = await db
            .Accounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Email == email, ct);
        var origin = config["PUBLIC_ORIGIN"]?.TrimEnd('/');
        if (
            !Uri.TryCreate(origin, UriKind.Absolute, out var url)
            || !(url.Scheme == "https" || (url.Scheme == "http" && url.IsLoopback))
        )
            throw new ShopError("Account email is unavailable. Please check the store URL.", 503);

        // Give the requester the same response regardless of account existence. The
        // mailbox owner receives either the requested action or the appropriate guidance.
        var registering = purpose == Create;
        string subject,
            body;
        if ((registering && account == null) || (!registering && account?.Role == "Customer"))
        {
            var minutes = registering ? 60 : 20;
            var token = Protector(purpose)
                .Protect(
                    JsonSerializer.Serialize(
                        new Ticket(email, account?.Id ?? 0, account?.SessionVersion ?? 0)
                    ),
                    TimeSpan.FromMinutes(minutes)
                );
            var mode = !registering && account!.RecoveryHash != null ? "legacy" : purpose;
            var next =
                input.Next == "/checkout"
                || System.Text.RegularExpressions.Regex.IsMatch(
                    input.Next ?? "",
                    "^/orders/FS-[A-F0-9]{20}$"
                )
                    ? "&next=" + Uri.EscapeDataString(input.Next!)
                    : "";
            var link = $"{origin}/account/confirm?mode={mode}{next}#token={token}";
            subject = registering
                ? "Verify your email to open your account"
                : "Reset your account password";
            body =
                $"{subject} using the link below. It expires in {minutes} minutes.\n\n{link}\n\nIf you did not request this email, you can ignore it.";
        }
        else
        {
            subject = "Your customer account request";
            body = registering
                ? $"This address is already registered. Sign in at {origin}/account or choose Forgot password."
                : $"This address has no customer account eligible for a password reset. Visit {origin}/account to sign in or create an account.";
        }
        await mail.Send(email, subject, body, ct);
        return Results.NoContent();
    }

    Ticket Read(string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
            throw InvalidLink();
        try
        {
            return JsonSerializer.Deserialize<Ticket>(Protector(purpose).Unprotect(token))
                ?? throw InvalidLink();
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            throw InvalidLink();
        }
    }

    const string PasswordHint =
        "Use 8 to 128 characters with an uppercase letter, a lowercase letter, a number and a special character.";

    static void SetPassword(Account account, string? password)
    {
        if (password == null || password.Length > 128)
            throw new ShopError(PasswordHint);
        var characters = password.EnumerateRunes().ToArray();
        if (
            characters.Length < 8
            || !characters.Any(Rune.IsUpper)
            || !characters.Any(Rune.IsLower)
            || !characters.Any(Rune.IsDigit)
            || !characters.Any(c => Rune.IsPunctuation(c) || Rune.IsSymbol(c))
        )
            throw new ShopError(PasswordHint);
        account.Hash = new PasswordHasher<Account>().HashPassword(account, password);
    }

    async Task<IResult> Verify(FinishAccountAccess input, HttpContext ctx)
    {
        var ticket = Read(input.Token, Create);
        var account = new Account { Email = ticket.Email, Role = "Customer" };
        SetPassword(account, input.Password);
        db.Accounts.Add(account);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e)
            when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw InvalidLink();
        }
        await CustomerAccounts.SignIn(ctx, account);
        return Results.NoContent();
    }

    async Task<IResult> Recover(FinishAccountAccess input)
    {
        var ticket = Read(input.Token, Reset);
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await db
            .Accounts.FromSqlInterpolated(
                $"SELECT * FROM \"Accounts\" WHERE \"Id\"={ticket.Id} FOR UPDATE"
            )
            .SingleOrDefaultAsync();
        if (a?.Role != "Customer" || a.Email != ticket.Email || a.SessionVersion != ticket.Version)
            throw InvalidLink();
        // Accounts created before email verification have a saved recovery hash.
        // Protect their private order history if an unverified address was mistyped.
        if (a.RecoveryHash != null)
        {
            var proof = input.PreviousCredential;
            if (
                string.IsNullOrEmpty(proof)
                || proof.Length > 128
                || (
                    !CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(a.RecoveryHash),
                        Convert.FromHexString(Checkout.Hash(proof.Trim().ToUpperInvariant()))
                    )
                    && new PasswordHasher<Account>().VerifyHashedPassword(a, a.Hash, proof)
                        == PasswordVerificationResult.Failed
                )
            )
                throw new ShopError(
                    "Enter your current password or your original saved recovery code."
                );
        }
        SetPassword(a, input.Password);
        a.RecoveryHash = null;
        a.SessionVersion++;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.NoContent();
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost(
                "/auth/register",
                (AccountEmailRequest input, EmailAccountAccess service, CancellationToken ct) =>
                    service.Request(input, Create, ct)
            )
            .RequireRateLimiting("registration");
        api.MapPost(
                "/auth/verify-email",
                (FinishAccountAccess input, EmailAccountAccess service, HttpContext ctx) =>
                    service.Verify(input, ctx)
            )
            .RequireRateLimiting("email-confirm");
        api.MapPost(
                "/auth/forgot-password",
                (AccountEmailRequest input, EmailAccountAccess service, CancellationToken ct) =>
                    service.Request(input, Reset, ct)
            )
            .RequireRateLimiting("recovery");
        api.MapPost(
                "/auth/reset-password",
                (FinishAccountAccess input, EmailAccountAccess service) => service.Recover(input)
            )
            .RequireRateLimiting("email-confirm");
    }
}
