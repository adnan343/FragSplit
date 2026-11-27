"use client";
import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { api, message, money } from "@/lib/shop";
import {
  DeliveryProfile,
  profileFields,
  accountDestination,
} from "@/lib/account";
import { useCart } from "./Cart";

type Mode = "signin" | "register" | "reset";
type HistoryRow = {
  id: number;
  reference: string;
  createdAt: string;
  payment: string;
  fulfillment: string;
  total: number;
  tracking: string;
};
export function CustomerAccount() {
  const cart = useCart(),
    [mode, select] = useState<Mode>("signin"),
    [notice, setNotice] = useState(""),
    [error, fail] = useState(""),
    [busy, pending] = useState(false);
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const values = Object.fromEntries(new FormData(e.currentTarget));
    pending(true);
    fail("");
    try {
      if (mode === "signin") {
        await api("/auth/login", {
          method: "POST",
          body: JSON.stringify(values),
        });
        location.replace(accountDestination());
        return;
      }
      await api(
        mode === "register" ? "/auth/register" : "/auth/forgot-password",
        {
          method: "POST",
          body: JSON.stringify({
            email: values.email,
            next: accountDestination(),
          }),
        },
      );
      setNotice(String(values.email));
    } catch (e) {
      fail(message(e));
    } finally {
      pending(false);
    }
  }
  if (notice)
    return (
      <section className="panel" role="status">
        <h2>Check your email</h2>
        <p>
          We sent instructions to <strong>{notice}</strong>. Open the email to{" "}
          {mode === "register"
            ? "finish creating your account"
            : "recover your password"}
          .
        </p>
        <button
          onClick={() => {
            setNotice("");
            fail("");
          }}
        >
          Request another email
        </button>
        <button
          onClick={() => {
            setNotice("");
            select("signin");
            fail("");
          }}
        >
          Back to sign in
        </button>
      </section>
    );
  if (!cart.ready && !cart.session.email) return <p>Loading account…</p>;
  if (cart.session.role === "Customer")
    return <Dashboard key={cart.session.id} email={cart.session.email!} />;
  if (cart.session.role === "Admin")
    return (
      <div className="panel">
        <p>Signed in as a store administrator.</p>
        <SignOut />
      </div>
    );
  const title =
    mode === "register"
      ? "Create account"
      : mode === "reset"
        ? "Reset password"
        : "Sign in";
  return (
    <div className="panel">
      <h2>{title}</h2>
      <p>
        {mode === "reset"
          ? "Enter your account email to request a password reset link."
          : mode === "register"
            ? "We’ll email you a verification link. Open it to choose your password."
            : "Save your bag and track orders from any device."}
      </p>
      <form key={mode} className="form" onSubmit={submit}>
        <label>
          Email
          <input
            name="email"
            type="email"
            autoComplete="email"
            maxLength={254}
            required
          />
        </label>
        {mode === "signin" && (
          <label>
            Password
            <input
              name="password"
              autoComplete="current-password"
              type="password"
              maxLength={128}
              required
            />
          </label>
        )}
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
        <button className="primary" disabled={busy}>
          {busy
            ? "Please wait…"
            : mode === "register"
              ? "Send verification email"
              : mode === "reset"
                ? "Send reset email"
                : title}
        </button>
      </form>
      <div className="size-row">
        {(["signin", "register", "reset"] as const)
          .filter((m) => m !== mode)
          .map((m) => (
            <button
              key={m}
              onClick={() => {
                select(m);
                fail("");
              }}
            >
              {m === "register"
                ? "Create account"
                : m === "reset"
                  ? "Forgot password?"
                  : "Sign in"}
            </button>
          ))}
      </div>
    </div>
  );
}
function SignOut() {
  const [error, fail] = useState("");
  return (
    <>
      <button
        onClick={() =>
          api("/auth/logout", { method: "POST" })
            .then(() => location.replace("/account"))
            .catch((e) => fail(message(e)))
        }
      >
        Sign out
      </button>
      {error && <p role="alert">{error}</p>}
    </>
  );
}
function Dashboard({ email }: { email: string }) {
  const [orders, setOrders] = useState<HistoryRow[]>([]),
    [more, setMore] = useState(false),
    [loading, pending] = useState(true),
    [profile, setProfile] = useState<DeliveryProfile | null>(null),
    [error, fail] = useState(""),
    [notice, note] = useState("");
  useEffect(() => {
    let active = true;
    Promise.all([
      api<HistoryRow[]>("/account/orders"),
      api<DeliveryProfile>("/account/profile"),
    ])
      .then(([rows, p]) => {
        if (active) {
          setOrders(rows);
          setMore(rows.length === 25);
          setProfile(p);
        }
      })
      .catch((e) => {
        if (active) fail(message(e));
      })
      .finally(() => {
        if (active) pending(false);
      });
    return () => {
      active = false;
    };
  }, []);
  return (
    <div className="stack">
      <div className="section-head">
        <p>{email}</p>
        <SignOut />
      </div>
      <section className="panel">
        <h2>Your orders</h2>
        {loading ? (
          <p>Loading orders…</p>
        ) : (
          !orders.length &&
          !error && <p>Your account orders will appear here.</p>
        )}
        {orders.map((o) => (
          <Link className="line" href={"/orders/" + o.reference} key={o.id}>
            <div>
              <strong>{o.reference}</strong>
              <p>
                {new Date(o.createdAt).toLocaleDateString("en-BD", {
                  timeZone: "Asia/Dhaka",
                })}{" "}
                · {o.fulfillment} · {o.payment}
              </p>
              {o.tracking && <small>Tracking · {o.tracking}</small>}
            </div>
            <b>{money(o.total)}</b>
          </Link>
        ))}
        {more && (
          <button
            disabled={loading}
            onClick={async () => {
              pending(true);
              try {
                const rows = await api<HistoryRow[]>(
                  "/account/orders?before=" + orders[orders.length - 1].id,
                );
                setOrders([...orders, ...rows]);
                setMore(rows.length === 25);
              } catch (e) {
                fail(message(e));
              } finally {
                pending(false);
              }
            }}
          >
            Older orders
          </button>
        )}
      </section>
      <Link className="button outline" href="/cart">
        View saved bag
      </Link>
      {profile && (
        <details className="panel">
          <summary>Profile and delivery address</summary>
          <form
            className="form"
            onSubmit={async (e) => {
              e.preventDefault();
              try {
                await api("/account/profile", {
                  method: "PUT",
                  body: JSON.stringify(
                    Object.fromEntries(new FormData(e.currentTarget)),
                  ),
                });
                note("Delivery details saved.");
                fail("");
              } catch (e) {
                fail(message(e));
              }
            }}
          >
            {profileFields.map(([k, label]) => (
              <label key={k}>
                {label}
                <input
                  name={k}
                  defaultValue={profile[k]}
                  maxLength={k === "postcode" ? 12 : k === "phone" ? 30 : 50}
                />
              </label>
            ))}
            <button>Save delivery details</button>
            {notice && <p role="status">{notice}</p>}
          </form>
        </details>
      )}
      <button
        onClick={async () => {
          try {
            await api("/account/sign-out-all", { method: "POST" });
            location.replace("/account");
          } catch (e) {
            fail(message(e));
          }
        }}
      >
        Sign out on all devices
      </button>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
    </div>
  );
}
