# Deployment and operations

Deploy the two Docker images behind a TLS reverse proxy and run PostgreSQL 17 on a private network. The Compose file binds web and API to loopback and assigns persistent database, upload and data-protection key volumes. Forward public traffic to Next.js. Its /api rewrite reaches the private API. For a separate API hostname, set API_ORIGIN before the Next build and again at runtime. Use the same exact HTTPS PUBLIC_ORIGIN in both services. Secure cookies are selected from that setting. Set proxy request size limits and deadlines suitable for 5 MB image uploads and gateway verification. Enable HSTS at the TLS terminator.

## Test through an existing Cloudflare Tunnel

For the store.example.com test deployment, the existing cloudflared connector and FragSplit run on the same Docker host. The connector uses the external Docker network named `tunnel`. The tunnel override attaches the web container to that network under the alias `fragsplit-store`.

1. Set `PUBLIC_ORIGIN=https://store.example.com` and `CALLBACK_BASE_URL=https://store.example.com` in the repository's local `.env`. Keep `SSLCOMMERZ_MODE=sandbox` for payment tests.
2. From the repository root, use this command to start the test deployment and whenever rebuilding it after changes. It includes the local Mailpit inbox.

   ```sh
   docker compose --env-file .env -f infra/docker-compose.yml -f infra/docker-compose.mailpit.yml -f infra/docker-compose.tunnel.yml up -d --build
   ```

3. In Cloudflare, open **Networking → Tunnels**, select the existing tunnel, then choose **Routes → Add route → Published application**. Enter subdomain `store`, domain `example.com`, and leave the path blank. Set the service to HTTP at `fragsplit-store:3000` and save. Cloudflare creates the DNS record when you save the dashboard route. [Cloudflare routing guide](https://developers.cloudflare.com/tunnel/concepts/routing/)
4. Open the public HTTPS store address on your phone and use that address for sign-in and checkout. To test email verification, open the message in [the local Mailpit inbox](http://localhost:8025) on the host. Its verification link should open the public store.

To switch back to local-only testing, remove the store hostname route and its DNS record in Cloudflare, set `PUBLIC_ORIGIN=http://localhost:3000` and clear `CALLBACK_BASE_URL` in `.env`, then run the local Compose command in the README.

## Secrets and launch
- Inject secrets through your platform secret manager. Use independent database, bootstrap, bucket and merchant credentials. Disable demo seeding, configure a real support contact and policies, upload licensed product imagery and review every enabled price and delivery rule.
- Bootstrap the owner once, then remove ADMIN_PASSWORD and ADMIN_EMAIL from the running environment. Reset an owner session by rotating the key ring only as part of a planned credential incident response. A key rotation that discards old keys also invalidates existing private guest links. Protect key material at rest with encrypted persistent storage.
- For S3-compatible images set S3_BUCKET, AWS_REGION and optional S3_ENDPOINT. Use a workload identity or scoped AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY. Limit access to the product image bucket. Images are decoded and served through the API.
- Keep sandbox and live SSLCommerz environments separate. Configure mode, credentials and public HTTPS callback base on the API. Verify callback reachability and test both browser and IPN flows with merchant credentials. Record refunds only after the merchant has refunded externally.
- Set a customer-data retention period with the business owner. Limit order/customer access to authorized operators, encrypt backups and volumes, redact request bodies, authorization headers, cookies, query strings and tokens from edge logs and tracing. Use synthetic customer information for test environments.

## Migrations, backups and rollback
Each release contains EF migrations. The single API startup applies them before accepting traffic. For multiple replicas, perform the migration once as a release step and roll out compatible application versions. Inspect the SQL before applying a production migration.

```sh
dotnet tool restore
dotnet ef migrations script --idempotent --project apps/api --output /tmp/fragsplit-migrate.sql
docker compose --env-file .env -f infra/docker-compose.yml exec -T db pg_dump -U fragsplit -Fc fragsplit > fragsplit-backup.dump
```

Retain encrypted off-host database backups and matching media/key backups. Periodically restore into a fresh database with pg_restore, connect a staging API and compare order counts, balances and ledger totals. Snapshot the database before upgrades. For a failed deploy, redeploy the prior image only when its model is compatible. Otherwise stop writers and restore the verified backup into a new database. Review any orders or gateway events received after the backup and reconcile them before resuming fulfillment.

## Monitoring
Probe /health for database connectivity and / for storefront readiness. JSON logs include request trace IDs. Monitor HTTP 5xx, failed expiry passes, stock conflicts, orders marked NeedsReview, old pending payments and backup age. Reconcile the merchant settlement report with paid orders and validation IDs daily. Restrict telemetry to identifiers and sanitized status codes. All clients behind a single Next reverse proxy can share the API IP rate-limit budget. Set per-client limits at the trusted public edge and load-test the configured service limit before launch.

## Go-live verification
Complete the acceptance checklist, production TLS and backup restore drill, storage-provider integration test and an actual merchant sandbox transaction. Verify the merchant control panel, amount, inventory and confirmation on both the successful payment and decline/cancel paths. Choose real rates and policies before accepting customer orders.
