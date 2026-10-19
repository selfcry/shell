import type { MetadataRoute } from "next"

/** cryvx.ru — копия mirage-ptg.ru с личным кабинетом: поисковики индексируют только основной сайт. */
export default function robots(): MetadataRoute.Robots {
  return { rules: { userAgent: "*", disallow: "/" } }
}
