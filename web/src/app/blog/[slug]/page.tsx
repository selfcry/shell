import Image from "next/image"
import Link from "next/link"
import { notFound } from "next/navigation"
import { articles } from "@/data/articles"
import { ParticleBackground } from "@/components/ParticleBackground"
import { CTASection } from "@/components/CTASection"

function formatDate(date: string) {
  return new Date(date).toLocaleDateString("ru-RU", { day: "numeric", month: "long", year: "numeric" })
}

export default async function ArticlePage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params
  const article = articles.find((item) => item.slug === slug)
  if (!article) notFound()

  return (
    <>
      <ParticleBackground />
      <article className="relative z-10 mx-auto max-w-3xl px-3 pb-16 pt-24 sm:px-4 sm:pb-20 sm:pt-28">
        <nav className="mb-6 flex items-center gap-2 overflow-x-auto whitespace-nowrap text-xs text-muted sm:text-sm">
          <Link href="/" className="hover:text-white transition-colors">
            Главная
          </Link>
          <span>/</span>
          <Link href="/blog" className="hover:text-white transition-colors">
            Статьи
          </Link>
          <span>/</span>
          <span className="text-white/70">{article.title}</span>
        </nav>

        <div className="mb-6 flex items-center gap-3 text-xs text-muted sm:text-sm">
          <time dateTime={article.publishedAt}>{formatDate(article.publishedAt)}</time>
          <span>·</span>
          <span>{article.readingMinutes} мин чтения</span>
        </div>

        <h1 className="mb-6 text-3xl font-black leading-tight sm:text-5xl">{article.title}</h1>

        <div className="relative mb-8 aspect-[16/9] w-full overflow-hidden rounded-3xl">
          <Image
            src={article.coverImage}
            alt={article.coverImageAlt}
            fill
            unoptimized
            priority
            className="object-cover"
            sizes="(min-width: 768px) 768px, 100vw"
          />
        </div>

        <section className="mb-10 rounded-3xl p-6 glass sm:p-8">
          <h2 className="mb-4 text-lg font-bold uppercase tracking-wider text-muted">Кратко</h2>
          <ul className="space-y-2.5">
            {article.tldr.map((point) => (
              <li key={point} className="flex gap-3 text-sm leading-relaxed sm:text-base">
                <span className="mt-1 h-1.5 w-1.5 shrink-0 rounded-full gradient-bg" aria-hidden="true" />
                <span>{point}</span>
              </li>
            ))}
          </ul>
        </section>

        <div className="space-y-10">
          {article.sections.map((section) => (
            <section key={section.heading}>
              <h2 className="mb-4 text-2xl font-bold sm:text-3xl">{section.heading}</h2>
              <div className="space-y-4 text-sm leading-relaxed text-muted sm:text-base">
                {section.paragraphs.map((paragraph, i) => (
                  <p key={i}>{paragraph}</p>
                ))}
              </div>
              {section.list && (
                <ul className="mt-4 space-y-2">
                  {section.list.map((item) => (
                    <li
                      key={item}
                      className="flex gap-3 text-sm leading-relaxed text-white/80 sm:text-base"
                    >
                      <span className="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-orange-500" aria-hidden="true" />
                      <span>{item}</span>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          ))}
        </div>

        <section className="mt-12">
          <h2 className="mb-5 text-2xl font-bold sm:text-3xl">Частые вопросы</h2>
          <div className="space-y-3">
            {article.faq.map((item) => (
              <details
                key={item.question}
                className="group glass rounded-3xl transition-colors open:bg-white/[0.06]"
              >
                <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-3 p-5 font-medium focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-orange-500 sm:min-h-16 sm:gap-4">
                  <span className="text-sm sm:text-base">{item.question}</span>
                  <span
                    aria-hidden="true"
                    className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full gradient-bg text-sm font-bold text-black transition-transform group-open:rotate-45"
                  >
                    +
                  </span>
                </summary>
                <p className="px-5 pb-5 text-sm leading-relaxed text-muted sm:text-base">
                  {item.answer}
                </p>
              </details>
            ))}
          </div>
        </section>
      </article>
      <CTASection />
    </>
  )
}
