const config = {
  output: "standalone",
  experimental: { useTypeScriptCli: false },
  poweredByHeader: false,
  async rewrites() {
    // The image optimizer resolves uploaded media inside Next.js, outside ingress.
    const source =
      process.env.API_BEHIND_INGRESS === "true"
        ? "/api/v1/media/:key"
        : "/api/:path*";
    return [
      {
        source,
        destination:
          (process.env.API_ORIGIN || "http://127.0.0.1:5080") + source,
      },
    ];
  },
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "Referrer-Policy", value: "no-referrer" },
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "X-Frame-Options", value: "DENY" },
          {
            key: "Content-Security-Policy",
            value: "object-src 'none'; base-uri 'self'; frame-ancestors 'none'",
          },
        ],
      },
    ];
  },
};

export default config;
