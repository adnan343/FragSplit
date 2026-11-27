import Image from "next/image";
import { CardLink } from "./CardLink";
import { ArrowUpRight } from "lucide-react";
import { Product, lowest, money } from "@/lib/shop";

export function Card({
  p,
  compact = false,
  headingLevel = 2,
}: {
  p: Product;
  compact?: boolean;
  headingLevel?: 2 | 3;
}) {
  const Heading = headingLevel === 2 ? "h2" : "h3";
  const price = lowest(p);
  const pic = p.images[0];
  const sizes = p.variants
    .filter((v) => v.enabled && v.sizeMl <= p.availableMl)
    .map((v) => v.sizeMl)
    .sort((a, b) => a - b);
  const notes = [p.top, p.heart, p.base].filter(Boolean).join(" · ");
  return (
    <CardLink
      className={compact ? "card card-compact" : "card"}
      href={"/fragrances/" + p.slug}
    >
      <div className="card-art">
        {pic ? (
          <Image
            src={pic.path}
            alt={pic.alt}
            fill
            sizes={
              compact
                ? "(max-width:600px) 72px, 100px"
                : "(max-width:680px) 91vw, (max-width:900px) 46vw, 30vw"
            }
          />
        ) : (
          <span>Image coming soon</span>
        )}
      </div>
      <div className="card-body">
        <p className="card-meta">
          {p.brand} <span> / </span> {p.category}
        </p>
        <Heading className="card-title">{p.name}</Heading>
        {notes && <p className="card-notes">{notes}</p>}
        <div className="card-price">
          <span>
            {price === null ? "Out of stock" : "From " + money(price)}
          </span>
          {!compact && sizes.length > 0 && (
            <span
              className="card-sizes"
              aria-label={"Available sizes in milliliters: " + sizes.join(", ")}
            >
              {sizes.join(" / ")} ml
            </span>
          )}
        </div>
      </div>
      {compact && (
        <ArrowUpRight size={18} className="arrival-arrow" aria-hidden />
      )}
    </CardLink>
  );
}
