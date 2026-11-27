import { money } from "@/lib/shop";
import { Order } from "@/lib/orders";
export function OrderSummary({ o }: { o: Order }) {
  return (
    <section className="panel">
      <h2>Order {o.reference}</h2>
      <p>
        {o.fulfillment} ·{" "}
        {o.method === "COD" ? "Cash on delivery" : "Online payment"} ·{" "}
        {o.payment}
      </p>
      {o.method === "COD" && o.fulfillment === "Pending" && o.expiresAt && (
        <p>
          Your stock hold expires{" "}
          <time dateTime={o.expiresAt}>
            {new Date(o.expiresAt).toLocaleString("en-BD", {
              timeZone: "Asia/Dhaka",
              dateStyle: "medium",
              timeStyle: "short",
            })}
          </time>{" "}
          (Dhaka time) unless the store confirms your order.
        </p>
      )}
      {o.needsReview && (
        <p>
          Payment or fulfillment is under review. Contact the store with your
          reference.
        </p>
      )}
      {o.lines.map((l) => (
        <div className="line" key={l.id}>
          <span>
            {l.name} · {l.sizeMl} ml × {l.quantity}
          </span>
          <b>{money(l.quantity * l.unitPrice)}</b>
        </div>
      ))}
      <dl>
        {[
          ["Subtotal", money(o.subtotal)],
          ["Delivery", money(o.shipping)],
          ["Total", money(o.total)],
        ].map(([k, v]) => (
          <div key={k}>
            <dt>{k}</dt>
            <dd>{v}</dd>
          </div>
        ))}
      </dl>
      <h3>Delivery details</h3>
      <p>
        {o.name} · {o.phone}
        <br />
        {[o.address, o.area, o.city, o.postcode, o.region]
          .filter(Boolean)
          .join(", ")}
      </p>
      {o.notes && <p>{o.notes}</p>}
      {o.tracking && <p>Tracking reference · {o.tracking}</p>}
    </section>
  );
}
