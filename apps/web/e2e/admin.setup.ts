import { createHmac } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { dirname } from "node:path";
import { expect, test } from "@playwright/test";
import { adminSession } from "./admin-session";

function totp(secret: string) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const bits = Array.from(secret.toUpperCase(), (c) =>
    alphabet.indexOf(c).toString(2).padStart(5, "0"),
  ).join("");
  const key = Buffer.from(
    bits.match(/.{8}/g)!.map((byte) => parseInt(byte, 2)),
  );
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000)));
  const hash = createHmac("sha1", key).update(counter).digest();
  const number = hash.readUInt32BE(hash[hash.length - 1] & 15) & 0x7fffffff;
  return String(number % 1_000_000).padStart(6, "0");
}

test("an admin signs in from the direct route with a password and authenticator", async ({
  page,
}) => {
  const { ADMIN_EMAIL, ADMIN_PASSWORD, ADMIN_TOTP_SECRET } = process.env;
  if (
    !ADMIN_EMAIL ||
    !ADMIN_PASSWORD ||
    !ADMIN_TOTP_SECRET?.match(/^[A-Z2-7]{32}$/i)
  )
    throw new Error("Set the ephemeral admin credentials for browser tests.");
  await page.goto("/admin");
  await expect(
    page.getByRole("heading", { name: "Admin sign in" }),
  ).toBeVisible();
  await page.getByLabel("Email", { exact: true }).fill(ADMIN_EMAIL);
  await page.getByLabel("Password", { exact: true }).fill(ADMIN_PASSWORD);
  await page.getByLabel("Authenticator code").fill(totp(ADMIN_TOTP_SECRET));
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(
    page.getByRole("link", { name: "Inventory", exact: true }),
  ).toBeVisible();
  await mkdir(dirname(adminSession), { recursive: true, mode: 0o700 });
  await writeFile(
    adminSession,
    JSON.stringify(await page.context().storageState()),
    {
      mode: 0o600,
    },
  );
});
