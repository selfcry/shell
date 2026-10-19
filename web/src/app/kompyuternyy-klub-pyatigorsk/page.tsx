import type { Metadata } from "next"
import Link from "next/link"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"
import { SITE_NAME, SITE_URL } from "@/lib/site"

const path = "/kompyuternyy-klub-pyatigorsk"
const title = "Компьютерный клуб в Пятигорске — Мираж PRO и Мираж"
const description =
  "Ищете компьютерный клуб в Пятигорске? Сеть Мираж: 2 локации, 79 игровых ПК, 7 PS5, круглосуточно. Адреса, цены, как добраться."

export const metadata: Metadata = {
  title: { absolute: `${title} | ${SITE_NAME}` },
  description,
  alternates: { canonical: `${SITE_URL}${path}` },
  openGraph: {
    title,
    description,
    url: `${SITE_URL}${path}`,
    type: "article",
  },
}

export default function ComputerClubLandingPage() {
  return (
    <>
      <ParticleBackground />
      <article className="relative z-10 mx-auto max-w-3xl px-3 pb-16 pt-24 sm:px-4 sm:pb-20 sm:pt-28">
        <nav className="mb-6 text-xs text-muted sm:text-sm">
          <Link href="/" className="hover:text-white">
            Главная
          </Link>
          <span className="mx-2">/</span>
          <span className="text-white/70">Компьютерный клуб Пятигорск</span>
        </nav>

        <h1 className="mb-5 text-3xl font-black sm:text-5xl">
          Компьютерный клуб в{" "}
          <span className="gradient-text">Пятигорске</span>
        </h1>
        <p className="mb-8 text-base leading-relaxed text-muted sm:text-lg">
          Сеть Мираж PRO — это два киберклуба в Пятигорске: флагман на Панагюриште и
          уютный клуб в центре на Октябрьской. Игровые ПК, PlayStation 5, ночные тарифы
          и работа 24/7.
        </p>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-white/80 sm:text-base">
          <h2 className="text-2xl font-bold text-white">Почему выбирают Мираж</h2>
          <p>
            В Пятигорске много запросов на «компьютерный клуб», «киберклуб» и «игровой
            клуб» рядом с домом или учёбой. Мы закрываем оба сценария: мощный зал с
            топовыми мониторами и камерный клуб ближе к центру города.
          </p>
          <ul className="list-disc space-y-2 pl-5 text-muted">
            <li>79 игровых ПК на двух локациях</li>
            <li>7 консолей PlayStation 5</li>
            <li>Интернет свыше 1 Гбит/с</li>
            <li>Круглосуточный режим без выходных</li>
            <li>Прозрачные цены и акции за отзывы</li>
          </ul>
        </section>

        <section className="mb-10 grid gap-4 sm:grid-cols-2">
          {clubs.map((club) => (
            <div key={club.id} className="glass rounded-3xl p-6">
              <h2 className="mb-2 text-xl font-bold">{club.name}</h2>
              <p className="mb-3 text-sm text-muted">{club.address}</p>
              <p className="mb-4 text-sm leading-relaxed text-white/75">
                {club.totalPC} ПК · {club.totalPS} PS5 · ★ {club.rating.value.toFixed(1)} (
                {club.rating.count} оценок)
              </p>
              <Link
                href={`/clubs/${club.id}`}
                className="inline-flex rounded-xl gradient-bg px-4 py-2.5 text-sm font-bold text-black"
              >
                Открыть клуб
              </Link>
            </div>
          ))}
        </section>

        <section className="mb-10 space-y-3 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Цены и бронирование</h2>
          <p>
            Почасовые пакеты, ночные тарифы и сравнение зон — на странице{" "}
            <Link href="/prices" className="text-white underline-offset-2 hover:underline">
              цен компьютерных клубов в Пятигорске
            </Link>
            . Забронировать место можно по телефону клуба или в{" "}
            <a
              href="https://t.me/mirage_cyber26"
              target="_blank"
              rel="noopener noreferrer"
              className="text-white underline-offset-2 hover:underline"
            >
              Telegram
            </a>
            .
          </p>
          <p>
            Не уверены, какой клуб выбрать? Смотрите{" "}
            <Link
              href="/mirage-vs-mirage-pro"
              className="text-white underline-offset-2 hover:underline"
            >
              сравнение Мираж и Мираж PRO
            </Link>{" "}
            или раздел{" "}
            <Link href="/faq" className="text-white underline-offset-2 hover:underline">
              вопросов
            </Link>
            .
          </p>
        </section>

        <section className="glass rounded-3xl p-6 sm:p-8">
          <h2 className="mb-4 text-xl font-bold">Адреса компьютерных клубов</h2>
          <ul className="space-y-3 text-sm text-muted sm:text-base">
            {clubs.map((club) => (
              <li key={club.id}>
                <strong className="text-white">{club.fullName}</strong>
                <br />
                {club.address}, {club.postalCode}
                <br />
                <a href={`tel:+${club.phone.replace(/\D/g, "")}`} className="gradient-text">
                  {club.phone}
                </a>
              </li>
            ))}
          </ul>
        </section>
      </article>
    </>
  )
}
