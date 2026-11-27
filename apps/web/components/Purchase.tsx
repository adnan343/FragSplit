"use client";
import Image from "next/image";
import Link from "next/link";
import { useId, useState } from "react";
import { Product, money } from "@/lib/shop";
import { useCart } from "./Cart";
import { Button } from "./ui/button";
export function Gallery({ p }: { p: Product }) {
  const [index, choose] = useState(0);
  const pic = p.images[index];
  return (
    <div>
      {pic ? (
        <>
          <div className="detail-art">
            <Image
              src={pic.path}
              alt={pic.alt}
              fill
              sizes="(max-width:720px) 95vw, 48vw"
              priority
            />
          </div>
          <div className="thumbs">
            {p.images.length > 1 &&
              p.images.map((i, n) => (
                <button
                  aria-pressed={index === n}
                  onClick={() => choose(n)}
                  key={i.id}
                >
                  <Image src={i.path} alt={i.alt} width={60} height={60} />
                </button>
              ))}
          </div>
        </>
      ) : (
        <p>Image coming soon</p>
      )}
    </div>
  );
}
export function Purchase({ p }: { p: Product }) {
  const cart = useCart();
  const [size, select] = useState(
      p.variants.find((v) => v.enabled && v.sizeMl <= p.availableMl)?.sizeMl,
    ),
    [quantityText, setQuantityText] = useState("1");
  const qty = Number(quantityText);
  const quantityStatus = useId();
  const validQuantity = Number.isInteger(qty) && qty >= 1 && qty <= 99;
  const v = p.variants.find((v) => v.sizeMl === size);
  const remaining =
    p.availableMl -
    cart.lines
      .filter((l) => l.perfumeId === p.id)
      .reduce((sum, l) => sum + l.quantity * l.sizeMl, 0);
  const can = !!v && validQuantity && qty * v.sizeMl <= remaining;
  return (
    <section className="purchase">
      <fieldset>
        <legend>Decant size</legend>
        <div className="size-row">
          {p.variants.map((v) => (
            <button
              disabled={!v.enabled || v.sizeMl > p.availableMl}
              className={size === v.sizeMl ? "selected" : ""}
              aria-pressed={size === v.sizeMl}
              onClick={() => select(v.sizeMl)}
              key={v.sizeMl}
            >
              {v.sizeMl} ml
            </button>
          ))}
        </div>
      </fieldset>
      <div className="purchase-order">
        <label>
          Quantity{" "}
          <input
            type="number"
            min={1}
            max={99}
            value={quantityText}
            aria-invalid={!validQuantity}
            aria-describedby={quantityStatus}
            onChange={(e) => setQuantityText(e.target.value)}
            onBlur={(e) => {
              if (Number.isInteger(qty) && qty > 0) {
                e.currentTarget.value = String(qty);
                setQuantityText(String(qty));
              }
            }}
          />
        </label>
        <Button
          disabled={!can || !cart.ready}
          onClick={() => {
            if (v)
              cart.add({
                perfumeId: p.id,
                name: p.name,
                slug: p.slug,
                sizeMl: v.sizeMl,
                price: v.price,
                quantity: qty,
              });
          }}
        >
          Add to bag {v && validQuantity ? " · " + money(v.price * qty) : ""}
        </Button>
      </div>
      <small>
        <span id={quantityStatus} role="status">
          {!validQuantity
            ? "Enter a whole quantity between 1 and 99."
            : can
              ? "Supplied in a decant atomizer."
              : "Insufficient available stock for this selection and your bag."}
        </span>{" "}
        <Link href="/guide#sizes">Size guide</Link>
      </small>
    </section>
  );
}
