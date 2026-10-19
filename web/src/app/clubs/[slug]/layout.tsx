import type { Metadata } from "next"
import { notFound } from "next/navigation"
import { clubs } from "@/data/clubs"
import { SITE_NAME, SITE_URL } from "@/lib/site"

export const dynamicParams = false

export function generateStaticParams() {
  return clubs.map((club) => ({ slug: club.id }))
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug: string }>
}): Promise<Metadata> {
  const { slug } = await params
  const club = clubs.find((item) => item.id === slug)
  if (!club) notFound()

  const title = club.seoH1
  const canonical = `${SITE_URL}/clubs/${club.id}`

  return {
    title: { absolute: `${title} | ${SITE_NAME}` },
    description: club.description,
    alternates: { canonical },
    openGraph: {
      title: `${title} | ${SITE_NAME}`,
      description: club.description,
      url: canonical,
      type: "website",
      images: [
        {
          url: club.gallery[0] ?? "/og-image.jpg",
          width: 1200,
          height: 630,
          alt: title,
        },
      ],
    },
  }
}

export default async function ClubLayout({
  children,
  params,
}: {
  children: React.ReactNode
  params: Promise<{ slug: string }>
}) {
  const { slug } = await params
  const club = clubs.find((item) => item.id === slug)
  if (!club) notFound()

  const breadcrumbSchema = {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: [
      {
        "@type": "ListItem",
        position: 1,
        name: "Главная",
        item: SITE_URL,
      },
      {
        "@type": "ListItem",
        position: 2,
        name: club.name,
        item: `${SITE_URL}/clubs/${club.id}`,
      },
    ],
  }

  return (
    <>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(breadcrumbSchema) }}
      />
      {children}
    </>
  )
}
