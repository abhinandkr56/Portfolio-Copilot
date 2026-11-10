import type { NextConfig } from "next";

// Server-only: set by the Aspire AppHost to the Api's http endpoint. Never expose as NEXT_PUBLIC_*.
const apiUrl = process.env.API_URL ?? "http://127.0.0.1:5080";

const nextConfig: NextConfig = {
  cacheComponents: true,
  partialPrefetching: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
  // Client components call same-origin /api/*; Next proxies to the Api, so the Api needs no CORS.
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiUrl}/api/:path*` }];
  },
};

export default nextConfig;
