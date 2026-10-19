import type { Metadata } from "next"
import Link from "next/link"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"
import { SITE_NAME, SITE_URL } from "@/lib/site"

const path = "/nochnoy-tarif-pyatigorsk"
const title = "Ночной тариф в компьютерном клубе Пятигорск — Мираж PRO"
const description =
  "Ночной тариф в компьютерном клубе Пятигорска: с 22:00 до 8:00 в Мираж PRO и Мираж. Цены по зонам, что входит, как забронировать ночь."

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

export default function NochnoyTarifLandingPage() {
  return (
    <>
      <ParticleBackground />
      <article className="relative z-10 mx-auto max-w-3xl px-3 pb-16 pt-24 sm:px-4 sm:pb-20 sm:pt-28">
        <nav className="mb-6 text-xs text-muted sm:text-sm" aria-label="Хлебные крошки">
          <Link href="/" className="hover:text-white">
            Главная
          </Link>
          <span className="mx-2">/</span>
          <span className="text-white/70">Ночной тариф Пятигорск</span>
        </nav>

        <h1 className="mb-5 text-3xl font-black sm:text-5xl">
          Ночной тариф в компьютерном клубе{" "}
          <span className="gradient-text">Пятигорска</span>
        </h1>
        <p className="mb-8 text-base leading-relaxed text-muted sm:text-lg">
          Хотите поиграть ночью без почасовой «нарезки»? В клубах Мираж действует ночной
          тариф <strong className="text-white">с 22:00 до 8:00</strong> — фиксированная
          цена за всю ночь в выбранной зоне. Оба клуба работают круглосуточно, так что
          можно спокойно досидеть до утра.
        </p>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-white/80 sm:text-base">
          <h2 className="text-2xl font-bold text-white">Как работает ночной тариф</h2>
          <p>
            Ночной пакет рассчитан на длинную сессию: вы оплачиваете ночь по зоне (ARENA,
            COMFORT, VIP / BOOTCAMP и т.д.) и играете в интервале с 22:00 до 8:00. Это
            выгоднее, чем набирать часы по одному, если планируете провести в клубе
            большую часть ночи — рейды, катки с друзьями, просмотр стримов или марафон
            по сюжетке.
          </p>
          <p>
            Клубы не закрываются на ночь: администратор на месте, снеки и напитки можно
            взять через стойку, интернет стабильный (свыше 1 Гбит/с). Если место
            критично — особенно в пятницу и субботу — лучше{" "}
            <Link
              href="/bronirovanie"
              className="text-white underline-offset-2 hover:underline"
            >
              забронировать заранее
            </Link>
            .
          </p>
          <ul className="list-disc space-y-2 pl-5 text-muted">
            <li>Интервал: 22:00–8:00</li>
            <li>Фиксированная цена за ночь по зоне</li>
            <li>Доступны ПК-зоны; PS5 — по наличию, уточняйте у администратора</li>
            <li>Оплата: наличные, карта, СБП</li>
          </ul>
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Цены на ночь — Мираж PRO</h2>
          <p className="text-white/80">
            {pro.fullName}, {pro.address}
          </p>
          <div className="overflow-x-auto rounded-3xl glass">
            <table className="w-full min-w-[320px] text-sm sm:text-base">
              <thead>
                <tr className="border-b border-white/10 text-left text-muted">
                  <th className="px-4 py-3 font-medium">Зона</th>
                  <th className="px-4 py-3 font-medium">Ночь (22:00–8:00)</th>
                </tr>
              </thead>
              <tbody className="text-white/85">
                {pro.prices.map((zone) => (
                  <tr key={zone.name} className="border-b border-white/5">
                    <td className="px-4 py-3">{zone.name}</td>
                    <td className="px-4 py-3 font-semibold">
                      {zone.prices["Ночь (22:00-8:00)"]} ₽
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Цены на ночь — Мираж</h2>
          <p className="text-white/80">
            {mirage.fullName}, {mirage.address}
          </p>
          <div className="overflow-x-auto rounded-3xl glass">
            <table className="w-full min-w-[320px] text-sm sm:text-base">
              <thead>
                <tr className="border-b border-white/10 text-left text-muted">
                  <th className="px-4 py-3 font-medium">Зона</th>
                  <th className="px-4 py-3 font-medium">Ночь (22:00–8:00)</th>
                </tr>
              </thead>
              <tbody className="text-white/85">
                {mirage.prices.map((zone) => (
                  <tr key={zone.name} className="border-b border-white/5">
                    <td className="px-4 py-3">{zone.name}</td>
                    <td className="px-4 py-3 font-semibold">
                      {zone.prices["Ночь (22:00-8:00)"]} ₽
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p>
            Полная сетка почасовых и пакетных тарифов — на{" "}
            <Link href="/prices" className="text-white underline-offset-2 hover:underline">
              странице цен
            </Link>
            . Цены могут обновляться — перед визитом уточните у администратора.
          </p>
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Кому выгоднее ночь</h2>
          <p>
            Ночной тариф особенно уместен, если вы планируете 6–10 часов игры подряд: пати
            на ночь, длинный рейд, марафон по сюжету или просто спокойные катки, когда
            днём нет времени. На флагмане{" "}
            <Link
              href="/clubs/mirage-pro"
              className="text-white underline-offset-2 hover:underline"
            >
              Мираж PRO
            </Link>{" "}
            больше ПК и топовые зоны BOOTCAMP / DUO VIP. В{" "}
            <Link
              href="/clubs/mirage"
              className="text-white underline-offset-2 hover:underline"
            >
              Мираж
            </Link>{" "}
            в центре — камернее и часто проще добраться вечером.
          </p>
          <p>
            Сравнить локации можно на странице{" "}
            <Link
              href="/mirage-vs-mirage-pro"
              className="text-white underline-offset-2 hover:underline"
            >
              Мираж vs Мираж PRO
            </Link>
            . Общие подборки:{" "}
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
            в Пятигорске.
          </p>
        </section>

        <section className="mb-10 space-y-3 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Как забронировать ночь</h2>
          <ol className="list-decimal space-y-2 pl-5">
            <li>Выберите клуб и зону (ARENA / COMFORT / VIP и др.).</li>
            <li>
              Позвоните или напишите в{" "}
              <a
                href="https://t.me/mirage_cyber26"
                target="_blank"
                rel="noopener noreferrer"
                className="text-white underline-offset-2 hover:underline"
              >
                Telegram
              </a>
              .
            </li>
            <li>Уточните свободные места на нужную дату и время начала.</li>
            <li>Приходите к 22:00 (или чуть раньше) и оплатите ночной пакет.</li>
          </ol>
          <p>
            Подробный разбор шагов — на{" "}
            <Link
              href="/bronirovanie"
              className="text-white underline-offset-2 hover:underline"
            >
              странице бронирования
            </Link>
            . Телефоны и карты — в{" "}
            <Link href="/kontakty" className="text-white underline-offset-2 hover:underline">
              контактах
            </Link>
            . Частые вопросы — в{" "}
            <Link href="/faq" className="text-white underline-offset-2 hover:underline">
              FAQ
            </Link>
            .
          </p>
        </section>

        <section className="glass rounded-3xl p-6 sm:p-8">
          <h2 className="mb-4 text-xl font-bold">Телефоны для брони ночи</h2>
          <ul className="space-y-3 text-sm text-muted sm:text-base">
            {clubs.map((club) => (
              <li key={club.id}>
                <strong className="text-white">{club.name}</strong> — {club.address}
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
