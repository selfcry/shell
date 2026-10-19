import Link from "next/link"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"

const specRows = [
  { key: "cpu", label: "Процессор" },
  { key: "gpu", label: "Видеокарта" },
  { key: "ram", label: "Оперативная память" },
  { key: "monitor", label: "Монитор" },
  { key: "storage", label: "Накопитель" },
] as const

export default function PricesPage() {
  return (
    <>
      <ParticleBackground />
      <div className="relative z-10 pb-12 pt-20 sm:pb-16 sm:pt-24">
        <div className="mx-auto max-w-7xl px-3 sm:px-4">
          <div className="text-center mb-12">
            <h1 className="mb-4 text-4xl font-black sm:text-7xl">
              Цены компьютерных клубов в{" "}
              <span className="gradient-text">Пятигорске</span>
            </h1>
            <p className="text-base text-muted sm:text-lg">
              Тарифы Мираж PRO (Панагюриште, 4) и Мираж (Октябрьская ул., 46) без скрытых платежей
            </p>
            <div className="mt-5 flex flex-wrap items-center justify-center gap-3 text-sm">
              <Link
                href="/faq"
                className="rounded-xl border border-white/10 px-4 py-2 text-muted transition-colors hover:text-white"
              >
                Частые вопросы
              </Link>
            </div>
          </div>

          {clubs.map((club) => (
            <section
              key={club.id}
              id={`prices-${club.id}`}
              className="mb-14 scroll-mt-28"
              aria-label={`Тарифы ${club.name}`}
            >
              <div className="mb-6 text-center sm:text-left">
                <h2 className="text-2xl font-bold sm:text-3xl">
                  Тарифы <span className="gradient-text">{club.name}</span>
                </h2>
                <p className="mt-2 text-sm text-muted sm:text-base">
                  {club.address} · от {Math.min(...club.prices.map((p) => p.prices["1 час"]))}₽/час ·{" "}
                  <Link href={`/clubs/${club.id}`} className="text-white/80 underline-offset-2 hover:underline">
                    страница клуба
                  </Link>
                </p>
              </div>
              <div className="grid gap-6 lg:grid-cols-2">
                {club.prices.map((price) => {
                  const prices = price.prices
                  const isTv = price.name.startsWith("TV")
                  return (
                    <div
                      key={price.name}
                      className={`glass rounded-3xl p-6 sm:p-10 hover:glass-hover transition-all ${
                        price.name === "BOOTCAMP" || price.name === "DUO VIP" || price.name === "TV VIP"
                          ? "gradient-border"
                          : ""
                      }`}
                    >
                      <div className="mb-5 flex items-center justify-between">
                        <h3 className="text-2xl font-bold">{price.name}</h3>
                        <span className="rounded-full bg-white/5 px-2 py-0.5 text-xs text-muted">
                          {isTv ? "ТВ / консоль" : "ПК"}
                        </span>
                      </div>

                      <div className="space-y-3">
                        {Object.entries(prices).map(([duration, cost]) =>
                          cost > 0 ? (
                            <div
                              key={duration}
                              className="flex items-center justify-between border-b border-white/5 py-2 last:border-0"
                            >
                              <span className="text-base text-muted">{duration}</span>
                              <span className="text-xl font-bold gradient-text">{cost}₽</span>
                            </div>
                          ) : null
                        )}
                      </div>
                    </div>
                  )
                })}
              </div>
            </section>
          ))}

          <div className="glass rounded-3xl p-5 sm:p-8 lg:p-10">
            <h2 className="mb-6 text-xl font-bold sm:text-2xl">
              Характеристики <span className="gradient-text">ПК</span>
            </h2>
            <div className="-mx-5 overflow-x-auto overscroll-x-contain sm:-mx-8 lg:mx-0">
              <table className="w-full text-sm sm:text-base">
                <thead>
                  <tr className="border-b border-white/10">
                    <th className="whitespace-nowrap px-3 py-3 text-left font-medium text-muted sm:px-4">
                      Параметр
                    </th>
                    {clubs.flatMap((club) =>
                      club.zones.map((zone) => (
                        <th
                          key={`${club.id}-${zone.name}`}
                          className="whitespace-nowrap px-3 py-3 text-left font-medium text-muted sm:px-4"
                        >
                          {club.name}: {zone.name}
                        </th>
                      ))
                    )}
                  </tr>
                </thead>
                <tbody>
                  {specRows.map((row) => (
                    <tr
                      key={row.label}
                      className="border-b border-white/5 transition-colors hover:bg-white/5"
                    >
                      <td className="whitespace-nowrap px-3 py-2 font-medium text-muted sm:px-4 sm:py-3">
                        {row.label}
                      </td>
                      {clubs.flatMap((club) =>
                        club.zones.map((zone) => (
                          <td
                            key={`${club.id}-${zone.name}-${row.key}`}
                            className="whitespace-nowrap px-3 py-2 sm:px-4 sm:py-3"
                          >
                            {zone[row.key] ?? "—"}
                          </td>
                        ))
                      )}
                    </tr>
                  ))}
                  <tr className="border-b border-white/5">
                    <td className="whitespace-nowrap px-3 py-2 font-medium text-muted sm:px-4 sm:py-3">
                      Интернет
                    </td>
                    {clubs.flatMap((club) =>
                      club.zones.map((zone) => (
                        <td
                          key={`${club.id}-${zone.name}-internet`}
                          className="whitespace-nowrap px-3 py-2 sm:px-4 sm:py-3"
                        >
                          {club.internet}
                        </td>
                      ))
                    )}
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </>
  )
}
