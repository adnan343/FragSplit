# Set up free Postmark email for FragSplit

The shop will send verification links, password resets, and order slips from `orders@store.example.com`. Postmark handles outgoing mail. If you want to receive customer replies, arrange a monitored inbox or forwarding address separately.

## Get a work email for signup

Postmark asks for an address on a domain you control. Use an address such as `owner@example.com` that can receive its confirmation email. If the domain already has a mail provider (MX records in Cloudflare DNS), create the address or alias with that provider. If it has no mail provider, open Cloudflare **Compute → Email Service → Email Routing**, select `example.com`, and enable inbound routing. Review the proposed DNS records before accepting. Add your personal inbox as a destination, verify it through the email Cloudflare sends, then create a routing rule from `owner@example.com` to that inbox. Send a test from a different address before signing up. The shop can still use `orders@store.example.com` as its outgoing sender. [Postmark signup requirement](https://postmarkapp.com/blog/why-cant-i-use-gmail-address) · [Cloudflare routing rules](https://developers.cloudflare.com/email-service/configuration/email-routing-addresses/)

## Connect the store

1. **Create your Postmark account.** Choose the free Developer plan (100 emails per month) and confirm your signup email. Open **Servers**, then select or create a **Live** server for FragSplit. A Sandbox server simulates delivery. [Postmark pricing](https://postmarkapp.com/pricing)
2. **Add your sending domain.** In **Sender Signatures → Add Domain**, enter `store.example.com` without `https://`. Open its **DNS Settings** and keep that tab open. Postmark generates a DKIM record that proves you control the sending domain and a Return-Path record that handles delivery failures. [Postmark subdomain guide](https://postmarkapp.com/support/article/1198-how-to-send-from-subdomains)
3. **Add two DNS records in Cloudflare.** Open **example.com → DNS → Records → Add record**. Enter the two rows below, saving each separately. Use the actual values shown by Postmark. In this Cloudflare zone the short record name includes `.store`. For example, a full hostname ending in `._domainkey.store.example.com` becomes `<selector>._domainkey.store`. Set TTL to **Auto**. These are new records beneath the existing store tunnel hostname. [Cloudflare instructions from Postmark](https://postmarkapp.com/support/article/adding-dkim-and-return-path-records-to-cloudflare)

   | Type | Cloudflare Name | Content or Target | Proxy |
   |---|---|---|---|
   | TXT | Postmark DKIM hostname with the final `.example.com` removed | Entire DKIM text supplied by Postmark | N/A |
   | CNAME | Postmark Return-Path hostname with the final `.example.com` removed | Target supplied by Postmark, usually `pm.mtasv.net` | **DNS only** (gray cloud) |

4. **Verify and request sending approval.** Click **Verify** for both records in Postmark. DNS checks can take up to 48 hours. As the account owner, select **Request approval**, give the store URL and explain that FragSplit sends customer-requested account verification, password resets, and order confirmations. Wait for approval before testing delivery to Gmail or another outside domain. [Domain verification](https://postmarkapp.com/support/article/how-do-i-verify-a-domain) · [Account approval](https://postmarkapp.com/support/article/1084-how-does-the-account-approval-process-work)
5. **Generate the SMTP credentials.** In the Live server's **Settings**, enable SMTP access. Open its **default Transactional message stream → Settings → SMTP Tokens** and generate a token. Save the **Access Key** and **Secret Key** privately. The secret appears once. [Postmark SMTP guide](https://postmarkapp.com/developer/user-guide/send-email-with-smtp)
6. **Configure FragSplit on your PC.** Open the existing environment file with `nano .env` from the repository root. Set the entries below, replacing the last two placeholders with your keys. Update existing entries and append missing ones. Keep the other settings, including the database password. In nano, save with **Ctrl+O**, **Enter**, then exit with **Ctrl+X**.

   ```dotenv
   PUBLIC_ORIGIN=https://store.example.com
   SMTP_HOST=smtp.postmarkapp.com
   SMTP_PORT=587
   SMTP_STARTTLS=true
   SMTP_FROM=orders@store.example.com
   SMTP_USERNAME=YOUR_ACCESS_KEY
   SMTP_PASSWORD=YOUR_SECRET_KEY
   ```

7. **Apply the settings.** On your PC run the following commands. Use this combination of Compose files for future public deployments too. The Mailpit override sets its own SMTP values, so omit it when using Postmark. The orphan-container warning for an existing Mailpit container is expected.

   ```sh
   docker compose --env-file .env -f infra/docker-compose.yml -f infra/docker-compose.tunnel.yml up -d
   ```

8. **Check delivery from the actual shop.** Open [My account](https://store.example.com/account) and register a new email address you control. Open the verification link, confirm it takes you to the HTTPS store, and finish setting your password. Test **Forgot password** for that same account. To check the order slip, place a test cash-on-delivery order with your own address and confirm it in **Admin → Orders**. Each message counts toward the monthly allowance.

If a message is missing, look for the recipient in the Live server's **Activity**. A delivery event means the receiving mail server accepted the message; check spam as well. If there is no event, check approval, the selected server, and the SMTP keys. If your home ISP blocks port 587, try `SMTP_PORT=2525` with STARTTLS and rerun step 7. [SMTP troubleshooting](https://postmarkapp.com/support/article/common-smtp-connection-errors)
