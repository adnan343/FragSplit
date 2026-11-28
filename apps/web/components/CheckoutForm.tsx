"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
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
type Receipt = { reference: string; token: string | null };

async function recoverAttempt(key: string) {
  try {
    return await api<Receipt>("/orders/recover", {
      method: "POST",
      body: JSON.stringify({ key }),
    });
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return null;
    throw error;
  }
}

export function CheckoutForm({ settings }: { settings: Settings }) {
  const { session } = useCart();
  return <CheckoutDetails key={session.id ?? "guest"} settings={settings} />;
}
function CheckoutDetails({ settings }: { settings: Settings }) {
  const c = useCart(),
    router = useRouter();
  const zones = settings.zones.filter((zone) => zone.cod);
  const [zone, setZone] = useState(zones[0]?.id ?? 0),
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
      method: "COD",
    }),
    [c.lines, zone],
  );
  const clearCart = c.clear;
  const attemptKey =
    c.session.id === null ? "fs.attempt" : "fs.attempt." + c.session.id;
  const savedKey = useCallback((): string | undefined => {
    const saved = sessionStorage.getItem(attemptKey);
    return saved ? JSON.parse(saved).key : undefined;
  }, [attemptKey]);
  const complete = useCallback(
    (receipt: Receipt, key: string) => {
      if (savedKey() !== key) return;
      if (receipt.token)
        sessionStorage.setItem("fs.order." + receipt.reference, receipt.token);
      sessionStorage.removeItem(attemptKey);
      clearCart();
      router.replace("/orders/" + receipt.reference);
    },
    [attemptKey, clearCart, router, savedKey],
  );
  useEffect(() => {
    const key = savedKey();
    if (!key) return;
    let live = true;
    recoverAttempt(key)
      .then((receipt) => {
        if (live && receipt) complete(receipt, key);
      })
      .catch((e) => {
        if (live) fail(message(e));
      });
    return () => {
      live = false;
    };
  }, [complete, savedKey]);
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
    if (basket.items.length && basket.zoneId)
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
  if (!zones.length) return <p>Delivery is currently unavailable.</p>;
  return (
    <form
      className="columns checkout-form"
      onSubmit={handleSubmit(async (values) => {
        if (!quote) return;
        const pending = savedKey();
        const requestKey = pending ?? crypto.randomUUID();
        sessionStorage.setItem(attemptKey, JSON.stringify({ key: requestKey }));
        try {
          if (pending) {
            const receipt = await recoverAttempt(pending);
            if (receipt) return complete(receipt, pending);
          }
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
          try {
            const receipt = await api<Receipt>("/orders", {
              method: "POST",
              body: JSON.stringify({ ...payload, requestKey }),
            });
            complete(receipt, requestKey);
          } catch (e) {
            // The first request can commit after recovery checked for it.
            if (pending && e instanceof ApiError && e.status === 409) {
              const receipt = await recoverAttempt(requestKey);
              if (receipt) return complete(receipt, requestKey);
            }
            throw e;
          }
        } catch (e) {
          fail(
            e instanceof TypeError
              ? "The connection dropped. Confirm again to check your previous submission."
              : message(e),
          );
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
            {["notes", "postcode"].includes(k) ? " (optional)" : ""}
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
          <select value={zone} onChange={(e) => setZone(+e.target.value)}>
            {zones.map((z) => (
              <option value={z.id} key={z.id}>
                {z.name}
              </option>
            ))}
          </select>
        </label>
        <p className="muted">
          Enter Dhaka city for delivery within the city. For delivery elsewhere,
          enter the destination city or district and choose the matching region.
        </p>
        <p>Payment by cash on delivery.</p>
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
                try {
                  const key = savedKey();
                  if (key) {
                    const receipt = await recoverAttempt(key);
                    if (receipt) return complete(receipt, key);
                  }
                  await c.refresh();
                  retry((n) => n + 1);
                } catch (e) {
                  fail(message(e));
                }
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
