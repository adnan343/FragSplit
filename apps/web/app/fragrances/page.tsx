import Link from "next/link";
import { api, Paged } from "@/lib/shop";
import { Card } from "@/components/Card";
import { Filter } from "@/components/Filter";
export const metadata = { title: "Shop fragrances" };
export default async function Catalog({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const raw = await searchParams,
    values = Object.fromEntries(
      Object.entries(raw).filter(
        (v): v is [string, string] => typeof v[1] === "string",
      ),
    );
  const q = new URLSearchParams(values);
  const [data, brands] = await Promise.all([
    api<Paged>("/perfumes?" + q),
    api<string[]>("/brands"),
  ]);
  const pageLink = (n: number) =>
    "/fragrances?" + new URLSearchParams({ ...values, page: String(n) });
  return (
    <main id="main" className="wrap section catalogue">
      <div className="catalogue-heading">
        <h1>{values.category || "Shop fragrances"}</h1>
        <p className="catalogue-count" role="status">
          {data.total} {data.total === 1 ? "fragrance" : "fragrances"}
        </p>
      </div>
      <Filter values={values} brands={brands} />
      <div className="grid">
        {data.items.map((p) => (
          <Card p={p} key={p.id} />
        ))}
      </div>
      {!data.items.length && (
        <p>
          No fragrances match this selection.{" "}
          <Link href="/fragrances">Clear filters</Link>
        </p>
      )}
      <nav className="pager">
        {data.page > 1 && <Link href={pageLink(data.page - 1)}>Previous</Link>}
        {data.page * data.pageSize < data.total && (
          <Link href={pageLink(data.page + 1)}>Next page</Link>
        )}
      </nav>
    </main>
  );
}
