using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace FragSplit;

public sealed class ConfirmationMail
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Recipient { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Body { get; set; } = "";
    public string Slip { get; set; } = "";
    public DateTime NextAttempt { get; set; }
    public int Attempts { get; set; }
    public DateTime? SentAt { get; set; }

    static string Money(decimal value) =>
        "BDT " + value.ToString("N2", CultureInfo.InvariantCulture);

    public static async Task Queue(ShopDb db, Order order)
    {
        if (string.IsNullOrWhiteSpace(order.Email))
            return;
        var shop = await db.Settings.Select(s => s.Name).SingleAsync();
        var now = DateTime.UtcNow;
        var paid = order.Payment == "Paid";
        var method = order.Method == "COD" ? "Cash on delivery" : "Online payment";
        var balance = paid ? 0 : order.Total;
        var issued =
            TimeZoneInfo
                .ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka"))
                .ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " (Dhaka)";
        static string H(string value) => WebUtility.HtmlEncode(value);
        var rows = new StringBuilder();
        foreach (var line in order.Lines.OrderBy(l => l.Id))
            rows.Append(
                $"<tr><td>{H(line.Brand)} · {H(line.Name)}</td><td>{line.SizeMl} ml</td><td>{line.Quantity}</td><td>{Money(line.UnitPrice)}</td><td>{Money(line.UnitPrice * line.Quantity)}</td></tr>"
            );
        var slip = $$"""
            <!doctype html><html lang="en"><meta charset="utf-8"><title>Order slip {{H(
                order.Reference
            )}}</title>
            <style>body{font:16px/1.5 system-ui,sans-serif;max-width:54rem;margin:2rem auto;padding:0 1rem;color:#211e1b}
            table{width:100%;border-collapse:collapse}td,th{padding:.5rem;text-align:left;border-bottom:1px solid #bbb}</style>
            <h1>{{H(shop)}} · Order slip</h1><p>{{H(
                order.Reference
            )}}<br>{{issued}}<br>Customer: {{H(order.Name)}}</p>
            <table><thead><tr><th>Fragrance</th><th>Size</th><th>Quantity</th><th>Unit price</th><th>Amount</th></tr></thead><tbody>{{rows}}</tbody></table>
            <p>Subtotal: {{Money(order.Subtotal)}}<br>Delivery: {{Money(
                order.Shipping
            )}}<br><strong>Total: {{Money(order.Total)}}</strong></p>
            <p>{{method}} · {{H(order.Payment)}} at confirmation<br>Paid: {{Money(
                paid ? order.Total : 0
            )}}<br>Balance due: {{Money(balance)}}</p></html>
            """;
        var payment = paid
            ? $"Payment received: {Money(order.Total)}."
            : $"Due on delivery: {Money(balance)}.";
        db.Add(
            new ConfirmationMail
            {
                OrderId = order.Id,
                Reference = order.Reference,
                Recipient = order.Email,
                Body =
                    $"Hello {order.Name},\n\n{shop} confirmed your order {order.Reference}. {payment}\n\nYour itemized order slip is attached. Open it in your browser to print or save a PDF.",
                Slip = slip,
                NextAttempt = now,
            }
        );
    }
}

public sealed class ConfirmationMailer(IServiceScopeFactory scopes, ILogger<ConfirmationMailer> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await DeliverOne(ct))
                    continue;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                log.LogWarning("Order email queue unavailable ({Failure})", e.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    async Task<bool> DeliverOne(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDb>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var mail = (
            await db
                .ConfirmationMails.FromSqlRaw(
                    """
                    SELECT * FROM "ConfirmationMails" WHERE "SentAt" IS NULL AND "NextAttempt" <= now()
                    ORDER BY "NextAttempt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                    """
                )
                .ToListAsync(ct)
        ).SingleOrDefault();
        if (mail == null)
            return false;
        try
        {
            await scope
                .ServiceProvider.GetRequiredService<IAccountEmail>()
                .Send(
                    mail.Recipient,
                    $"Order confirmed · {mail.Reference}",
                    mail.Body,
                    ct,
                    new MailFile(
                        mail.Reference + "-order-slip.html",
                        "text/html",
                        Encoding.UTF8.GetBytes(mail.Slip)
                    )
                );
            mail.SentAt = DateTime.UtcNow;
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            mail.Attempts++;
            mail.NextAttempt = DateTime.UtcNow.AddMinutes(
                Math.Min(360, Math.Pow(2, Math.Min(9, mail.Attempts - 1)))
            );
            log.LogWarning(
                "Order {OrderId} email attempt {Attempt} failed ({Failure})",
                mail.OrderId,
                mail.Attempts,
                e.GetType().Name
            );
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
