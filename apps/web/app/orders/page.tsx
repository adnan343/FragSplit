"use client";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { api, message } from "@/lib/shop";

export default function Lookup() {
  const reference = useSearchParams().get("reference") ?? "";
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, fail] = useState("");
  return (
    <main id="main" className="wrap narrow section">
      <h1>Find your order</h1>
      <p>
        <Link href="/account">View my account orders</Link>
      </p>
      <form
        className="panel form"
        onSubmit={async (e) => {
          e.preventDefault();
          setBusy(true);
          setSent(false);
          fail("");
          try {
            await api("/orders/receipt-link", {
              method: "POST",
              body: JSON.stringify(
                Object.fromEntries(new FormData(e.currentTarget)),
              ),
            });
            setSent(true);
          } catch (e) {
            fail(message(e));
          } finally {
            setBusy(false);
          }
        }}
      >
        <p>
          Enter your order reference and the email address you used at checkout.
        </p>
        <label>
          Order reference
          <input
            name="reference"
            required
            maxLength={30}
            defaultValue={reference}
            placeholder="VM-…"
          />
        </label>
        <label>
          Email
          <input name="email" type="email" autoComplete="email" required />
        </label>
        <button disabled={busy}>
          {busy ? "Sending…" : "Email me a receipt link"}
        </button>
        {sent && (
          <p role="status">
            If the details match a guest order, you’ll receive a private link
            that expires in 30 minutes.
          </p>
        )}
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
      </form>
    </main>
  );
}
