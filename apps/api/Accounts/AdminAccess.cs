using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public record AdminLogin(string Email, string Password, string Code);

public static class AdminAccess
{
    static IDataProtector TotpProtector(IDataProtectionProvider protection) =>
        protection.CreateProtector("vialmuse.admin-totp.v1");

    public static void Enroll(Account account, string? secret, IDataProtectionProvider protection)
    {
        if (Decode(secret) is not byte[] key)
            throw new InvalidOperationException(
                "Supply ADMIN_TOTP_SECRET as 32 Base32 characters."
            );
        account.AdminTotpKey = TotpProtector(protection).Protect(Convert.ToHexString(key));
        account.AdminTotpStep = null;
        account.SessionVersion++;
    }

    static byte[]? Decode(string? secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        if (secret?.Length != 32)
            return null;
        var key = new byte[20];
        var buffer = 0;
        var bits = 0;
        var offset = 0;
        foreach (var c in secret.ToUpperInvariant())
        {
            var value = alphabet.IndexOf(c);
            if (value < 0)
                return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits < 8)
                continue;
            bits -= 8;
            key[offset++] = (byte)(buffer >> bits);
        }
        return key;
    }

    static bool Matches(byte[] key, long step, string code)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0f;
        var number =
            (BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset)) & 0x7fffffff) % 1_000_000;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(number.ToString("D6", CultureInfo.InvariantCulture)),
            Encoding.ASCII.GetBytes(code)
        );
    }

    public static async Task<IResult> Login(
        AdminLogin input,
        HttpContext context,
        ShopDb db,
        IDataProtectionProvider protection,
        TimeProvider clock
    )
    {
        static IResult Invalid() =>
            Results.Problem(
                statusCode: 401,
                detail: "Check your email, password and current authenticator code."
            );
        if (
            input.Email is not { Length: > 0 and <= 254 }
            || input.Password is not { Length: > 0 and <= 128 }
            || input.Code is not { Length: 6 }
            || input.Code.Any(c => !char.IsAsciiDigit(c))
        )
            return Invalid();
        var email = input.Email.Trim().ToLowerInvariant();
        var account = await db
            .Accounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Email == email && a.Role == "Admin" && a.Enabled);
        if (
            account?.AdminTotpKey == null
            || new PasswordHasher<Account>().VerifyHashedPassword(
                account,
                account.Hash,
                input.Password
            ) == PasswordVerificationResult.Failed
        )
            return Invalid();
        byte[] key;
        try
        {
            key = Convert.FromHexString(TotpProtector(protection).Unprotect(account.AdminTotpKey));
        }
        catch (CryptographicException)
        {
            return Invalid();
        }
        var current = clock.GetUtcNow().ToUnixTimeSeconds() / 30;
        var step = Matches(key, current, input.Code) ? current : current - 1;
        if (step != current && !Matches(key, step, input.Code))
            return Invalid();
        var used = await db
            .Accounts.Where(a =>
                a.Id == account.Id
                && a.Enabled
                && a.Role == "Admin"
                && a.SessionVersion == account.SessionVersion
                && (a.AdminTotpStep == null || a.AdminTotpStep < step)
            )
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.AdminTotpStep, step));
        if (used != 1)
            return Invalid();
        await CustomerAccounts.SignIn(context, account, mfa: true);
        return Results.Ok(new { account.Role });
    }
}
