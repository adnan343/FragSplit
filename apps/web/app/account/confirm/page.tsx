"use client";
import Link from "next/link";
import { useSyncExternalStore, useState, type FormEvent } from "react";
import { api, message } from "@/lib/shop";
import { accountDestination } from "@/lib/account";

function subscribeToLink(change: () => void) {
  window.addEventListener("hashchange", change);
  window.addEventListener("popstate", change);
  return () => {
    window.removeEventListener("hashchange", change);
    window.removeEventListener("popstate", change);
  };
}

export default function ConfirmAccount() {
  const href = useSyncExternalStore(
    subscribeToLink,
    () => location.href,
    () => "",
  );
  const url = href ? new URL(href) : null;
  const mode = url?.searchParams.get("mode") || "";
  const token = new URLSearchParams(url?.hash.slice(1)).get("token");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [finished, setFinished] = useState(false);

  const registering = mode === "register";
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || busy) return;
    const fields = Object.fromEntries(new FormData(event.currentTarget));
    setBusy(true);
    setError("");
    try {
      await api(registering ? "/auth/verify-email" : "/auth/reset-password", {
        method: "POST",
        body: JSON.stringify({ ...fields, token }),
      });
      if (registering) location.replace(accountDestination());
      else {
        history.replaceState(null, "", location.pathname);
        setFinished(true);
      }
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main id="main" className="wrap section narrow">
      <h1>{registering ? "Verify your email" : "Account access"}</h1>
      {!url ? (
        <p role="status">Opening your email link…</p>
      ) : finished ? (
        <div className="panel">
          <h2>Password updated</h2>
          <Link className="button primary" href="/account">
            Sign in with your new password
          </Link>
        </div>
      ) : !token || !["register", "reset", "legacy"].includes(mode) ? (
        <div className="panel">
          <p>
            This link is incomplete. Open the full link from your email or
            request a new one.
          </p>
          <Link href="/account">Go to my account</Link>
        </div>
      ) : (
        <form className="panel form" onSubmit={submit}>
          <h2>
            {registering ? "Create your password" : "Choose a new password"}
          </h2>
          {mode === "legacy" && (
            <label>
              This account predates email verification. Enter your current
              password or original saved recovery code.
              <input
                name="previousCredential"
                type="password"
                autoComplete="current-password"
                maxLength={128}
                required
              />
            </label>
          )}
          <label>
            New password
            <input
              name="password"
              type="password"
              autoComplete="new-password"
              minLength={8}
              pattern={String.raw`(?=.*\p{Lu})(?=.*\p{Ll})(?=.*\p{Nd})(?=.*[\p{P}\p{S}]).{8,}`}
              maxLength={128}
              aria-describedby="password-hint"
              required
            />
          </label>
          <small id="password-hint">
            Use 8 to 128 characters with an uppercase letter, a lowercase
            letter, a number and a special character such as ! or #.
          </small>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <button className="primary" disabled={busy}>
            {busy
              ? "Saving…"
              : registering
                ? "Verify and create account"
                : "Reset password"}
          </button>
          <Link href="/account">Request a new email</Link>
        </form>
      )}
    </main>
  );
}
