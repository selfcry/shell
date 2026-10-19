import type { Metadata } from "next"
import Link from "next/link"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"
import { SITE_NAME, SITE_URL } from "@/lib/site"

const path = "/ps5-pyatigorsk"
const title = "PS5 в Пятигорске — PlayStation 5 в компьютерных клубах Мираж"
const description =
  "Играть на PlayStation 5 в Пятигорске: 7 консолей PS5 в клубах Мираж PRO и Мираж. Адреса, как забронировать, цены и режим 24/7."

export const metadata: Metadata = {
  title: { absolute: `${title} | ${SITE_NAME}` },
  description,
  alternates: { canonical: `${SITE_URL}${path}` },
  openGraph: { title, description, url: `${SITE_URL}${path}`, type: "article" },
}

const totalPS = clubs.reduce((sum, club) => sum + club.totalPS, 0)

export default function Ps5LandingPage() {
  return (
    <>
      <ParticleBackground />
      <article className="relative z-10 mx-auto max-w-3xl px-3 pb-16 pt-24 sm:px-4 sm:pb-20 sm:pt-28">
        <nav className="mb-6 text-xs text-muted sm:text-sm">
          <Link href="/" className="hover:text-white">
            Главная
          </Link>
          <span className="mx-2">/</span>
          <span className="text-white/70">PS5 Пятигорск</span>
        </nav>

        <h1 className="mb-5 text-3xl font-black sm:text-5xl">
          PlayStation 5 в <span className="gradient-text">Пятигорске</span>
        </h1>
        <p className="mb-8 text-base leading-relaxed text-muted sm:text-lg">
          В сети компьютерных клубов Мираж доступны {totalPS} консолей PS5. Можно прийти
          компанией, взять консоль и играть круглосуточно — без покупки приставки дома.
        </p>

        <section className="mb-10 grid gap-4 sm:grid-cols-2">
          {clubs.map((club) => (
            <div key={club.id} className="glass rounded-3xl p-6">
              <h2 className="mb-2 text-xl font-bold">{club.name}</h2>
              <p className="mb-2 text-sm text-muted">{club.address}</p>
              <p className="mb-4 text-sm text-white/80">PS5 ×{club.totalPS}</p>
              <a
                href={`tel:+${club.phone.replace(/\D/g, "")}`}
                className="block text-sm font-semibold gradient-text"
              >
                {club.phone}
              </a>
              <Link
                href={`/clubs/${club.id}`}
                className="mt-4 inline-flex rounded-xl border border-white/10 px-4 py-2 text-sm text-white hover:bg-white/5"
              >
                Страница клуба
              </Link>
            </div>
          ))}
        </section>

        <section className="mb-8 space-y-3 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Как поиграть на PS5</h2>
          <p>
            Позвоните в нужный клуб или напишите в{" "}
            <a
              href="https://t.me/mirage_cyber26"
              target="_blank"
              rel="noopener noreferrer"
              className="text-white underline-offset-2 hover:underline"
            >
              Telegram
            </a>
            , чтобы узнать свободные консоли. Актуальные пакеты — на странице{" "}
            <Link href="/prices" className="text-white underline-offset-2 hover:underline">
              цен
            </Link>
            . Ответы на частые вопросы — в{" "}
            <Link href="/faq" className="text-white underline-offset-2 hover:underline">
              FAQ
            </Link>
            .
          </p>
          <p>
            Ищете именно игровой ПК, а не консоль? Смотрите подборку{" "}
            <Link
              href="/kompyuternyy-klub-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              компьютерных клубов в Пятигорске
            </Link>
            .
          </p>
        </section>
      </article>
    </>
  )
}
