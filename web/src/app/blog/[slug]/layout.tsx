import type { Metadata } from "next"
import { notFound } from "next/navigation"
import { articles } from "@/data/articles"
import { SITE_NAME, SITE_URL } from "@/lib/site"

export const dynamicParams = false

export function generateStaticParams() {
  return articles.map((article) => ({ slug: article.slug }))
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug: string }>
}): Promise<Metadata> {
  const { slug } = await params
  const article = articles.find((item) => item.slug === slug)
  if (!article) notFound()

  const canonical = `${SITE_URL}/blog/${article.slug}`

  return {
    title: { absolute: `${article.seoTitle} | ${SITE_NAME}` },
    description: article.description,
    alternates: { canonical },
    openGraph: {
      title: article.seoTitle,
      description: article.description,
      url: canonical,
      type: "article",
      publishedTime: article.publishedAt,
      modifiedTime: article.updatedAt,
      images: [{ url: article.coverImage, width: 1200, height: 630, alt: article.coverImageAlt }],
    },
  }
}

export default async function ArticleLayout({
  children,
  params,
}: {
  children: React.ReactNode
  params: Promise<{ slug: string }>
}) {
  const { slug } = await params
  const article = articles.find((item) => item.slug === slug)
  if (!article) notFound()

  const canonical = `${SITE_URL}/blog/${article.slug}`

  const graph = [
    {
      "@type": "BreadcrumbList",
      itemListElement: [
        { "@type": "ListItem", position: 1, name: "Главная", item: SITE_URL },
        { "@type": "ListItem", position: 2, name: "Статьи", item: `${SITE_URL}/blog` },
        { "@type": "ListItem", position: 3, name: article.title, item: canonical },
      ],
    },
    {
      "@type": "BlogPosting",
      "@id": `${canonical}#article`,
      headline: article.title,
      description: article.description,
      image: `${SITE_URL}${article.coverImage}`,
      datePublished: article.publishedAt,
      dateModified: article.updatedAt,
      inLanguage: "ru-RU",
      mainEntityOfPage: canonical,
      author: { "@type": "Organization", name: SITE_NAME, url: SITE_URL },
      publisher: { "@type": "Organization", name: SITE_NAME, url: SITE_URL },
    },
    {
      "@type": "FAQPage",
      mainEntity: article.faq.map((item) => ({
        "@type": "Question",
        name: item.question,
        acceptedAnswer: { "@type": "Answer", text: item.answer },
      })),
    },
  ]

  return (
    <>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{
          __html: JSON.stringify({ "@context": "https://schema.org", "@graph": graph }),
        }}
      />
      {children}
    </>
  )
}
