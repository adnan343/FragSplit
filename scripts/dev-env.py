"""Create local-only Compose credentials without writing them to stdout."""
import argparse
import base64
import os
import secrets
from pathlib import Path

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path, default=root / ".env")
args = parser.parse_args()
content = (root / ".env.example").read_text()
for key, value in {
    "POSTGRES_PASSWORD": secrets.token_hex(32),
    "SHOP_MIGRATION_PASSWORD": secrets.token_hex(32),
    "SHOP_RUNTIME_PASSWORD": secrets.token_hex(32),
    "INGRESS_SECRET": secrets.token_hex(32),
    "CATALOG_ORIGIN_SECRET": secrets.token_hex(32),
    "ADMIN_EMAIL": "owner@example.test",
    "ADMIN_PASSWORD": secrets.token_urlsafe(32),
    "ADMIN_TOTP_SECRET": base64.b32encode(secrets.token_bytes(20)).decode(),
}.items():
    content = content.replace(f"\n{key}=\n", f"\n{key}={value}\n")
try:
    fd = os.open(args.output, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
except FileExistsError:
    parser.exit(1, f"{args.output} exists. Existing credentials were preserved.\n")
with os.fdopen(fd, "w") as output:
    output.write(content)
print(f"Local connection settings and admin login saved to {args.output}.")
