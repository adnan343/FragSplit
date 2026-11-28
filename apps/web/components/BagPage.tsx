"use client";
import Link from "next/link";
import { useCart, CartList } from "./Cart";
import { money } from "@/lib/shop";
export function BagPage({ rates }: { rates: number[] }) {
  const cart = useCart();
  const subtotal = cart.lines.reduce((n, l) => n + l.quantity * l.price, 0);
  const range = (base: number) => {
    const low = Math.min(...rates) + base,
      high = Math.max(...rates) + base;
    return low === high ? money(low) : money(low) + "–" + money(high);
  };
  return (
    <>
      {(cart.ready || cart.lines.length > 0) && <CartList />}
      {cart.lines.length > 0 && (
        <>
          {rates.length > 0 && (
            <dl>
              <div>
                <dt>Estimated delivery</dt>
                <dd>{range(0)}</dd>
              </div>
              <div>
                <dt>Estimated total</dt>
                <dd>{range(subtotal)}</dd>
              </div>
            </dl>
          )}
          <p>Choose your delivery region at checkout to confirm the total.</p>
        </>
      )}
      <div className="bag-page-actions">
        {cart.lines.length > 0 && (
          <Link className="button primary" href="/checkout">
            Continue to checkout
          </Link>
        )}
        <Link className="button outline" href="/fragrances">
          Continue shopping
        </Link>
      </div>
    </>
  );
}
