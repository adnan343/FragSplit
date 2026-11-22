"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { zodResolver } from "@hookform/resolvers/zod";
import { api, ApiError, Settings, message, money } from "@/lib/shop";
import { DeliveryProfile } from "@/lib/account";
import { useCart } from "./Cart";
const schema = z.object({
  name: z.string().trim().min(1).max(50),
  phone: z
    .string()
    .refine(
      (value) => /^(?:\+?88)?01[3-9]\d{8}$/.test(value.replace(/[ ()-]/g, "")),
      "Enter a Bangladesh mobile number.",
    ),
  email: z.string().trim().pipe(z.email("Enter a valid email address.")),
  address: z.string().trim().min(1).max(50),
  city: z.string().min(1).max(50),
  area: z.string().min(1).max(50),
  postcode: z.string().max(12),
  notes: z.string().max(600),
  accepted: z
    .boolean()
    .refine(Boolean, "Please review and accept the policies."),
});
type Quote = {
  subtotal: number;
  shipping: number;
  total: number;
  signature: string;
  lines: {
    perfumeId: number;
    name: string;
    sizeMl: number;
    quantity: number;
    unitPrice: number;
  }[];
};
export function CheckoutForm({ settings }: { settings: Settings }) {
  const { session } = useCart();
  return <CheckoutDetails key={session.id ?? "guest"} settings={settings} />;
}
function CheckoutDetails({ settings }: { settings: Settings }) {
  const c = useCart(),
    router = useRouter();
  const [zone, setZone] = useState(settings.zones[0]?.id ?? 0),
    [method, setMethod] = useState(settings.zones[0]?.cod ? "COD" : "Online"),
    [priced, price] = useState<{ quote: Quote; key: string } | null>(null),
    [error, fail] = useState(""),
    [revision, retry] = useState(0);
  const basket = useMemo(
    () => ({
      items: c.lines.map(({ perfumeId, sizeMl, quantity }) => ({
        perfumeId,
        sizeMl,
        quantity,
      })),
      zoneId: zone,
      method,
    }),
    [c.lines, zone, method],
  );
  const clearCart = c.clear;
  const attemptKey =
    c.session.id === null ? "fs.attempt" : "fs.attempt." + c.session.id;
  useEffect(() => {
    const pending = sessionStorage.getItem(attemptKey);
    if (!pending) return;
    let live = true;
    const key = JSON.parse(pending).key;
    api<{ reference: string; token: string | null }>("/orders/recover", {
      method: "POST",
      body: JSON.stringify({ key }),
    })
      .then((r) => {
        if (!live) return;
        if (r.token) sessionStorage.setItem("fs.order." + r.reference, r.token);
        sessionStorage.removeItem(attemptKey);
        clearCart();
        router.replace("/orders/" + r.reference);
      })
      .catch((e) => {
        if (live && !(e instanceof ApiError && e.status === 404))
          fail(message(e));
      });
    return () => {
      live = false;
    };
  }, [clearCart, router, attemptKey]);
  const {
    register,
    setValue,
    getFieldState,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { email: c.session.email || "" },
  });
  useEffect(() => {
    if (c.session.id === null) return;
    let live = true;
    api<DeliveryProfile>("/account/profile")
      .then((profile) => {
        if (live) {
          const defaults = profile;
          for (const key of Object.keys(defaults) as (keyof typeof defaults)[])
            if (defaults[key] && !getFieldState(key).isDirty)
              setValue(key, defaults[key]);
        }
      })
      .catch((e) => {
        if (live) fail(message(e));
      });
    return () => {
      live = false;
    };
  }, [c.session.id, c.session.email, setValue, getFieldState]);
  const key = JSON.stringify([basket, revision]);
  const quote = priced?.key === key ? priced.quote : null;
  useEffect(() => {
    let live = true;
    if (basket.items.length)
      api<Quote>("/checkout/quote", {
        method: "POST",
        body: JSON.stringify(basket),
      })
        .then((q) => {
          if (live) {
            price({ quote: q, key });
            fail("");
          }
        })
        .catch((e) => {
          if (live) fail(message(e));
        });
    return () => {
      live = false;
    };
  }, [basket, key]);
  if (!c.ready) return <p>Loading bag…</p>;
  if (!c.lines.length)
    return (
      <p>
        Your bag is empty. <Link href="/fragrances">Shop fragrances</Link>
      </p>
    );
  return (
    <form
      className="columns"
      onSubmit={handleSubmit(async (values) => {
        if (!quote) return;
        try {
          if (method === "Online" && !values.postcode)
            throw new Error("Online payment requires a postal code.");
          const { accepted, ...customer } = values;
          const payload = {
            basket,
            customer,
            accepted,
            quote: quote.signature,
            ...(c.session.id !== null
              ? { accountId: c.session.id, cartVersion: c.version }
              : {}),
          };
          const digest = Array.from(
            new Uint8Array(
              await crypto.subtle.digest(
                "SHA-256",
                new TextEncoder().encode(JSON.stringify(payload)),
              ),
            ),
          ).join("-");
          let attempt = JSON.parse(
            sessionStorage.getItem(attemptKey) || "null",
          );
          if (attempt?.digest !== digest) {
            attempt = { digest, key: crypto.randomUUID() };
            sessionStorage.setItem(attemptKey, JSON.stringify(attempt));
          }
          const r = await api<{ reference: string; token: string | null }>(
            "/orders",
            {
              method: "POST",
              body: JSON.stringify({ ...payload, requestKey: attempt.key }),
            },
          );
          if (r.token)
            sessionStorage.setItem("fs.order." + r.reference, r.token);
          sessionStorage.setItem("fs.last", r.reference);
          c.clear();
          sessionStorage.removeItem(attemptKey);
          router.push("/orders/" + r.reference);
        } catch (e) {
          fail(message(e));
        }
      })}
    >
      <div className="panel form">
        <h2>Delivery details</h2>
        {c.session.id === null ? (
          <p>
            Checking out as a guest.{" "}
            <Link href="/account?next=/checkout">
              Sign in or create an account
            </Link>{" "}
            to track this order across devices.
          </p>
        ) : (
          <p>
            Ordering as {c.session.email}. We’ll save your delivery details for
            your next order.
          </p>
        )}
        {(
          [
            ["name", "Full name", "name"],
            ["phone", "Mobile number", "tel"],
            ["email", "Email", "email"],
            ["address", "Street address", "street-address"],
            ["area", "Area", "address-level3"],
            ["city", "City or district", "address-level2"],
            ["postcode", "Postal code", "postal-code"],
            ["notes", "Delivery notes", "off"],
          ] as const
        ).map(([k, l, a]) => (
          <label key={k}>
            {l}
            {["notes", "postcode"].includes(k) &&
            !(method === "Online" && k !== "notes")
              ? " (optional)"
              : ""}
            <input
              {...register(k)}
              required={k === "email"}
              autoComplete={a}
              type={k === "email" ? "email" : k === "phone" ? "tel" : "text"}
              aria-invalid={!!errors[k]}
            />
            {errors[k] && <small className="error">{errors[k]?.message}</small>}
          </label>
        ))}
        <label>
          Delivery region
          <select
            value={zone}
            onChange={(e) => {
              const z = +e.target.value;
              setZone(z);
              if (!settings.zones.find((x) => x.id === z)?.cod)
                setMethod("Online");
            }}
          >
            {settings.zones.map((z) => (
              <option value={z.id} key={z.id}>
                {z.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Payment method
          <select value={method} onChange={(e) => setMethod(e.target.value)}>
            {settings.zones.find((z) => z.id === zone)?.cod && (
              <option value="COD">Cash on delivery</option>
            )}
            {settings.online && (
              <option value="Online">Online via SSLCommerz</option>
            )}
          </select>
        </label>
      </div>
      <section className="panel">
        <h2>Review your order</h2>
        {quote ? (
          <>
            {quote.lines.map((l) => (
              <p key={l.perfumeId + "-" + l.sizeMl}>
                {l.name} · {l.sizeMl} ml × {l.quantity}{" "}
                <b>{money(l.unitPrice * l.quantity)}</b>
                {c.lines.some(
                  (x) =>
                    x.perfumeId === l.perfumeId &&
                    x.sizeMl === l.sizeMl &&
                    x.price !== l.unitPrice,
                ) && <small> · Updated price</small>}
              </p>
            ))}
            <dl>
              {[
                ["Subtotal", quote.subtotal],
                ["Delivery", quote.shipping],
                ["Total", quote.total],
              ].map(([k, v]) => (
                <div key={k}>
                  <dt>{k}</dt>
                  <dd>{money(Number(v))}</dd>
                </div>
              ))}
            </dl>
          </>
        ) : (
          <p>Waiting for a current quote.</p>
        )}
        <Link href="/cart">Edit bag</Link>
        <p>{settings.store.shippingPolicy}</p>
        <p>{settings.store.returnPolicy}</p>
        <label>
          <input type="checkbox" {...register("accepted")} /> I accept the
          delivery and return policies.
        </label>
        {errors.accepted && (
          <small className="error">{errors.accepted.message}</small>
        )}
        {error && (
          <p role="alert" className="error">
            {error}{" "}
            <button
              type="button"
              onClick={async () => {
                await c.refresh();
                retry((n) => n + 1);
              }}
            >
              Refresh quote
            </button>
          </p>
        )}
        <button className="primary" disabled={!quote || isSubmitting}>
          {isSubmitting ? "Submitting…" : "Confirm order"}
        </button>
      </section>
    </form>
  );
}
