import type { Metadata } from "next"
import Link from "next/link"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"
import { SITE_NAME, SITE_URL } from "@/lib/site"

const path = "/kontakty"
const title = "Контакты компьютерных клубов Мираж в Пятигорске"
const description =
  "Контакты Мираж PRO и Мираж в Пятигорске: адреса, телефоны, email, карты, как добраться. Бронирование ПК и PS5 24/7."

export const metadata: Metadata = {
  title: { absolute: `${title} | ${SITE_NAME}` },
  description,
  alternates: { canonical: `${SITE_URL}${path}` },
  openGraph: {
    title,
    description,
    url: `${SITE_URL}${path}`,
    type: "website",
  },
}

export default function KontaktyPage() {
  return (
    <>
      <ParticleBackground />
      <article className="relative z-10 mx-auto max-w-3xl px-3 pb-16 pt-24 sm:px-4 sm:pb-20 sm:pt-28">
        <nav className="mb-6 text-xs text-muted sm:text-sm" aria-label="Хлебные крошки">
          <Link href="/" className="hover:text-white">
            Главная
          </Link>
          <span className="mx-2">/</span>
          <span className="text-white/70">Контакты</span>
        </nav>

        <h1 className="mb-5 text-3xl font-black sm:text-5xl">
          Контакты клубов Мираж в{" "}
          <span className="gradient-text">Пятигорске</span>
        </h1>
        <p className="mb-8 text-base leading-relaxed text-muted sm:text-lg">
          На этой странице собраны адреса, телефоны, почта и ссылки на карты обоих клубов
          сети Мираж PRO. Можно позвонить, написать на email или открыть маршрут в Яндекс
          Картах. Режим работы — круглосуточно, без выходных.
        </p>

        <section className="mb-10 grid gap-4 sm:grid-cols-2">
          {clubs.map((club) => (
            <div key={club.id} className="glass rounded-3xl p-6 sm:p-7">
              <h2 className="mb-1 text-xl font-bold">{club.name}</h2>
              <p className="mb-4 text-sm text-muted">{club.fullName}</p>

              <dl className="space-y-4 text-sm sm:text-base">
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted/70">Адрес</dt>
                  <dd className="mt-1 text-white/90">
                    {club.address}, {club.postalCode}
                  </dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted/70">Телефон</dt>
                  <dd className="mt-1">
                    <a
                      href={`tel:+${club.phone.replace(/\D/g, "")}`}
                      className="gradient-text font-semibold hover:underline"
                    >
                      {club.phone}
                    </a>
                  </dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted/70">Email</dt>
                  <dd className="mt-1">
                    <a
                      href={`mailto:${club.email}`}
                      className="break-all text-white/85 hover:text-white"
                    >
                      {club.email}
                    </a>
                  </dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted/70">Режим</dt>
                  <dd className="mt-1 text-green-400">{club.workTime}</dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted/70">Рейтинг</dt>
                  <dd className="mt-1 text-white/90">
                    ★ {club.rating.value.toFixed(1)} · {club.rating.count} оценок (
                    <a
                      href={club.rating.sourceUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="underline-offset-2 hover:underline"
                    >
                      {club.rating.source}
                    </a>
                    )
                  </dd>
                </div>
              </dl>

              <div className="mt-6 flex flex-wrap gap-3">
                <a
                  href={club.mapUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex rounded-xl gradient-bg px-4 py-2.5 text-sm font-bold text-black"
                >
                  На карте
                </a>
                <Link
                  href={`/clubs/${club.id}`}
                  className="inline-flex rounded-xl border border-white/10 px-4 py-2.5 text-sm font-medium text-white hover:bg-white/5"
                >
                  Страница клуба
                </Link>
              </div>
            </div>
          ))}
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Как добраться</h2>
          {clubs.map((club) => (
            <div key={club.id}>
              <h3 className="mb-2 text-lg font-semibold text-white">{club.name}</h3>
              <p>{club.howToGet}</p>
            </div>
          ))}
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Мессенджеры и соцсети</h2>
          <p>
            Общий Telegram сети:{" "}
            <a
              href="https://t.me/mirage_cyber26"
              target="_blank"
              rel="noopener noreferrer"
              className="text-white underline-offset-2 hover:underline"
            >
              @mirage_cyber26
            </a>
            . Там удобно уточнить свободные места, ночной тариф или наличие PS5. Также мы
            есть во{" "}
            <a
              href="https://vk.com/club229615472"
              target="_blank"
              rel="noopener noreferrer"
              className="text-white underline-offset-2 hover:underline"
            >
              ВКонтакте
            </a>
            ,{" "}
            <a
              href="https://www.instagram.com/mirage_ptg/"
              target="_blank"
              rel="noopener noreferrer"
              className="text-white underline-offset-2 hover:underline"
            >
              Instagram
            </a>{" "}
            и{" "}
            <a
              href="https://www.tiktok.com/@miragepro_pyatigorsk"
              target="_blank"
              rel="noopener noreferrer"
              className="text-white underline-offset-2 hover:underline"
            >
              TikTok
            </a>
            .
          </p>
          <p>
            Чтобы забронировать ПК по шагам, откройте{" "}
            <Link
              href="/bronirovanie"
              className="text-white underline-offset-2 hover:underline"
            >
              инструкцию по бронированию
            </Link>
            . Актуальные цены — на{" "}
            <Link href="/prices" className="text-white underline-offset-2 hover:underline">
              странице тарифов
            </Link>
            , ответы на частые вопросы — в{" "}
            <Link href="/faq" className="text-white underline-offset-2 hover:underline">
              FAQ
            </Link>
            .
          </p>
        </section>

        <section className="mb-10 space-y-3 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Полезные страницы</h2>
          <ul className="list-disc space-y-2 pl-5">
            <li>
              <Link
                href="/kompyuternyy-klub-pyatigorsk"
                className="text-white underline-offset-2 hover:underline"
              >
                Компьютерный клуб в Пятигорске
              </Link>
            </li>
            <li>
              <Link
                href="/kiberklub-pyatigorsk"
                className="text-white underline-offset-2 hover:underline"
              >
                Киберклуб в Пятигорске
              </Link>
            </li>
            <li>
              <Link
                href="/igrovoy-klub-pyatigorsk"
                className="text-white underline-offset-2 hover:underline"
              >
                Игровой клуб в Пятигорске
              </Link>
            </li>
            <li>
              <Link
                href="/nochnoy-tarif-pyatigorsk"
                className="text-white underline-offset-2 hover:underline"
              >
                Ночной тариф
              </Link>
            </li>
            <li>
              <Link
                href="/ps5-pyatigorsk"
                className="text-white underline-offset-2 hover:underline"
              >
                PlayStation 5 в Пятигорске
              </Link>
            </li>
            <li>
              <Link
                href="/mirage-vs-mirage-pro"
                className="text-white underline-offset-2 hover:underline"
              >
                Сравнение Мираж и Мираж PRO
              </Link>
            </li>
          </ul>
        </section>

        <section className="glass rounded-3xl p-6 sm:p-8">
          <h2 className="mb-3 text-xl font-bold">Быстрая связь</h2>
          <p className="mb-5 text-sm text-muted sm:text-base">
            Не знаете, какой клуб ближе? Позвоните в любой — подскажем по загрузке и
            зоне. Или напишите в Telegram.
          </p>
          <div className="flex flex-wrap gap-3">
            <a
              href="https://t.me/mirage_cyber26"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex rounded-xl gradient-bg px-5 py-3 text-sm font-bold text-black"
            >
              Telegram
            </a>
            {clubs.map((club) => (
              <a
                key={club.id}
                href={`tel:+${club.phone.replace(/\D/g, "")}`}
                className="inline-flex rounded-xl border border-white/10 px-5 py-3 text-sm font-medium text-white hover:bg-white/5"
              >
                {club.name}
              </a>
            ))}
          </div>
        </section>
      </article>
    </>
  )
}
