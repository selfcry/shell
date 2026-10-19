"use client"

import { use, useMemo, useState, type MouseEvent } from "react"
import Link from "next/link"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"
import { PCSpecsTable } from "@/components/PCSpecsTable"
import { Gallery } from "@/components/Gallery"
import { MapComponent } from "@/components/MapComponent"

function formatPhone(phone: string) {
  return `+${phone.replace(/\D/g, "")}`
}

function isDesktopPointer() {
  return typeof window !== "undefined" && window.matchMedia("(pointer: fine)").matches
}

export default function ClubPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = use(params)
  const club = useMemo(() => clubs.find((c) => c.id === slug), [slug])
  const [phoneModalOpen, setPhoneModalOpen] = useState(false)

  function handleCallClick(e: MouseEvent<HTMLAnchorElement>) {
    if (isDesktopPointer()) {
      e.preventDefault()
      setPhoneModalOpen(true)
    }
  }

  if (!club) {
    return (
      <>
        <ParticleBackground />
        <div className="relative z-10 pt-32 pb-16 text-center">
          <h1 className="text-4xl font-black mb-4">Клуб не найден</h1>
          <Link href="/clubs/mirage-pro" className="gradient-text hover:underline">← Все клубы</Link>
        </div>
      </>
    )
  }

  return (
    <>
      <ParticleBackground />
      <div className="relative z-10 pb-12 pt-20 sm:pb-16 sm:pt-24">
        <div className="mx-auto max-w-7xl px-3 sm:px-4">
          <div className="mb-6 flex items-center gap-2 overflow-x-auto whitespace-nowrap pb-1 text-xs text-muted sm:text-sm">
            <Link href="/" className="hover:text-white transition-colors">Главная</Link>
            <span>/</span>
            <span className="text-white/60">{club.name}</span>
          </div>

          <div className="mb-8 flex justify-center sm:mb-10">
            <>
              <div className="grid w-full max-w-sm grid-cols-2 items-center gap-2 rounded-2xl p-1.5 liquid-glass-nav sm:inline-flex sm:w-auto sm:max-w-none">
                {clubs.map((c) => (
                  <Link
                    key={c.id}
                    href={`/clubs/${c.id}`}
                    aria-current={c.id === club.id ? "page" : undefined}
                    className={`relative min-w-0 rounded-xl px-3 py-3 text-sm font-bold transition-all duration-300 sm:px-8 ${
                      c.id === club.id
                        ? "text-white"
                        : "text-white/40 hover:text-white/70"
                    }`}
                  >
                    {c.id === club.id && (
                      <div className="absolute inset-0 rounded-xl gradient-bg" />
                    )}
                    <span className="relative z-10">{c.name}</span>
                  </Link>
                ))}
              </div>
            </>
          </div>

          <div className="mb-8">
            <div className="mb-4 flex flex-wrap items-start justify-between gap-4">
              <div>
                <h1 className="text-3xl font-black sm:text-5xl lg:text-6xl">
                  {club.seoH1}
                </h1>
                <p className="mt-2 text-sm text-muted sm:text-base">
                  {club.address}, {club.postalCode} · {club.workTime}
                </p>
                <p className="mt-3 text-sm text-white/80 sm:text-base">
                  ★ {club.rating.value.toFixed(1)} · {club.rating.count} оценок на{" "}
                  <a
                    href={club.rating.sourceUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="underline-offset-2 hover:underline"
                  >
                    {club.rating.source}
                  </a>
                </p>
              </div>
              <div className="flex items-center gap-3">
                <div className="rounded-full gradient-bg px-3 py-1.5 text-xs font-bold text-black">
                  {club.totalPC} ПК
                </div>
                {club.totalPS > 0 && (
                  <div className="rounded-full border border-blue-500/30 bg-blue-500/20 px-3 py-1.5 text-xs font-bold text-blue-300">
                    PS5 ×{club.totalPS}
                  </div>
                )}
              </div>
            </div>

            <div className="mb-6 max-w-3xl space-y-3 text-sm leading-relaxed text-muted sm:text-lg">
              {club.about.map((paragraph) => (
                <p key={paragraph.slice(0, 48)}>{paragraph}</p>
              ))}
            </div>

            <div className="flex flex-wrap items-center gap-3 text-sm sm:gap-4 sm:text-base">
              <div className="flex min-w-0 items-start gap-1.5">
                <span className="text-muted">Адрес:</span>
                <span className="break-words">{club.address}</span>
              </div>
              <a
                href={`tel:${formatPhone(club.phone)}`}
                onClick={handleCallClick}
                className="inline-flex min-h-12 w-full items-center justify-center gap-2 rounded-xl gradient-bg px-5 py-3 text-base font-bold text-black transition-all hover:scale-105 hover:opacity-90 active:scale-95 sm:min-h-14 sm:w-auto sm:px-9 sm:py-3.5 sm:text-lg"
              >
                Позвонить для брони
              </a>
              {club.socialLinks?.telegram && (
                <a
                  href={club.socialLinks.telegram}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex min-h-12 w-full items-center justify-center rounded-xl border border-[#0088CC]/40 bg-[#0088CC]/15 px-5 py-3 text-base font-bold text-[#50baf0] transition-colors hover:bg-[#0088CC]/25 sm:min-h-14 sm:w-auto sm:px-9 sm:py-3.5 sm:text-lg"
                >
                  Написать в Telegram
                </a>
              )}
              <Link
                href="/prices"
                className="inline-flex min-h-12 w-full items-center justify-center rounded-xl border border-white/10 px-5 py-3 text-base font-medium text-white transition-colors hover:bg-white/5 sm:min-h-14 sm:w-auto sm:px-9 sm:py-3.5 sm:text-lg"
              >
                Смотреть цены
              </Link>
              <Link
                href="/faq"
                className="inline-flex min-h-12 w-full items-center justify-center rounded-xl border border-white/10 px-5 py-3 text-base font-medium text-white transition-colors hover:bg-white/5 sm:min-h-14 sm:w-auto sm:px-9 sm:py-3.5 sm:text-lg"
              >
                Вопросы и ответы
              </Link>
              <div className="flex items-center gap-1.5 text-green-400">
                <span>Режим:</span>
                {club.workTime}
              </div>
            </div>
          </div>

          {club.gallery.length > 0 && (
            <div className="mb-10">
              <h2 className="text-2xl font-bold mb-6">
                Фотогалерея
              </h2>
              <Gallery images={club.gallery} clubName={club.name} />
            </div>
          )}

          <div className="mb-10 grid min-w-0 gap-8 lg:grid-cols-3">
            <div className="min-w-0 space-y-6 lg:col-span-2">
              <PCSpecsTable zones={club.zones} showTitle />

              <div className="glass rounded-3xl p-6 sm:p-8 lg:p-10">
                <h3 className="text-2xl font-bold mb-6">
                  Тарифы
                </h3>
                <div className="-mx-4 overflow-x-auto overscroll-x-contain sm:mx-0">
                  <table className="w-full text-sm sm:text-base">
                    <thead>
                      <tr className="border-b border-white/10">
                        <th className="text-left py-2 sm:py-3 px-3 sm:px-4 text-muted font-medium">Зона</th>
                        <th className="text-right py-2 sm:py-3 px-3 sm:px-4 text-muted font-medium">1 час</th>
                        <th className="text-right py-2 sm:py-3 px-3 sm:px-4 text-muted font-medium">3 часа</th>
                        <th className="text-right py-2 sm:py-3 px-3 sm:px-4 text-muted font-medium">5 часов</th>
                        <th className="text-right py-2 sm:py-3 px-3 sm:px-4 text-muted font-medium">Ночь</th>
                      </tr>
                    </thead>
                    <tbody>
                      {club.prices.map((price) => {
                        const p = price.prices
                        return (
                          <tr key={price.name} className="border-b border-white/5 hover:bg-white/5 transition-colors">
                            <td className="py-2 sm:py-3 px-3 sm:px-4 font-medium whitespace-nowrap">{price.name}</td>
                            <td className="text-right py-2 sm:py-3 px-3 sm:px-4 gradient-text font-semibold whitespace-nowrap">{p["1 час"]}₽</td>
                            <td className="text-right py-2 sm:py-3 px-3 sm:px-4 gradient-text font-semibold whitespace-nowrap">{p["3 часа"] > 0 ? `${p["3 часа"]}₽` : "—"}</td>
                            <td className="text-right py-2 sm:py-3 px-3 sm:px-4 gradient-text font-semibold whitespace-nowrap">{p["5 часов"] > 0 ? `${p["5 часов"]}₽` : "—"}</td>
                            <td className="text-right py-2 sm:py-3 px-3 sm:px-4 gradient-text font-semibold whitespace-nowrap">
                              {p["Ночь (22:00-8:00)"] > 0 ? `${p["Ночь (22:00-8:00)"]}₽` : "—"}
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>
              </div>

              {club.promotions.length > 0 && (
                <div className="glass rounded-3xl p-6 sm:p-8 lg:p-10">
                  <h3 className="text-2xl font-bold mb-6">
                    Акции <span className="gradient-text">{club.name}</span>
                  </h3>
                  <div className="grid sm:grid-cols-2 gap-3">
                    {club.promotions.map((promo) => (
                      <div
                        key={promo.title}
                        className="flex items-start gap-3 p-4 rounded-xl bg-white/[0.02] border border-white/5"
                      >
                        <div>
                          <div className="font-medium text-sm mb-0.5">{promo.title}</div>
                          <div className="text-xs text-muted">{promo.description}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              <div className="glass rounded-2xl overflow-hidden min-h-[300px]">
                <MapComponent
                  key={club.id}
                  selectedId={club.id}
                />
              </div>
              <div className="glass rounded-3xl p-6 sm:p-8">
                <h3 className="mb-3 text-xl font-bold sm:text-2xl">Как добраться</h3>
                <p className="text-sm leading-relaxed text-muted sm:text-base">{club.howToGet}</p>
              </div>
              <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap">
                <a
                  href={`https://yandex.ru/maps/?pt=${club.coords[0]},${club.coords[1]}&z=16&l=map`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex min-h-11 w-full items-center justify-center rounded-xl border border-white/10 px-5 py-2.5 text-center text-sm font-medium transition-colors hover:bg-white/5 sm:w-auto"
                >
                  Построить маршрут
                </a>
                <a
                  href={club.mapUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex min-h-11 w-full items-center justify-center rounded-xl border border-white/10 px-5 py-2.5 text-center text-sm font-medium transition-colors hover:bg-white/5 sm:w-auto"
                >
                  Открыть в Яндекс Картах
                </a>
                <Link
                  href={club.id === "mirage-pro" ? "/clubs/mirage" : "/clubs/mirage-pro"}
                  className="inline-flex min-h-11 w-full items-center justify-center rounded-xl border border-white/10 px-5 py-2.5 text-center text-sm font-medium transition-colors hover:bg-white/5 sm:w-auto"
                >
                  {club.id === "mirage-pro" ? "Второй клуб — Мираж" : "Флагман — Мираж PRO"}
                </Link>
              </div>
            </div>

            <div className="min-w-0 space-y-6">
              <div className="glass rounded-3xl p-6 sm:p-8">
                <h4 className="font-bold text-base mb-5">Услуги и удобства</h4>
                <ul className="space-y-3">
                  {club.features.map((f) => (
                    <li key={f} className="flex items-center gap-2.5 text-sm text-muted">
                      <span className="w-1.5 h-1.5 rounded-full bg-orange-500/60 shrink-0" />
                      {f}
                    </li>
                  ))}
                </ul>
              </div>

              <div className="glass rounded-3xl p-6 sm:p-8">
                <h4 className="font-bold text-base mb-5">Способы оплаты</h4>
                <div className="flex flex-wrap gap-2">
                  {club.paymentMethods.map((m) => (
                    <span
                      key={m}
                      className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl bg-white/5 text-sm text-muted border border-white/5"
                    >
                      {m}
                    </span>
                  ))}
                </div>
              </div>

              <div className="glass rounded-3xl p-7 sm:p-9">
                <h4 className="font-bold text-xl mb-5">Контакты {club.name}</h4>
                <div className="space-y-4">
                  <div>
                    <span className="text-xs text-muted uppercase tracking-wider">{club.name}</span>
                    <a
                      href={`tel:${formatPhone(club.phone)}`}
                      onClick={handleCallClick}
                      className="mt-1 block text-xl font-bold gradient-text hover:underline sm:text-2xl"
                    >
                      {club.phone}
                    </a>
                  </div>
                  <div>
                    <span className="text-xs text-muted uppercase tracking-wider">Почта</span>
                    <a
                      href={`mailto:${club.email}`}
                      className="mt-1 block break-all text-sm text-white/70 transition-colors hover:text-white"
                    >
                      {club.email}
                    </a>
                  </div>
                  <div className="flex items-center gap-2 text-green-400 text-sm">
                    <span>Режим:</span>
                    {club.workTime}
                  </div>
                </div>
              </div>

              {club.socialLinks && (
                <div className="glass rounded-3xl p-7 sm:p-9">
                  <h4 className="font-bold text-xl mb-5">Мы в соцсетях</h4>
                  <div className="flex flex-wrap gap-2">
                    {club.socialLinks.telegram && (
                      <a href={club.socialLinks.telegram} target="_blank" rel="noopener noreferrer"
                        className="px-4 py-2.5 rounded-xl bg-[#0088CC]/20 text-[#0088CC] text-sm font-medium hover:bg-[#0088CC]/30 transition-colors"
                      >Telegram</a>
                    )}
                    {club.socialLinks.vk && (
                      <a href={club.socialLinks.vk} target="_blank" rel="noopener noreferrer"
                        className="px-4 py-2.5 rounded-xl bg-[#4A76A8]/20 text-[#4A76A8] text-sm font-medium hover:bg-[#4A76A8]/30 transition-colors"
                      >VK</a>
                    )}
                    {club.socialLinks.tiktok && (
                      <a href={club.socialLinks.tiktok} target="_blank" rel="noopener noreferrer"
                        className="px-4 py-2.5 rounded-xl bg-white/10 text-white text-sm font-medium hover:bg-white/15 transition-colors"
                      >TikTok</a>
                    )}
                    {club.socialLinks.instagram && (
                      <a href={club.socialLinks.instagram} target="_blank" rel="noopener noreferrer"
                        className="px-4 py-2.5 rounded-xl bg-[#E4405F]/20 text-[#E4405F] text-sm font-medium hover:bg-[#E4405F]/30 transition-colors"
                      >Instagram</a>
                    )}
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>

      {phoneModalOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 px-4"
          onClick={() => setPhoneModalOpen(false)}
        >
          <div
            className="glass w-full max-w-sm rounded-3xl p-8 text-center"
            onClick={(e) => e.stopPropagation()}
          >
            <p className="mb-2 text-sm text-muted">{club.name}</p>
            <p className="mb-6 text-3xl font-bold gradient-text">{club.phone}</p>
            <button
              type="button"
              onClick={() => setPhoneModalOpen(false)}
              className="inline-flex min-h-11 w-full items-center justify-center rounded-xl border border-white/10 px-5 py-2.5 text-sm font-medium transition-colors hover:bg-white/5"
            >
              Закрыть
            </button>
          </div>
        </div>
      )}
    </>
  )
}
