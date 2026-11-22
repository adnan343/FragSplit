"use client";
import { useState } from "react";
import { Feedback, useResource } from "@/components/Admin";
import { Order } from "@/lib/orders";
import { api, message, money } from "@/lib/shop";
import { OrderSummary } from "@/components/OrderSummary";
const states = [
  "Pending",
  "Confirmed",
  "Processing",
  "Shipped",
  "Delivered",
  "Cancelled",
  "Returned",
];
export default function Orders() {
  const [q, query] = useState(""),
    [page, setPage] = useState(1),
    [ref, select] = useState("");
  const { data, error, reload } = useResource<Order[]>(
    "/admin/orders?" + q + "&page=" + page,
  );
  return (
    <>
      <h1>Orders</h1>
      <form
        className="filters"
        onSubmit={(e) => {
          e.preventDefault();
          const p = new URLSearchParams();
          new FormData(e.currentTarget).forEach((v, k) => {
            if (v) p.set(k, String(v));
          });
          query(p.toString());
          setPage(1);
        }}
      >
        <input
          aria-label="Search orders"
          name="search"
          placeholder="Reference, name or phone"
        />
        <select aria-label="Fulfillment status" name="status">
          <option value="">All statuses</option>
          {states.map((s) => (
            <option key={s}>{s}</option>
          ))}
        </select>
        <select aria-label="Payment method" name="method">
          <option value="">All payments</option>
          <option>COD</option>
          <option>Online</option>
        </select>
        <label>
          From <input type="date" name="from" />
        </label>
        <label>
          To <input type="date" name="to" />
        </label>
        <button>Filter orders</button>
      </form>
      <Feedback error={error} />
      {data?.map((o) => (
        <div className="line" key={o.reference}>
          <div>
            <b>{o.reference}</b>
            <p>
              {o.name} · {money(o.total)} · {o.payment} · {o.fulfillment}
              {o.needsReview ? " · Review required" : ""}
            </p>
          </div>
          <button onClick={() => select(o.reference)}>View order</button>
        </div>
      ))}
      {data?.length === 0 && <p>No matching orders.</p>}
      <div className="pager">
        <button disabled={page === 1} onClick={() => setPage(page - 1)}>
          Previous
        </button>
        <button
          disabled={data?.length !== 40}
          onClick={() => setPage(page + 1)}
        >
          Next
        </button>
      </div>
      {ref && <Manage key={ref} reference={ref} refresh={reload} />}
    </>
  );
}
function Manage({
  reference,
  refresh,
}: {
  reference: string;
  refresh: () => void;
}) {
  const {
      data: o,
      error,
      reload,
    } = useResource<Order>("/admin/orders/" + reference),
    [err, fail] = useState("");
  if (!o) return <Feedback error={error} />;
  const actions: Record<string, string[]> = {
    Pending: ["Confirmed", "Cancelled"],
    Confirmed: ["Processing", "Cancelled"],
    Processing: ["Shipped", "Cancelled"],
    Shipped: ["Delivered", "Cancelled"],
    Delivered: ["Returned"],
  };
  const options = (actions[o.fulfillment] || []).filter(
    (a) =>
      a !== "Confirmed" ||
      o.method === "COD" ||
      (o.payment === "Paid" && !o.needsReview),
  );
  if (
    o.fulfillment === "Delivered" &&
    o.method === "COD" &&
    o.payment === "Unpaid"
  )
    options.push("Collect");
  if (o.payment === "Paid") options.push("Refund");
  return (
    <div className="columns">
      <OrderSummary o={o} />
      <section className="panel">
        <h2>Manage order</h2>
        {o.method === "Online" && (
          <button
            onClick={async () => {
              try {
                await api("/admin/orders/" + reference + "/reconcile", {
                  method: "POST",
                });
                reload();
                refresh();
                fail("");
              } catch (e) {
                fail(message(e));
              }
            }}
          >
            Check provider status
          </button>
        )}
        {o.verification && <p>Provider result · {o.verification}</p>}
        {options.length > 0 && (
          <form
            className="form"
            onSubmit={async (e) => {
              e.preventDefault();
              const fd = new FormData(e.currentTarget);
              try {
                await api("/admin/orders/" + reference + "/status", {
                  method: "PATCH",
                  body: JSON.stringify({
                    ...Object.fromEntries(fd),
                    recoverStock: fd.has("recoverStock"),
                  }),
                });
                reload();
                refresh();
                fail("");
              } catch (e) {
                fail(message(e));
              }
            }}
          >
            <label>
              Action
              <select name="action">
                {options.map((a) => (
                  <option key={a} value={a}>
                    {a === "Refund"
                      ? "Record completed refund"
                      : a === "Collect"
                        ? "Record COD collection"
                        : a}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Reason
              <input name="reason" />
            </label>
            <label>
              Tracking reference
              <input name="tracking" />
            </label>
            <label>
              <span>
                <input type="checkbox" name="recoverStock" />
                Approve return of physically recoverable liquid to stock
              </span>
            </label>
            <button>Apply order action</button>
          </form>
        )}
        <Feedback error={err || error} />
        <h3>Status history</h3>
        {o.history.map((h) => (
          <p key={h.id}>
            {h.event} · {h.actor} ·{" "}
            {new Date(h.at).toLocaleString("en-BD", { timeZone: "Asia/Dhaka" })}{" "}
            {h.reason}
          </p>
        ))}
      </section>
    </div>
  );
}
