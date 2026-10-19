"use client"

import { useState } from "react"
import Link from "next/link"
import { clubs } from "@/data/clubs"

export function ClubsPreview() {
  const [selected, setSelected] = useState(clubs[0].id)

  return (
    <section className="relative px-3 py-16 sm:px-4 sm:py-24">
      <div className="max-w-7xl mx-auto">
        <div className="text-center mb-16">
          <h2 className="mb-4 text-3xl font-black sm:text-6xl">
            Наши <span className="gradient-text">клубы</span>
          </h2>
          <p className="mx-auto mb-9 max-w-xl text-base text-muted sm:mb-12 sm:text-xl">
            Два компьютерных клуба в Пятигорске — выбирай свой формат
          </p>

          <div
            role="tablist"
            aria-label="Выбор клуба"
            className="club-tabs-motion grid w-full max-w-sm grid-cols-2 items-center gap-2 rounded-2xl p-1.5 liquid-glass-nav sm:inline-flex sm:w-auto sm:max-w-none"
          >
            {clubs.map((club) => (
              <button
                type="button"
                role="tab"
                aria-selected={selected === club.id}
                aria-controls={`club-preview-${club.id}`}
                key={club.id}
                onClick={() => setSelected(club.id)}
                className={`relative min-w-0 rounded-xl px-3 py-3 text-sm font-bold transition-all duration-300 sm:px-8 ${
                  selected === club.id
                    ? "text-white"
                    : "text-white/40 hover:text-white/70"
                }`}
              >
                {selected === club.id && (
                  <div className="club-tab-indicator absolute inset-0 rounded-xl gradient-bg" />
                )}
                <span className="relative z-10">{club.name}</span>
              </button>
            ))}
          </div>
        </div>

        {clubs.map((club) => {
          const isActive = club.id === selected
          return (
            <div
              key={club.id}
              id={`club-preview-${club.id}`}
              role="tabpanel"
              hidden={!isActive}
              className="club-panel-motion"
            >
              <div className="club-card-motion group relative block overflow-hidden rounded-3xl glass hover:glass-hover">
                <div
                  className="club-card-image absolute inset-0 bg-cover bg-center opacity-20"
                  style={{ backgroundImage: `url('/clubs/${club.id}-fast.jpg')` }}
                />
                <div className="relative z-10 p-6 sm:p-14">
                  <div className="mb-8 flex flex-col items-start gap-4 sm:mb-10 sm:flex-row sm:justify-between">
                    <div>
                      {club.id === "mirage-pro" && (
                        <span className="mb-2 inline-block text-xs font-semibold uppercase tracking-widest text-orange-400">
                          Флагман сети
                        </span>
                      )}
                      <h3 className="text-3xl font-bold sm:text-4xl">{club.name}</h3>
                      <p className="mt-2 text-sm text-muted">
                        ★ {club.rating.value.toFixed(1)} · {club.rating.count} оценок ·{" "}
                        {club.address}
                      </p>
                    </div>
                    <div className="rounded-full gradient-bg px-4 py-1.5 text-sm font-bold text-black">
                      {club.totalPC} ПК
                    </div>
                  </div>

                  <p className="mb-8 max-w-4xl text-base leading-relaxed text-muted sm:mb-10 sm:text-lg">
                    {club.description}
                  </p>

                  <div className="mb-8 flex flex-wrap gap-3">
                    {club.specs.slice(0, 5).map((spec) => (
                      <span
                        key={spec}
                        className="club-spec-chip rounded-full bg-white/5 px-4 py-2 text-sm text-muted sm:px-5 sm:py-2.5 sm:text-base"
                      >
                        {spec}
                      </span>
                    ))}
                  </div>

                  <div className="flex flex-wrap items-center justify-between gap-5 border-t border-white/5 pt-8">
                    <span className="break-words text-base text-muted">{club.address}</span>
                    <div className="flex w-full flex-col gap-3 sm:w-auto sm:flex-row">
                      <a
                        href={`tel:+${club.phone.replace(/\D/g, "")}`}
                        className="motion-cta motion-cta-primary inline-flex min-h-12 items-center justify-center rounded-xl gradient-bg px-7 py-3 text-base font-bold text-black"
                      >
                        Забронировать
                      </a>
                      <Link
                        href={`/clubs/${club.id}`}
                        className="motion-cta motion-cta-secondary inline-flex min-h-12 items-center justify-center rounded-xl border border-white/10 px-7 py-3 text-base font-semibold text-white"
                      >
                        Подробнее →
                      </Link>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )
        })}
      </div>
    </section>
  )
}
