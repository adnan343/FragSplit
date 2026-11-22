using System.Net;
using System.Net.Mail;

namespace FragSplit;

public record MailFile(string Name, string MediaType, byte[] Bytes);

public interface IAccountEmail
{
    Task Send(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellation,
        MailFile? attachment = null
    );
}

public sealed class SmtpAccountEmail(IConfiguration config) : IAccountEmail
{
    public async Task Send(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellation,
        MailFile? attachment = null
    )
    {
        var host = config["SMTP_HOST"];
        var from = config["SMTP_FROM"];
        if (string.IsNullOrWhiteSpace(host) || !MailAddress.TryCreate(from, out var sender))
            throw new ShopError("Account email is unavailable. Please try again later.", 503);
        using var mail = new MailMessage(sender, new MailAddress(recipient))
        {
            Subject = subject,
            Body = body,
        };
        if (attachment != null)
            mail.Attachments.Add(
                new Attachment(
                    new MemoryStream(attachment.Bytes),
                    attachment.Name,
                    attachment.MediaType
                )
            );
        using var smtp = new SmtpClient(host, config.GetValue("SMTP_PORT", 587))
        {
            EnableSsl = config.GetValue("SMTP_STARTTLS", true),
        };
        if (!string.IsNullOrWhiteSpace(config["SMTP_USERNAME"]))
            smtp.Credentials = new NetworkCredential(
                config["SMTP_USERNAME"],
                config["SMTP_PASSWORD"]
            );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await smtp.SendMailAsync(mail, timeout.Token);
        }
        catch (Exception e)
            when (e is SmtpException
                || (e is OperationCanceledException && !cancellation.IsCancellationRequested)
            )
        {
            throw new ShopError("Email delivery failed. Request another email in a moment.", 503);
        }
    }
}
