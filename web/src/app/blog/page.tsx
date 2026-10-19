import type { Metadata } from "next"
import Image from "next/image"
import Link from "next/link"
import { articles } from "@/data/articles"
import { ParticleBackground } from "@/components/ParticleBackground"
import { SITE_NAME, SITE_URL } from "@/lib/site"

const path = "/blog"
const title = "Статьи о геймерских клубах и играх в Пятигорске"
const description =
  "Гайды и разборы для геймеров: тарифы, пинг, эргономика, сохранение прогресса и другие практические темы про компьютерные клубы Пятигорска."

export const metadata: Metadata = {
  title: { absolute: `${title} | ${SITE_NAME}` },
  description,
  alternates: { canonical: `${SITE_URL}${path}` },
  openGraph: { title, description, url: `${SITE_URL}${path}`, type: "website" },
}

function formatDate(date: string) {
  return new Date(date).toLocaleDateString("ru-RU", { day: "numeric", month: "long", year: "numeric" })
}

export default function BlogIndexPage() {
  return (
    <>
      <ParticleBackground />
      <div className="relative z-10 pb-16 pt-24 sm:pb-20 sm:pt-28">
        <div className="mx-auto max-w-6xl px-3 sm:px-4">
          <nav className="mb-6 text-xs text-muted sm:text-sm">
            <Link href="/" className="hover:text-white">
              Главная
            </Link>
            <span className="mx-2">/</span>
            <span className="text-white/70">Статьи</span>
          </nav>

          <div className="mb-12 text-center">
            <h1 className="mb-4 text-4xl font-black sm:text-6xl">
              <span className="gradient-text">Статьи</span> для геймеров
            </h1>
            <p className="mx-auto max-w-2xl text-base leading-relaxed text-muted sm:text-lg">
              Практические гайды про компьютерные клубы, сети и комфортный гейминг — без воды,
              с реальными цифрами по сети Мираж в Пятигорске.
            </p>
          </div>

          <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {articles.map((article) => (
              <Link
                key={article.slug}
                href={`/blog/${article.slug}`}
                className="glass-hover group flex flex-col overflow-hidden rounded-3xl glass"
              >
                <div className="relative aspect-[16/10] w-full overflow-hidden">
                  <Image
                    src={article.coverImage}
                    alt={article.coverImageAlt}
                    fill
                    unoptimized
                    className="object-cover transition-transform duration-500 group-hover:scale-105"
                    sizes="(min-width: 1024px) 33vw, (min-width: 640px) 50vw, 100vw"
                  />
                </div>
                <div className="flex flex-1 flex-col p-5 sm:p-6">
                  <div className="mb-3 flex items-center gap-2 text-xs text-muted">
                    <time dateTime={article.publishedAt}>{formatDate(article.publishedAt)}</time>
                    <span>·</span>
                    <span>{article.readingMinutes} мин чтения</span>
                  </div>
                  <h2 className="mb-2 text-lg font-bold leading-snug sm:text-xl">{article.title}</h2>
                  <p className="line-clamp-3 text-sm leading-relaxed text-muted">
                    {article.description}
                  </p>
                </div>
              </Link>
            ))}
          </div>
        </div>
      </div>
    </>
  )
}
