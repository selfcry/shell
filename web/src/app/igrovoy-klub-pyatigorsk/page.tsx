import type { Metadata } from "next"
import Link from "next/link"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"
import { SITE_NAME, SITE_URL } from "@/lib/site"

const path = "/igrovoy-klub-pyatigorsk"
const title = "Игровой клуб в Пятигорске — ПК и PS5 в сети Мираж"
const description =
  "Игровой клуб в Пятигорске: Мираж PRO и Мираж — 79 ПК, 7 PS5, зоны VIP, ночные тарифы, работа 24/7. Адреса, цены и как забронировать место."

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

const [pro, mirage] = [
  clubs.find((c) => c.id === "mirage-pro")!,
  clubs.find((c) => c.id === "mirage")!,
]

export default function IgrovoyKlubLandingPage() {
  return (
    <>
      <ParticleBackground />
      <article className="relative z-10 mx-auto max-w-3xl px-3 pb-16 pt-24 sm:px-4 sm:pb-20 sm:pt-28">
        <nav className="mb-6 text-xs text-muted sm:text-sm" aria-label="Хлебные крошки">
          <Link href="/" className="hover:text-white">
            Главная
          </Link>
          <span className="mx-2">/</span>
          <span className="text-white/70">Игровой клуб Пятигорск</span>
        </nav>

        <h1 className="mb-5 text-3xl font-black sm:text-5xl">
          Игровой клуб в <span className="gradient-text">Пятигорске</span>
        </h1>
        <p className="mb-8 text-base leading-relaxed text-muted sm:text-lg">
          Игровой клуб Мираж — место, куда приходят за комфортной игрой: мощные ПК,
          PlayStation 5, снеки у администратора и круглосуточный режим. Две точки в
          Пятигорске закрывают разные сценарии — от камерной вечеринки в центре до
          bootcamp на флагмане.
        </p>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-white/80 sm:text-base">
          <h2 className="text-2xl font-bold text-white">Для кого подходит игровой клуб</h2>
          <p>
            В игровой клуб обычно идут не только за «часом CS». Это удобный формат для
            компании друзей, свидания за дуэлью, тренировки перед турниром или просто
            вечера, когда дома железо не тянет новые релизы. В сети Мираж можно прийти
            соло, вдвоём в VIP или собрать отряд на несколько соседних мест.
          </p>
          <p>
            Оба клуба принимают наличные, карты и СБП. Есть клубные аккаунты Steam/Epic,
            можно скачивать игры, курить электронные сигареты (кроме IQOS). Wi‑Fi и
            кондиционер есть в каждой зоне.
          </p>
          <ul className="list-disc space-y-2 pl-5 text-muted">
            <li>Соло, пати и VIP-форматы</li>
            <li>ПК и PlayStation 5 в одном месте</li>
            <li>Пакеты на 1 / 3 / 5 часов и ночь</li>
            <li>Акции: день рождения ×2, бонусы за отзыв</li>
            <li>Работа без выходных, 24 часа</li>
          </ul>
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Два формата одной сети</h2>
          <p>
            <strong className="text-white">{pro.fullName}</strong> — {pro.address}.{" "}
            {pro.totalPC} ПК, {pro.totalPS} PS5, рейтинг ★{pro.rating.value.toFixed(1)} (
            {pro.rating.count} оценок на {pro.rating.source}). Здесь мониторы до 400 Гц,
            зоны BOOTCAMP и DUO VIP — если нужен максимум производительности и мест.
          </p>
          <p>
            <strong className="text-white">{mirage.fullName}</strong> — {mirage.address}.{" "}
            {mirage.totalPC} ПК, {mirage.totalPS} PS5, рейтинг ★
            {mirage.rating.value.toFixed(1)} ({mirage.rating.count} оценок). Центр города,
            RTX 3070, VIP и SQUAD CAMP — удобно для спокойных сессий и компаний поменьше.
          </p>
          <div className="grid gap-4 pt-2 sm:grid-cols-2">
            {clubs.map((club) => (
              <div key={club.id} className="glass rounded-3xl p-5">
                <h3 className="mb-2 text-lg font-bold text-white">{club.name}</h3>
                <p className="mb-3 text-sm text-muted">{club.address}</p>
                <Link
                  href={`/clubs/${club.id}`}
                  className="inline-flex rounded-xl gradient-bg px-4 py-2.5 text-sm font-bold text-black"
                >
                  Подробнее о клубе
                </Link>
              </div>
            ))}
          </div>
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Что поиграть и сколько стоит</h2>
          <p>
            На ПК доступны актуальные онлайн-игры через клубные и личные аккаунты — от
            шутеров и королевских битв до синглплеера. На консолях — каталог PlayStation
            5. Актуальные цены по зонам смотрите на{" "}
            <Link href="/prices" className="text-white underline-offset-2 hover:underline">
              странице цен
            </Link>
            : там же ночные пакеты с 22:00 до 8:00.
          </p>
          <p>
            Хотите заранее понять разницу между локациями — откройте{" "}
            <Link
              href="/mirage-vs-mirage-pro"
              className="text-white underline-offset-2 hover:underline"
            >
              сравнение клубов
            </Link>
            . Ищете именно киберформат с упором на пинг и мониторы —{" "}
            <Link
              href="/kiberklub-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              киберклуб в Пятигорске
            </Link>
            . Общая страница по запросу «компьютерный клуб» —{" "}
            <Link
              href="/kompyuternyy-klub-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              здесь
            </Link>
            .
          </p>
          <p>
            Забронировать место:{" "}
            <Link
              href="/bronirovanie"
              className="text-white underline-offset-2 hover:underline"
            >
              как забронировать ПК
            </Link>
            . Контакты и карты — на{" "}
            <Link href="/kontakty" className="text-white underline-offset-2 hover:underline">
              странице контактов
            </Link>
            . Ответы на типовые вопросы — в{" "}
            <Link href="/faq" className="text-white underline-offset-2 hover:underline">
              FAQ
            </Link>
            .
          </p>
        </section>

        <section className="mb-10 space-y-3 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Как добраться</h2>
          <p>{pro.howToGet}</p>
          <p>{mirage.howToGet}</p>
        </section>

        <section className="glass rounded-3xl p-6 sm:p-8">
          <h2 className="mb-4 text-xl font-bold">Контакты игровых клубов</h2>
          <ul className="space-y-4 text-sm text-muted sm:text-base">
            {clubs.map((club) => (
              <li key={club.id}>
                <strong className="text-white">{club.name}</strong>
                <br />
                {club.address}, {club.postalCode}
                <br />
                Тел.:{" "}
                <a href={`tel:+${club.phone.replace(/\D/g, "")}`} className="gradient-text">
                  {club.phone}
                </a>
                <br />
                Email:{" "}
                <a href={`mailto:${club.email}`} className="text-white/80 hover:text-white">
                  {club.email}
                </a>
              </li>
            ))}
          </ul>
        </section>
      </article>
    </>
  )
}
