"use client";
import { useState, useEffect, useCallback } from "react";
import Link from "next/link";
import { useCart } from "./Cart";
import { OrderSummary } from "./OrderSummary";
import { api, message } from "@/lib/shop";
import { Order } from "@/lib/orders";
export function GuestOrder({ reference }: { reference: string }) {
  const cart = useCart();
  return (
    <OrderDetails
      key={reference + ":" + (cart.session.id ?? "guest")}
      reference={reference}
      signedIn={cart.session.id !== null}
    />
  );
}
function OrderDetails({
  reference,
  signedIn,
}: {
  reference: string;
  signedIn: boolean;
}) {
  const [o, set] = useState<Order | null>(null),
    [token, access] = useState<string | null>(null),
    [error, fail] = useState(""),
    [busy, pending] = useState(false);
  // The private fragment and session storage are available only after hydration.
  useEffect(() => {
    const fragment = new URLSearchParams(location.hash.slice(1));
    const t =
      fragment.get("token") ||
      sessionStorage.getItem("fs.order." + reference) ||
      "";
    // eslint-disable-next-line react-hooks/set-state-in-effect
    access(t);
    if (t) {
      sessionStorage.setItem("fs.order." + reference, t);
      if (location.hash) history.replaceState(null, "", location.pathname);
    }
  }, [reference]);
  const reload = useCallback(() => {
    if (token !== null)
      api<Order>("/orders/" + reference + "/status", {
        headers: { "X-Order-Token": token || "" },
      })
        .then((d) => {
          set(d);
          fail("");
        })
        .catch((e) => {
          set(null);
          fail(message(e));
        });
  }, [reference, token]);
  useEffect(() => {
    reload();
  }, [reload]);
  return (
    <div className="stack">
      {error && (
        <p role="alert" className="error">
          {error}{" "}
          <Link href={"/account?next=/orders/" + reference}>My account</Link>
        </p>
      )}
      {o && (
        <>
          <OrderSummary o={o} />
          {o.accountOrder ? (
            <Link href="/account">View all my orders</Link>
          ) : (
            token && (
              <>
                {signedIn ? (
                  <button
                    onClick={async () => {
                      try {
                        await api("/account/orders/" + reference + "/claim", {
                          method: "POST",
                          headers: { "X-Order-Token": token },
                        });
                        sessionStorage.removeItem("fs.order." + reference);
                        access("");
                      } catch (e) {
                        fail(message(e));
                      }
                    }}
                  >
                    Save this order to my account
                  </button>
                ) : (
                  <Link href={"/account?next=/orders/" + reference}>
                    Sign in to save this order
                  </Link>
                )}
                <label>
                  Private order link — save for future access
                  <input
                    readOnly
                    value={
                      location.origin +
                      "/orders/" +
                      reference +
                      "#token=" +
                      encodeURIComponent(token)
                    }
                    onFocus={(e) => e.target.select()}
                  />
                </label>
              </>
            )
          )}
          <section className="panel">
            <h3>Order updates</h3>
            <ol>
              {o.history.map((h) => (
                <li key={h.id}>
                  <strong>{h.event}</strong> ·{" "}
                  <time dateTime={h.at}>
                    {new Date(h.at).toLocaleString("en-BD", {
                      timeZone: "Asia/Dhaka",
                      dateStyle: "medium",
                      timeStyle: "short",
                    })}
                  </time>
                </li>
              ))}
            </ol>
          </section>
          <button onClick={reload}>Refresh status</button>
          {o.method === "Online" &&
            o.payment === "Pending" &&
            o.stockState === "Reserved" &&
            !o.needsReview && (
              <button
                className="primary"
                disabled={busy}
                onClick={async () => {
                  pending(true);
                  try {
                    const r = await api<{ url: string }>(
                      "/payments/sslcommerz/initiate",
                      {
                        method: "POST",
                        headers: { "X-Order-Token": token || "" },
                        body: JSON.stringify({ reference }),
                      },
                    );
                    location.assign(r.url);
                  } catch (e) {
                    fail(message(e));
                    pending(false);
                  }
                }}
              >
                Continue to secure payment
              </button>
            )}
        </>
      )}
    </div>
  );
}
