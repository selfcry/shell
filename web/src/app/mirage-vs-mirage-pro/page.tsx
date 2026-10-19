import type { Metadata } from "next"
import Link from "next/link"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"
import { SITE_NAME, SITE_URL } from "@/lib/site"

const path = "/mirage-vs-mirage-pro"
const title = "Мираж или Мираж PRO — какой компьютерный клуб выбрать в Пятигорске"
const description =
  "Сравнение клубов Мираж и Мираж PRO в Пятигорске: адреса, ПК, PS5, зоны, цены и кому какой клуб подойдёт."

export const metadata: Metadata = {
  title: { absolute: `${title} | ${SITE_NAME}` },
  description,
  alternates: { canonical: `${SITE_URL}${path}` },
  openGraph: { title, description, url: `${SITE_URL}${path}`, type: "article" },
}

const [pro, mirage] = [clubs.find((c) => c.id === "mirage-pro")!, clubs.find((c) => c.id === "mirage")!]

export default function CompareClubsPage() {
  return (
    <>
      <ParticleBackground />
      <article className="relative z-10 mx-auto max-w-3xl px-3 pb-16 pt-24 sm:px-4 sm:pb-20 sm:pt-28">
        <nav className="mb-6 text-xs text-muted sm:text-sm">
          <Link href="/" className="hover:text-white">
            Главная
          </Link>
          <span className="mx-2">/</span>
          <span className="text-white/70">Мираж vs Мираж PRO</span>
        </nav>

        <h1 className="mb-5 text-3xl font-black sm:text-5xl">
          Мираж или <span className="gradient-text">Мираж PRO</span>?
        </h1>
        <p className="mb-8 text-base leading-relaxed text-muted sm:text-lg">
          Оба клуба — одна сеть в Пятигорске, но формат разный. Коротко: PRO — больше ПК и
          топовые зоны, Мираж — камернее и ближе к центру.
        </p>

        <div className="-mx-3 mb-10 overflow-x-auto sm:mx-0">
          <table className="w-full min-w-[560px] text-sm sm:text-base">
            <thead>
              <tr className="border-b border-white/10 text-left text-muted">
                <th className="px-3 py-3 font-medium">Параметр</th>
                <th className="px-3 py-3 font-medium">{pro.name}</th>
                <th className="px-3 py-3 font-medium">{mirage.name}</th>
              </tr>
            </thead>
            <tbody className="text-white/85">
              {[
                ["Адрес", pro.address, mirage.address],
                ["ПК", `${pro.totalPC}`, `${mirage.totalPC}`],
                ["PS5", `${pro.totalPS}`, `${mirage.totalPS}`],
                ["Рейтинг Яндекс", `${pro.rating.value} (${pro.rating.count})`, `${mirage.rating.value} (${mirage.rating.count})`],
                ["От 1 часа", `${Math.min(...pro.prices.map((p) => p.prices["1 час"]))}₽`, `${Math.min(...mirage.prices.map((p) => p.prices["1 час"]))}₽`],
                ["Особенности", "BOOTCAMP / DUO VIP, до 400Hz", "VIP, SQUAD CAMP, центр"],
              ].map(([label, a, b]) => (
                <tr key={label} className="border-b border-white/5">
                  <td className="px-3 py-3 text-muted">{label}</td>
                  <td className="px-3 py-3">{a}</td>
                  <td className="px-3 py-3">{b}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <section className="mb-8 space-y-3 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Кому подойдёт Мираж PRO</h2>
          <p>
            Выбирайте флагман на ул. Панагюриште, 4, если нужны максимальные мониторы,
            bootcamp на компанию и больше свободных мест. Подробнее — на{" "}
            <Link href="/clubs/mirage-pro" className="text-white underline-offset-2 hover:underline">
              странице Мираж PRO
            </Link>
            .
          </p>
          <h2 className="mt-8 text-2xl font-bold text-white">Кому подойдёт Мираж</h2>
          <p>
            Клуб на Октябрьской ул., 46 удобен, если вы в центре и хотите спокойный формат
            с RTX 3070 и PS5. Смотрите{" "}
            <Link href="/clubs/mirage" className="text-white underline-offset-2 hover:underline">
              страницу Мираж
            </Link>
            .
          </p>
        </section>

        <div className="flex flex-wrap gap-3">
          <Link href="/prices" className="rounded-xl gradient-bg px-5 py-3 text-sm font-bold text-black">
            Сравнить цены
          </Link>
          <Link
            href="/kompyuternyy-klub-pyatigorsk"
            className="rounded-xl border border-white/10 px-5 py-3 text-sm font-medium text-white"
          >
            Компьютерный клуб Пятигорск
          </Link>
        </div>
      </article>
    </>
  )
}
