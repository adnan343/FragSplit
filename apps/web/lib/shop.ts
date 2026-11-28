export type Size = { sizeMl: number; price: number; enabled: boolean };
export type Picture = { id: number; path: string; alt: string };
export type Product = {
  id: number;
  name: string;
  slug: string;
  brand: string;
  category: string;
  description: string;
  top: string;
  heart: string;
  base: string;
  concentration: string;
  tags: string;
  seoTitle: string;
  seoDescription: string;
  active: boolean;
  featured: boolean;
  threshold: number;
  availableMl: number;
  variants: Size[];
  images: Picture[];
};
export type Paged = {
  items: Product[];
  page: number;
  pageSize: number;
  total: number;
};
export type Zone = {
  id: number;
  name: string;
  charge: number;
  cod: boolean;
  dhakaCity: boolean;
  active: boolean;
};
export type Store = {
  name: string;
  logo: string;
  contact: string;
  shippingPolicy: string;
  returnPolicy: string;
};
export type Settings = { store: Store; zones: Zone[]; online: boolean };
export const money = (n: number) =>
  "৳" + new Intl.NumberFormat("en-BD", { maximumFractionDigits: 2 }).format(n);
export function lowest(p: Product) {
  const prices = p.variants
    .filter((v) => v.enabled && v.sizeMl <= p.availableMl)
    .map((v) => v.price);
  return prices.length ? Math.min(...prices) : null;
}
export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
  ) {
    super(message);
  }
}
export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const server = typeof window === "undefined";
  const origin = server
    ? process.env.API_ORIGIN || "http://127.0.0.1:5080"
    : "";
  const res = await fetch(origin + "/api/v1" + path, {
    cache: "no-store",
    ...init,
    headers: {
      ...(init?.body && !(init.body instanceof FormData)
        ? { "Content-Type": "application/json" }
        : {}),
      "X-Shop-Request": "1",
      ...init?.headers,
      ...(server &&
      (path === "/perfumes" || path.startsWith("/perfumes?")) &&
      process.env.CATALOG_ORIGIN_SECRET
        ? { "X-VialMuse-Catalog-Origin": process.env.CATALOG_ORIGIN_SECRET }
        : {}),
    },
  });
  if (!res.ok) {
    const problem = await res.json().catch(() => ({}));
    throw new ApiError(
      problem.detail ||
        (
          {
            401: "Please sign in.",
            403: "Access denied.",
            429: "Too many requests. Try again shortly.",
          } as Record<number, string>
        )[res.status] ||
        "The request could not be completed.",
      res.status,
    );
  }
  return res.status === 204 ? (undefined as T) : res.json();
}
export const categories = ["Middle Eastern", "Designer", "Niche"];
export const message = (error: unknown) =>
  error instanceof Error
    ? error.message
    : "Something went wrong. Please try again.";
