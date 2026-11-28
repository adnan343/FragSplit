import { notFound } from "next/navigation";
import { api, Product, Paged, ApiError, lowest } from "@/lib/shop";
import { Gallery, Purchase } from "@/components/Purchase";
import { Card } from "@/components/Card";
import Link from "next/link";
async function get(slug: string) {
  try {
    return await api<Product>("/perfumes/" + encodeURIComponent(slug));
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) notFound();
    throw e;
  }
}
type Props = { params: Promise<{ slug: string }> };
export async function generateMetadata({ params }: Props) {
  const p = await get((await params).slug);
  return {
    title: p.seoTitle || p.name,
    description: p.seoDescription || p.description,
    alternates: { canonical: "/fragrances/" + p.slug },
    openGraph: {
      title: p.name,
      images: p.images.slice(0, 1).map((i) => i.path),
    },
  };
}
export default async function Detail({ params }: Props) {
  const p = await get((await params).slug);
  const { items } = await api<Paged>(
    "/perfumes?category=" + encodeURIComponent(p.category) + "&pageSize=4",
  );
  const related = items.filter((item) => item.id !== p.id);
  const price = lowest(p);
  const schema = {
    "@context": "https://schema.org",
    "@type": "Product",
    name: p.name,
    brand: p.brand,
    description: p.description,
    image: p.images.map((i) => i.path),
    ...(price !== null
      ? {
          offers: {
            "@type": "Offer",
            priceCurrency: "BDT",
            price,
            availability: "https://schema.org/InStock",
          },
        }
      : {}),
  };
  return (
    <main id="main" className="wrap section">
      <nav aria-label="Breadcrumb" className="breadcrumb">
        <Link href="/fragrances">Shop fragrances</Link>
        <span aria-hidden>/</span>
        <Link href={"/fragrances?category=" + encodeURIComponent(p.category)}>
          {p.category}
        </Link>
      </nav>
      <div className="detail">
        <Gallery p={p} />
        <div className="detail-copy">
          <p className="eyebrow">{p.brand}</p>
          <h1>{p.name}</h1>
          <p className="muted">
            {p.category} · {p.concentration}
          </p>
          <p>{p.description}</p>
          <Purchase p={p} />
          <h2>Fragrance notes</h2>
          <dl className="note-list">
            {[
              ["Top", p.top],
              ["Heart", p.heart],
              ["Base", p.base],
            ].map(([k, v]) => (
              <div key={k}>
                <dt>{k}</dt>
                <dd>{v || "—"}</dd>
              </div>
            ))}
          </dl>
        </div>
      </div>
      {related.length > 0 && (
        <section>
          <h2>From the same collection</h2>
          <div className="grid">
            {related.map((item) => (
              <Card p={item} key={item.id} headingLevel={3} />
            ))}
          </div>
        </section>
      )}
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{
          __html: JSON.stringify(schema).replace(/</g, "\\u003c"),
        }}
      />
    </main>
  );
}
