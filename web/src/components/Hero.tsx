import Image from "next/image"
import { clubs } from "@/data/clubs"

export function Hero() {
  const totalPC = clubs.reduce((sum, club) => sum + club.totalPC, 0)
  const totalPS = clubs.reduce((sum, club) => sum + club.totalPS, 0)

  return (
    <section className="hero-stage relative flex min-h-[100svh] items-center justify-center overflow-hidden px-0 pb-16 pt-28 sm:min-h-[100svh] sm:pb-20 sm:pt-32">
      <Image
        src="/clubs/mirage-pro-fast.jpg"
        alt="Компьютерный клуб Мираж PRO в Пятигорске — игровой зал с ПК"
        fill
        sizes="(max-width: 640px) 100vw, (max-width: 1024px) 100vw, 1440px"
        priority
        unoptimized
        className="hero-backdrop absolute inset-0 object-cover object-center"
      />
      <div className="hero-veil absolute inset-0 bg-gradient-to-b from-background/80 via-background/60 to-background" />

      <div className="hero-ambient absolute inset-0 opacity-30">
        <div className="motion-orb motion-orb-a absolute top-1/4 left-1/4 w-96 h-96 bg-orange-500/20 rounded-full blur-[128px]" />
        <div className="motion-orb motion-orb-b absolute bottom-1/4 right-1/4 w-96 h-96 bg-mirage-yellow/15 rounded-full blur-[128px]" />
      </div>

      <div className="hero-content relative z-10 mx-auto w-full max-w-7xl px-5 text-center">
        <div>
          <div className="hero-badge mb-5 inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/5 px-3 py-1.5 text-xs text-muted sm:mb-6 sm:px-4 sm:text-sm">
            Компьютерные клубы в Пятигорске
          </div>
        </div>

        <h1 className="hero-title mb-5 sm:mb-6">
          <span className="hero-word gradient-text flex items-end justify-center gap-[0.12em] whitespace-nowrap text-[clamp(2.75rem,12vw,7rem)] leading-[0.92] font-black tracking-[-0.04em]">
            <span className="font-[family-name:var(--font-russo-one)]">МИРАЖ</span>
            <span className="mb-[0.14em] text-[0.24em] leading-none tracking-[0.08em] font-[family-name:var(--font-black-ops)]">
              PRO
            </span>
          </span>
          <span className="hero-subtitle mt-3 block text-lg font-black tracking-wide text-white/90 sm:text-3xl md:text-4xl font-[family-name:var(--font-black-ops)]">
            КОМПЬЮТЕРНЫЕ КЛУБЫ В ПЯТИГОРСКЕ
          </span>
        </h1>

        <p className="hero-copy gradient-text mx-auto mb-3 max-w-2xl text-sm font-bold uppercase leading-relaxed tracking-wide sm:mb-4 sm:text-lg">
          2 клуба — 1 семья
        </p>
        <p className="hero-lead mx-auto mb-7 max-w-2xl text-sm leading-relaxed text-white/70 sm:mb-8 sm:text-base">
          Два компьютерных клуба в Пятигорске: Мираж PRO на Панагюриште, 4 и Мираж на Октябрьской, 46.
          Игровые ПК, PS5 и работа 24/7.
        </p>

        <div className="hero-actions mx-auto flex w-full max-w-sm flex-col items-stretch justify-center gap-3 sm:max-w-none sm:flex-row sm:items-center sm:gap-4">
          <a
            href="https://t.me/mirage_cyber26"
            target="_blank"
            rel="noopener noreferrer"
            className="motion-cta motion-cta-primary rounded-xl gradient-bg px-8 py-4 text-center text-base font-bold text-black sm:px-10 sm:text-lg"
          >
            Забронировать
          </a>
        </div>

        <div className="hero-stats mt-10 grid grid-cols-3 items-start gap-3 text-center sm:mt-16 sm:flex sm:flex-wrap sm:items-center sm:justify-center sm:gap-14">
          <div className="hero-stat">
            <div className="text-2xl sm:text-4xl font-bold gradient-text">
              {totalPC}
            </div>
            <div className="mt-1 text-xs text-muted sm:text-sm">игровых ПК</div>
          </div>
          <div className="hero-stat-divider hidden h-12 w-px bg-white/10 sm:block" />
          <div className="hero-stat">
            <div className="text-2xl sm:text-4xl font-bold gradient-text">
              {totalPS}
            </div>
            <div className="mt-1 text-xs text-muted sm:text-sm">Консолей PS5</div>
          </div>
          <div className="hero-stat-divider hidden h-12 w-px bg-white/10 sm:block" />
          <div className="hero-stat">
            <div className="text-2xl sm:text-4xl font-bold gradient-text">
              24/7
            </div>
            <div className="mt-1 text-xs text-muted sm:text-sm">Круглосуточно</div>
          </div>
        </div>
      </div>
    </section>
  )
}
