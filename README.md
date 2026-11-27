# VialMuse
A single-vendor fragrance decant store for Bangladesh. The storefront and admin share a Next.js App Router application. ASP.NET Core owns authorization, checkout and liquid stock. PostgreSQL stores orders, stock movements and reporting history.

## Run locally
Install Docker Engine with Compose v2 or later and Python 3. Run these commands from the repository root.

```sh
python3 scripts/dev-env.py
docker compose --env-file .env -f infra/docker-compose.yml -f infra/docker-compose.mailpit.yml up --build -d
```

Open [the storefront](http://localhost:3000) or check [service health](http://localhost:3000/health). Visit `/admin` directly for administration. The first boot runs database migrations using the migration role. With `SEED_DEMO=true`, it loads six fictional fragrances and labeled artwork. The bootstrap script writes the local sign-in credentials to an owner-only `.env` file. Add `ADMIN_TOTP_SECRET` as a time-based setup key in an authenticator app, then sign in with `ADMIN_EMAIL`, `ADMIN_PASSWORD` and the current six-digit code.

Use distinct database and ingress credentials in each environment and an admin password of at least 14 characters. Checkout uses cash on delivery. Unconfirmed orders reserve stock for 24 hours by default, and each customer can hold two pending orders.

```sh
docker compose --env-file .env -f infra/docker-compose.yml -f infra/docker-compose.mailpit.yml down
```

This stops the containers and preserves the named volumes.

## Customer accounts

Open [My account](http://localhost:3000/account) to register or sign in. Registration sends a verification email. Open its link within one hour to choose a password. **Forgot password?** sends a single-use reset link valid for 20 minutes. Completing a reset signs out existing customer sessions. Customers can save a default delivery address, resume their shopping bag and see fulfillment, payment and tracking updates. To add an earlier guest purchase, open its private receipt while signed in and choose **Save this order to my account**.

The local Compose command above includes Mailpit. Open the [local test inbox](http://localhost:8025) to read verification and reset emails. It captures messages for any recipient on this computer. For delivery to real inboxes, run the base Compose file alone, set `PUBLIC_ORIGIN` to the public HTTPS store URL and configure `SMTP_HOST`, `SMTP_PORT`, `SMTP_FROM`, `SMTP_USERNAME` and `SMTP_PASSWORD` in the deployment environment. Use a sender authorized by your mail provider and its STARTTLS endpoint (usually port 587). Keep `SMTP_STARTTLS=true`. Missing mail configuration leaves guest checkout and existing sign-ins available.

Customers from before email verification can sign in as usual. Their first email password reset also asks for their current password or original saved recovery code to confirm ownership of their existing order history. Later resets require only the emailed link.

For physical devices to share an account, both must reach the same store URL. The default Compose setup binds to the host’s loopback interface. Production uses the existing Cloudflare Tunnel at `store.adnanhossain.me`. [Deployment guide](docs/deployment.md)

## Order confirmation emails

Confirm a pending cash-on-delivery order in **Admin → Orders** to send the customer an itemized order slip. Checkout requires an email for every order. Guests can use **Find an order** to request a private receipt link at the checkout email address. The link expires after 30 minutes. Signed-in customers start with their account email and the delivery details saved from their last checkout.

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

For hot reload, point `ConnectionStrings__Shop` at a development PostgreSQL database and set `PUBLIC_ORIGIN`, `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `ADMIN_TOTP_SECRET` and `SEED_DEMO`. Run `dotnet run --project apps/api --urls http://127.0.0.1:5080` and run `pnpm dev` in `apps/web`. The development server uses the Next.js same-origin rewrite. Set `API_ORIGIN` before starting Next.js if the API is elsewhere.

## Project guides
- [Implementation plan](docs/implementation-plan.md) and [acceptance results](docs/acceptance.md)
- [Architecture, inventory and reporting definitions](docs/architecture.md)
- [API and authentication](docs/api.md)
- [Deployment and recovery](docs/deployment.md)
- [COD and payment scope](docs/payments.md)
- [Seed artwork and provenance](docs/assets.md)
