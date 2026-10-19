import type { NextConfig } from "next";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);

const nextConfig: NextConfig = {
  // самодостаточная сборка для VDS: node .next/standalone/server.js
  output: "standalone",
  // локально кабинет ходит к серверу клуба через прокси (CLUBSHELL_API); на проде /api/guest отдаёт nginx
  async rewrites() {
    return process.env.CLUBSHELL_API
      ? [{ source: "/api/guest/:path*", destination: `${process.env.CLUBSHELL_API ?? "https://cryvx.ru"}/api/guest/:path*` }]
      : []
  },
  images: {
    unoptimized: true,
    formats: ["image/avif", "image/webp"],
    remotePatterns: [
      {
        protocol: "https",
        hostname: "avatars.mds.yandex.net",
      },
    ],
  },
  compress: true,
  webpack: (config, { dev, isServer }) => {
    // Opt-in only: sealing every JS/CSS chunk costs 40-65+ Lighthouse performance
    // points (measured), so it must never be the silent default for a public site.
    if (!dev && !isServer && process.env.MIRAGE_OBF === "1") {
      if (process.env.MIRAGE_OBF_MAX === "1") {
        const { MirageObfMaxWebpackPlugin } = require(
          "./scripts/mirage-obf/webpack-plugin-max.cjs"
        );
        config.plugins.push(new MirageObfMaxWebpackPlugin());
      } else {
        const { MirageObfWebpackPlugin } = require("./scripts/mirage-obf/webpack-plugin.cjs");
        config.plugins.push(new MirageObfWebpackPlugin());
      }
    }
    return config;
  },
};

export default nextConfig;
