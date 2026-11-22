# FragSplit
A single-vendor fragrance decant store for Bangladesh. The storefront and admin share a Next.js App Router application. ASP.NET Core owns authorization, prices, payment verification and liquid stock. PostgreSQL persists the order, stock ledger and reporting history.

## Run locally
Install Docker Engine with Compose v2 or later and Python 3. Run these commands from the repository root.

```sh
python3 scripts/dev-env.py
docker compose --env-file .env -f infra/docker-compose.yml -f infra/docker-compose.mailpit.yml up --build -d
```

Open [the storefront](http://localhost:3000), [administration](http://localhost:3000/admin), [API health](http://localhost:5080/health) or the [OpenAPI document](http://localhost:5080/openapi/v1.json). The first boot applies EF migrations. With `SEED_DEMO=true`, it loads six fictional fragrances and labeled artwork. The bootstrap script creates a fresh local password in the ignored, owner-readable `.env` file. Use its `ADMIN_EMAIL` and `ADMIN_PASSWORD` at the admin sign-in. Existing files and account passwords are preserved on restart.

To supply your own configuration, copy `.env.example` to `.env`. Set a random database password and a separate admin password of at least 14 characters. The store supports guest COD immediately. Online checkout becomes available with configured merchant credentials and an HTTPS callback origin. The production checkout uses the merchant’s enabled hosted payment channels.

```sh
docker compose --env-file .env -f infra/docker-compose.yml -f infra/docker-compose.mailpit.yml down
```

This stops the containers and preserves the named volumes.

## Customer accounts

Open [My account](http://localhost:3000/account) to register or sign in. Registration sends a verification email. Open its link within one hour to choose a password. **Forgot password?** sends a single-use reset link valid for 20 minutes. Completing a reset signs out existing customer sessions. Customers can save a default delivery address, resume their shopping bag and see fulfillment, payment and tracking updates. To add an earlier guest purchase, open its private receipt while signed in and choose **Save this order to my account**.

The local Compose command above includes Mailpit. Open the [local test inbox](http://localhost:8025) to read verification and reset emails. It captures messages for any recipient on this computer. For delivery to real inboxes, run the base Compose file alone, set `PUBLIC_ORIGIN` to the public HTTPS store URL and configure `SMTP_HOST`, `SMTP_PORT`, `SMTP_FROM`, `SMTP_USERNAME` and `SMTP_PASSWORD` in the deployment environment. Use a sender authorized by your mail provider and its STARTTLS endpoint (usually port 587). Keep `SMTP_STARTTLS=true`. Missing mail configuration leaves guest checkout and existing sign-ins available.

Customers from before email verification can sign in as usual. Their first email password reset also asks for their current password or original saved recovery code to confirm ownership of their existing order history. Later resets require only the emailed link.

For physical devices to share an account, both must reach the same store URL. The default Compose setup binds to the host’s loopback interface. To use the shared HTTPS test store, follow the [Cloudflare Tunnel setup and rebuild instructions](docs/deployment.md#test-through-an-existing-cloudflare-tunnel).

## Order confirmation emails

Confirm a pending cash-on-delivery order in **Admin → Orders** to send the customer an itemized order slip. Successful online payment verification sends the slip automatically when it confirms the order. Checkout requires an email for every order. Signed-in customers start with their account email and the delivery details saved from their last checkout.

The attached HTML slip opens in a browser and can be printed or saved as a PDF. It shows the payment status at confirmation, including the amount due on delivery. The store queues the email when confirmation commits and retries delivery after SMTP failures. Local testing uses the same Mailpit inbox as account verification.

## Development and tests
Use .NET 10 SDK, Node.js 24, pnpm 11.25, Docker, Bash and OpenSSL. The API test command starts and removes a dedicated PostgreSQL 17 container. It seeds test accounts with generated credentials. Run browser tests against a disposable seeded store because the tests place orders and create listings.

```sh
dotnet tool restore
bash scripts/test-api.sh
cd apps/web
pnpm install --frozen-lockfile --ignore-scripts
pnpm lint
pnpm build
pnpm typecheck
pnpm exec playwright install chromium
set -a
. ../../.env
set +a
pnpm e2e
```

For hot reload, point `ConnectionStrings__Shop` at a development PostgreSQL database, set `PUBLIC_ORIGIN`, `ADMIN_EMAIL`, `ADMIN_PASSWORD` and `SEED_DEMO`, then run `dotnet run --project apps/api --urls http://127.0.0.1:5080`. In another terminal run `pnpm dev` from `apps/web`. Browser requests use the Next.js same-origin rewrite. Set `API_ORIGIN` before building or starting Next.js if the API is elsewhere.

## Project guides
- [Implementation plan](docs/implementation-plan.md) and [acceptance results](docs/acceptance.md)
- [Architecture, inventory and reporting definitions](docs/architecture.md)
- [API and authentication](docs/api.md)
- [Deployment and recovery](docs/deployment.md)
- [SSLCommerz sandbox setup and acceptance procedure](docs/payments.md)
- [Seed artwork and provenance](docs/assets.md)
