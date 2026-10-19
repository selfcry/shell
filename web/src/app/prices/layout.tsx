import type { Metadata } from "next"
import { clubs } from "@/data/clubs"
import { SITE_URL } from "@/lib/site"

export const metadata: Metadata = {
  title: "Цены компьютерных клубов в Пятигорске",
  description:
    "Цены компьютерных клубов Мираж и Мираж PRO в Пятигорске: почасовые пакеты, ночные тарифы и характеристики игровых ПК.",
  alternates: { canonical: `${SITE_URL}/prices` },
  openGraph: {
    title: "Цены компьютерных клубов в Пятигорске | Мираж PRO",
    description:
      "Сравните тарифы, игровые зоны и характеристики ПК двух клубов в Пятигорске.",
    url: `${SITE_URL}/prices`,
  },
}

const offerSchema = {
  "@context": "https://schema.org",
  "@type": "ItemList",
  name: "Тарифы компьютерных клубов Мираж в Пятигорске",
  itemListElement: clubs.flatMap((club, clubIndex) =>
    club.prices.map((price, priceIndex) => ({
      "@type": "ListItem",
      position: clubIndex * 10 + priceIndex + 1,
      item: {
        "@type": "Offer",
        name: `${club.name} — ${price.name}`,
        url: `${SITE_URL}/prices#prices-${club.id}`,
        priceCurrency: "RUB",
        price: String(price.prices["1 час"]),
        availability: "https://schema.org/InStock",
        description: `1 час от ${price.prices["1 час"]}₽, 3 часа ${price.prices["3 часа"]}₽, ночь ${price.prices["Ночь (22:00-8:00)"]}₽`,
        seller: {
          "@type": "InternetCafe",
          "@id": `${SITE_URL}/clubs/${club.id}#business`,
          name: club.fullName,
        },
      },
    }))
  ),
}

export default function PricesLayout({
  children,
}: {
  children: React.ReactNode
}) {
  return (
    <>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(offerSchema) }}
      />
      {children}
    </>
  )
}
