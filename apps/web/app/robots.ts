import { MetadataRoute } from "next";
export const dynamic = "force-dynamic";

export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: ["/admin", "/account", "/orders", "/checkout", "/cart", "/api"],
    },
    sitemap:
      (process.env.PUBLIC_ORIGIN || "http://localhost:3000") + "/sitemap.xml",
  };
}
