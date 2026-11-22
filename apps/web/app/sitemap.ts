import { MetadataRoute } from "next";
import { api, Paged, categories } from "@/lib/shop";
export const dynamic = "force-dynamic";
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const origin = process.env.PUBLIC_ORIGIN || "http://localhost:3000";
  const urls = [
    "/",
    "/guide",
    "/fragrances",
    ...categories.map((c) => "/fragrances?category=" + encodeURIComponent(c)),
  ];
  let page = 1;
  for (;;) {
    const data = await api<Paged>(
      "/perfumes?sort=name&pageSize=60&page=" + page,
    );
    urls.push(...data.items.map((p) => "/fragrances/" + p.slug));
    if (page * 60 >= data.total) break;
    page++;
  }
  return urls.map((path) => ({ url: origin + path }));
}
