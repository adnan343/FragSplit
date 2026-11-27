import Image from "next/image";
import Link from "next/link";
import { ArrowRight, ArrowUpRight } from "lucide-react";
import { api, categories, Paged } from "@/lib/shop";
import { Card } from "@/components/Card";
import { Button } from "@/components/ui/button";

const shelves = [
  { title: "Featured fragrances", query: "featured=true" },
  { title: "New arrivals", query: "sort=newest" },
  { title: "Best sellers", query: "sort=best&soldOnly=true" },
];

export default async function Home() {
  const results = await Promise.all(
    shelves.map(({ query }) => api<Paged>("/perfumes?pageSize=4&" + query)),
  );
  return (
    <main id="main">
      <section className="hero" aria-labelledby="hero-title">
        <Image
          src="/art/bergamot-light.webp"
          alt=""
          fill
          preload
          sizes="100vw"
        />
        <div className="wrap hero-copy">
          <h1 id="hero-title">
            Perfume,
            <br />
            <em>by the milliliter.</em>
          </h1>
          <p>Choose a fragrance and try it in a 3 to 30 ml decant.</p>
          <div className="hero-actions">
            <Button asChild>
              <Link href="/fragrances">
                Shop fragrances <ArrowRight size={17} aria-hidden />
              </Link>
            </Button>
            <Link className="size-link" href="/guide#sizes">
              Size guide
            </Link>
          </div>
        </div>
      </section>
      <div className="wrap">
        <nav
          className="collection-index"
          id="collections"
          aria-label="Shop by collection"
        >
          <span>Shop by collection</span>
          {categories.map((category) => (
            <Link
              href={"/fragrances?category=" + encodeURIComponent(category)}
              key={category}
            >
              {category}
              <ArrowUpRight size={18} aria-hidden />
            </Link>
          ))}
        </nav>
        {shelves.map(({ title, query }, index) => {
          const { items } = results[index];
          if (!items.length) return null;
          const compact = index === 1;
          return (
            <section className="section shelf" key={query}>
              <div className="section-head">
                <h2>{title}</h2>
                <Link
                  className="text-link"
                  href={"/fragrances?" + query}
                  aria-label={"View all " + title.toLowerCase()}
                >
                  View all <ArrowRight size={17} aria-hidden />
                </Link>
              </div>
              <div
                className={
                  compact
                    ? "arrival-list"
                    : items.length === 3
                      ? "grid grid-three"
                      : "grid"
                }
              >
                {items.map((p) => (
                  <Card key={p.id} p={p} compact={compact} headingLevel={3} />
                ))}
              </div>
            </section>
          );
        })}
        <section className="guide-strip">
          <Image
            src="/art/decant-study.webp"
            alt="Illustrated study of two travel atomizers"
            width={640}
            height={427}
            sizes="(max-width:600px) 90vw, 30vw"
          />
          <div>
            <h2>Choosing a decant size</h2>
            <p>
              Start with 3 or 5 ml to try a scent on your skin. Explore larger
              sizes, atomizers and delivery in the guide.
            </p>
            <Link className="text-link" href="/guide">
              Read the decant guide <ArrowRight size={17} aria-hidden />
            </Link>
          </div>
        </section>
      </div>
    </main>
  );
}
