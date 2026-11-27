# Deploy VialMuse

## Production on the existing Cloudflare Tunnel

The public storefront is `https://store.adnanhossain.me`. Cloudflare sends traffic to `http://fragsplit-store:3000` on the external Docker network `tunnel`. The existing service alias and Compose project name retain the tunnel route and named storage volumes. Compose publishes only the ingress proxy on the PC's loopback interface. The web, API and PostgreSQL services communicate on the private project network.

Keep `PUBLIC_ORIGIN=https://store.adnanhossain.me` and `SEED_DEMO=false` in the owner-only `.env`. Set distinct random hex values for `POSTGRES_PASSWORD`, `SHOP_MIGRATION_PASSWORD`, `SHOP_RUNTIME_PASSWORD` and `INGRESS_SECRET`. For an existing cluster, `POSTGRES_PASSWORD` is its current bootstrap password. Keep the SMTP configuration for the authorized sender and preserve the `pgdata`, `uploads` and `keys` volumes.

Run the deployment from the repository root.

```sh
docker compose --env-file .env -f infra/docker-compose.yml -f infra/docker-compose.tunnel.yml up --build -d --wait
```

The `db-roles` job provisions separate PostgreSQL users. The `migrate` job owns schema migration and credential enrollment. The API uses `vialmuse_api`, which can read and write the application tables it needs. The migration history and stock-ledger updates remain outside its privileges.

Nginx uses the Cloudflare client address only for connections from the tunnel connector. Set `TRUSTED_TUNNEL_PROXY` if the connector's Docker DNS name differs from `cloudflared`. Reload the ingress after recreating the connector so Nginx resolves its current address. The Cloudflare edge handles the HTTPS redirect and certificate. [Cloudflare routing guide](https://developers.cloudflare.com/tunnel/concepts/routing/) · [Cloudflare visitor IP header](https://developers.cloudflare.com/fundamentals/reference/http-headers/#cf-connecting-ip)

After a release, check `/health`, the public catalog, HTTP-to-HTTPS redirect and the browser checkout path on a separate test database. Use the base Compose file plus `infra/docker-compose.mailpit.yml` for the isolated mail test. The live database retains historic orders and stock movements.

## Administrator

Open `/admin` directly. Add the `ADMIN_TOTP_SECRET` value from the private environment file as a time-based setup key in an authenticator app. Sign in with the configured email, password and current six-digit code. Password-only customer sign-in is separate.

To reset the password and authenticator, update `ADMIN_PASSWORD` and `ADMIN_TOTP_SECRET` in the private environment file. Run the following with the matching `ADMIN_EMAIL`. The reset invalidates existing administrator sessions.

```sh
docker compose --env-file .env -f infra/docker-compose.yml -f infra/docker-compose.tunnel.yml run --rm migrate reset-admin
```

## Backups and restore

Store a PostgreSQL custom-format dump, a media and key archive and the matching environment file in an owner-only location. An exact pre-cutover backup requires pausing the application writers. Capture the media and key archive while the API is available or from a read-only mount of its stopped container volumes.

```sh
umask 077
docker compose --env-file .env -f infra/docker-compose.yml exec -T db pg_dump -U fragsplit -Fc fragsplit > shop.dump
docker compose --env-file .env -f infra/docker-compose.yml exec -T api tar -czf - -C /app uploads keys > media-and-keys.tar.gz
```

Restore to a separate fresh PostgreSQL volume using `pg_restore --exit-on-error`, restore the media and keys to their matching volumes, and run role provisioning followed by migration. Confirm the restored counts, authorized sessions and public catalogue on an isolated network. Keep the backups away from the web root. The data protection keys are required for existing private links and the administrator's encrypted authenticator key.

The one-time `infra/remove-demo-catalog.sql` script identifies original samples by their seed stock movement and demo tag. It removes unreferenced samples and archives those associated with purchase or stock history. Run it through the database bootstrap user during the cutover, with a verified backup and the application writers stopped.
