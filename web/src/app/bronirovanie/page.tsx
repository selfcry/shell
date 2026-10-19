import type { Metadata } from "next"
import Link from "next/link"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"
import { SITE_NAME, SITE_URL } from "@/lib/site"

const path = "/bronirovanie"
const title = "Бронирование ПК в компьютерном клубе Пятигорск — Мираж"
const description =
  "Как забронировать ПК или PS5 в Мираж PRO и Мираж в Пятигорске: Telegram, телефоны, шаги бронирования, ночной тариф и FAQ."

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

const bookingFaq = [
  {
    question: "Нужно ли бронировать место заранее?",
    answer:
      "В будни часто можно прийти без брони. В пятницу, субботу и перед ночным тарифом лучше записаться — особенно на VIP, BOOTCAMP и PS5.",
  },
  {
    question: "Можно ли забронировать через Telegram?",
    answer:
      "Да. Напишите в @mirage_cyber26: укажите клуб (Мираж PRO или Мираж), зону или PS5, дату, время и сколько человек.",
  },
  {
    question: "Как долго держат бронь?",
    answer:
      "Обычно место держат разумное время по договорённости с администратором. Если опаздываете — предупредите по телефону или в Telegram.",
  },
  {
    question: "Можно ли забронировать ночной тариф?",
    answer:
      "Да. Уточните зону и дату. Ночь действует с 22:00 до 8:00 — подробности и цены на странице ночного тарифа.",
  },
  {
    question: "Нужен ли паспорт или депозит?",
    answer:
      "Условия уточняйте у администратора выбранного клуба. Оплата — наличные, карта или СБП. Актуальные пакеты смотрите в разделе цен.",
  },
]

const faqJsonLd = {
  "@context": "https://schema.org",
  "@type": "FAQPage",
  mainEntity: bookingFaq.map((item) => ({
    "@type": "Question",
    name: item.question,
    acceptedAnswer: {
      "@type": "Answer",
      text: item.answer,
    },
  })),
}

