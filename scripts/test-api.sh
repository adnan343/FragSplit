#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
name="fragsplit-test-$(openssl rand -hex 6)"
export POSTGRES_USER=fragsplit POSTGRES_DB=fragsplit
POSTGRES_PASSWORD="$(openssl rand -hex 32)"
ADMIN_PASSWORD="$(openssl rand -hex 32)"
export POSTGRES_PASSWORD ADMIN_PASSWORD ADMIN_EMAIL=owner@example.test SEED_DEMO=true
export ADMIN_TOTP_SECRET=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ
INGRESS_SECRET="$(openssl rand -hex 32)"
export INGRESS_SECRET
export PUBLIC_ORIGIN=http://localhost:3000
trap 'docker rm -f "$name" >/dev/null 2>&1 || true' EXIT
docker run -d --name "$name" --tmpfs /var/lib/postgresql/data -p 127.0.0.1::5432 \
  -e POSTGRES_USER -e POSTGRES_DB -e POSTGRES_PASSWORD postgres:17 >/dev/null
for attempt in {1..60}; do
  if docker exec "$name" pg_isready -h 127.0.0.1 -U fragsplit >/dev/null 2>&1; then break; fi
  sleep 1
done
port="$(docker port "$name" 5432/tcp)"
export ConnectionStrings__Shop="Host=127.0.0.1;Port=${port##*:};Database=fragsplit;Username=fragsplit;Password=$POSTGRES_PASSWORD"
dotnet test FragSplit.sln
