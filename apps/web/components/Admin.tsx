"use client";
import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { api, ApiError, message } from "@/lib/shop";
export function useResource<T>(path: string) {
  const [data, set] = useState<T | null>(null),
    [error, fail] = useState("");
  const [revision, bump] = useState(0);
  useEffect(() => {
    let live = true;
    api<T>(path)
      .then((d) => {
        if (live) {
          set(d);
          fail("");
        }
      })
      .catch((e) => {
        if (live) fail(message(e));
      });
    return () => {
      live = false;
    };
  }, [path, revision]);
  const reload = useCallback(() => bump((x) => x + 1), []);
  return { data, error, reload };
}
export function AdminShell({ children }: { children: React.ReactNode }) {
  const [state, set] = useState("loading"),
    [error, fail] = useState("");
  useEffect(() => {
    api("/admin/me")
      .then(() => set("in"))
      .catch((e) => {
        if (e instanceof ApiError && [401, 403].includes(e.status)) set("out");
        else fail(message(e));
      });
  }, []);
  return (
    <main id="main" className="wrap section">
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {state === "loading" ? (
        <p>Checking session…</p>
      ) : state === "out" ? (
        <form
          className="panel form narrow"
          onSubmit={async (e) => {
            e.preventDefault();
            const fd = new FormData(e.currentTarget);
            try {
              await api("/auth/admin/login", {
                method: "POST",
                body: JSON.stringify({
                  email: fd.get("email"),
                  password: fd.get("password"),
                  code: fd.get("code"),
                }),
              });
              await api("/admin/me");
              window.location.reload();
              fail("");
            } catch (e) {
              fail(message(e));
            }
          }}
        >
          <h1>Admin sign in</h1>
          <label>
            Email
            <input type="email" name="email" required autoComplete="username" />
          </label>
          <label>
            Password
            <input
              type="password"
              name="password"
              required
              autoComplete="current-password"
            />
          </label>
          <label>
            Authenticator code
            <input
              name="code"
              required
              inputMode="numeric"
              autoComplete="one-time-code"
              pattern="[0-9]{6}"
              maxLength={6}
            />
          </label>
          <button className="primary">Sign in</button>
        </form>
      ) : (
        <>
          <div className="section-head">
            <p className="eyebrow">Store administration</p>
            <button
              onClick={async () => {
                try {
                  await api("/auth/logout", { method: "POST" });
                  window.location.reload();
                } catch (e) {
                  fail(message(e));
                }
              }}
            >
              Sign out
            </button>
          </div>
          <nav className="admin-nav">
            {[
              ["Overview", ""],
              ["Products", "/products"],
              ["Inventory", "/inventory"],
              ["Orders", "/orders"],
              ["Settings", "/settings"],
            ].map(([label, url]) => (
              <Link href={"/admin" + url} key={label}>
                {label}
              </Link>
            ))}
          </nav>
          {children}
        </>
      )}
    </main>
  );
}
export function Feedback({ error, text }: { error?: string; text?: string }) {
  return (
    <>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {text && (
        <p role="status" className="success">
          {text}
        </p>
      )}
    </>
  );
}