export default function BronirovaniePage() {
  return (
    <>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(faqJsonLd) }}
      />
      <ParticleBackground />
      <article className="relative z-10 mx-auto max-w-3xl px-3 pb-16 pt-24 sm:px-4 sm:pb-20 sm:pt-28">
        <nav className="mb-6 text-xs text-muted sm:text-sm" aria-label="Хлебные крошки">
          <Link href="/" className="hover:text-white">
            Главная
          </Link>
          <span className="mx-2">/</span>
          <span className="text-white/70">Бронирование</span>
        </nav>

        <h1 className="mb-5 text-3xl font-black sm:text-5xl">
          Бронирование ПК в компьютерном клубе{" "}
          <span className="gradient-text">Пятигорска</span>
        </h1>
        <p className="mb-8 text-base leading-relaxed text-muted sm:text-lg">
          Забронировать игровое место в Мираж PRO или Мираж можно по телефону клуба или
          через Telegram. Ниже — пошаговая инструкция, контакты и ответы на частые
          вопросы о бронировании ПК и PlayStation 5.
        </p>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-white/80 sm:text-base">
          <h2 className="text-2xl font-bold text-white">Как забронировать за 4 шага</h2>
          <ol className="list-decimal space-y-3 pl-5 text-muted">
            <li>
              <strong className="text-white">Выберите клуб.</strong> Флагман на
              Панагюриште — больше ПК и топовые зоны; Мираж на Октябрьской — центр и
              камерный формат. Сравнение —{" "}
              <Link
                href="/mirage-vs-mirage-pro"
                className="text-white underline-offset-2 hover:underline"
              >
                здесь
              </Link>
              .
            </li>
            <li>
              <strong className="text-white">Определите зону или PS5.</strong> ARENA,
              COMFORT, VIP / BOOTCAMP / DUO VIP или консоль. Цены — на{" "}
              <Link href="/prices" className="text-white underline-offset-2 hover:underline">
                странице тарифов
              </Link>
              .
            </li>
            <li>
              <strong className="text-white">Свяжитесь с нами.</strong> Напишите в
              Telegram или позвоните в нужный клуб — укажите дату, время и число гостей.
            </li>
            <li>
              <strong className="text-white">Приходите вовремя.</strong> Оплатите пакет на
              стойке и занимайте место. Если опаздываете — предупредите администратора.
            </li>
          </ol>
        </section>

        <section className="mb-10 grid gap-4 sm:grid-cols-2">
          {clubs.map((club) => (
            <div key={club.id} className="glass rounded-3xl p-6">
              <h2 className="mb-2 text-xl font-bold">{club.name}</h2>
              <p className="mb-3 text-sm text-muted">{club.address}</p>
              <a
                href={`tel:+${club.phone.replace(/\D/g, "")}`}
                className="mb-2 block text-sm font-semibold gradient-text"
              >
                {club.phone}
              </a>
              <a
                href={`mailto:${club.email}`}
                className="mb-4 block break-all text-sm text-white/75 hover:text-white"
              >
                {club.email}
              </a>
              <Link
                href={`/clubs/${club.id}`}
                className="inline-flex rounded-xl border border-white/10 px-4 py-2 text-sm text-white hover:bg-white/5"
              >
                Страница клуба
              </Link>
            </div>
          ))}
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Бронь через Telegram</h2>
          <p>
            Самый быстрый способ для большинства гостей — написать в{" "}
            <a
              href="https://t.me/mirage_cyber26"
              target="_blank"
              rel="noopener noreferrer"
              className="text-white underline-offset-2 hover:underline"
            >
              @mirage_cyber26
            </a>
            . В сообщении укажите:
          </p>
          <ul className="list-disc space-y-2 pl-5">
            <li>Клуб: Мираж PRO (Панагюриште, 4) или Мираж (Октябрьская, 46)</li>
            <li>Что нужно: ПК (зона) или PS5</li>
            <li>Дата и время начала</li>
            <li>Сколько человек / сколько мест</li>
            <li>Если нужна ночь — напишите «ночной тариф»</li>
          </ul>
          <p>
            Подробнее о ночном пакете (22:00–8:00) — на странице{" "}
            <Link
              href="/nochnoy-tarif-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              ночного тарифа в Пятигорске
            </Link>
            . Контакты и карты обоих клубов — в разделе{" "}
            <Link href="/kontakty" className="text-white underline-offset-2 hover:underline">
              контактов
            </Link>
            .
          </p>
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Когда бронировать особенно стоит</h2>
          <p>
            Вечер пятницы и субботы, праздники, большие компании и ночные сессии — часы
            пик. VIP, BOOTCAMP, DUO VIP и PlayStation 5 разбирают быстрее обычной арены.
            Если собираетесь компанией от 4 человек — напишите заранее, чтобы места стояли
            рядом.
          </p>
          <p>
            Ищете клуб «рядом» по смыслу запроса:{" "}
            <Link
              href="/kompyuternyy-klub-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              компьютерный клуб
            </Link>
            ,{" "}
            <Link
              href="/kiberklub-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              киберклуб
            </Link>
            ,{" "}
            <Link
              href="/igrovoy-klub-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              игровой клуб
            </Link>{" "}
            или{" "}
            <Link
              href="/ps5-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              PS5 в Пятигорске
            </Link>
            .
          </p>
        </section>

        <section className="mb-10">
          <h2 className="mb-4 text-2xl font-bold text-white">FAQ по бронированию</h2>
          <div className="space-y-3">
            {bookingFaq.map((item) => (
              <details
                key={item.question}
                className="group glass rounded-3xl transition-colors open:bg-white/[0.06]"
              >
                <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-3 p-5 font-medium focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-orange-500">
                  <span className="text-sm sm:text-base">{item.question}</span>
                  <span
                    aria-hidden="true"
                    className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full gradient-bg text-sm font-bold text-black transition-transform group-open:rotate-45"
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
          <p className="mt-4 text-sm text-muted sm:text-base">
            Больше ответов — в общем{" "}
            <Link href="/faq" className="text-white underline-offset-2 hover:underline">
              разделе вопросов
            </Link>
            .
          </p>
        </section>

        <section className="glass rounded-3xl p-6 sm:p-8">
          <h2 className="mb-4 text-xl font-bold">Связаться сейчас</h2>
          <div className="flex flex-wrap gap-3">
            <a
              href="https://t.me/mirage_cyber26"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex rounded-xl gradient-bg px-5 py-3 text-sm font-bold text-black"
            >
              Написать в Telegram
            </a>
            {clubs.map((club) => (
              <a
                key={club.id}
                href={`tel:+${club.phone.replace(/\D/g, "")}`}
                className="inline-flex rounded-xl border border-white/10 px-5 py-3 text-sm font-medium text-white hover:bg-white/5"
              >
                {club.name}: {club.phone}
              </a>
            ))}
          </div>
        </section>
      </article>
    </>
  )
}
