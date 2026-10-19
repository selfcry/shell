import type { Metadata } from "next"
import { faqData } from "@/data/faq"
import { SITE_URL } from "@/lib/site"

export const metadata: Metadata = {
  title: "Вопросы и ответы",
  description:
    "Ответы о бронировании, оплате, ночных тарифах, PlayStation 5, клубных аккаунтах и правилах компьютерных клубов Мираж и Мираж PRO в Пятигорске.",
  alternates: { canonical: `${SITE_URL}/faq` },
  openGraph: {
    title: "Вопросы и ответы | Мираж PRO",
    description:
      "Всё о посещении компьютерных клубов Мираж и Мираж PRO в Пятигорске.",
    url: `${SITE_URL}/faq`,
  },
}

export default function FAQLayout({ children }: { children: React.ReactNode }) {
  const faqSchema = {
    "@context": "https://schema.org",
    "@type": "FAQPage",
    mainEntity: faqData.map((item) => ({
      "@type": "Question",
      name: item.question,
      acceptedAnswer: {
        "@type": "Answer",
        text: item.answer,
      },
    })),
  }

  return (
    <>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(faqSchema) }}
      />
      {children}
    </>
  )
}
