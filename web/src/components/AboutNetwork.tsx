import Link from "next/link"
import { clubs } from "@/data/clubs"

export function AboutNetwork() {
  return (
    <section className="relative px-3 py-16 sm:px-4 sm:py-24" aria-labelledby="about-network-title">
      <div className="mx-auto max-w-7xl">
        <div className="mb-10 text-center sm:mb-12">
          <h2 id="about-network-title" className="mb-4 text-3xl font-black sm:text-6xl">
            О сети <span className="gradient-text">Мираж</span>
          </h2>
          <p className="mx-auto max-w-3xl text-base leading-relaxed text-muted sm:text-xl">
            Мираж PRO — сеть компьютерных клубов в Пятигорске. Две локации работают
            круглосуточно: флагман Мираж PRO и уютный Мираж в центре города. Игровые ПК,
            PlayStation 5, быстрый интернет и прозрачные тарифы.
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-2 sm:gap-6">
          {clubs.map((club) => (
            <article
              key={club.id}
              className="glass rounded-3xl p-6 sm:p-8"
            >
              <h3 className="mb-2 text-2xl font-bold">{club.name}</h3>
              <p className="mb-5 text-sm leading-relaxed text-muted sm:text-base">
                {club.description}
              </p>
              <dl className="space-y-3 text-sm sm:text-base">
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted/70">Адрес</dt>
                  <dd className="mt-1 text-white/90">
                    {club.address}, {club.postalCode}
                  </dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted/70">Рейтинг</dt>
                  <dd className="mt-1 text-white/90">
                    ★ {club.rating.value.toFixed(1)} · {club.rating.count} оценок ({club.rating.source})
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
                  <dt className="text-xs uppercase tracking-wider text-muted/70">Режим</dt>
                  <dd className="mt-1 text-green-400">{club.workTime}</dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted/70">Оснащение</dt>
                  <dd className="mt-1 text-white/90">
                    {club.totalPC} ПК · {club.totalPS} PS5
                  </dd>
                </div>
              </dl>
              <div className="mt-6 flex flex-wrap gap-3">
                <Link
                  href={`/clubs/${club.id}`}
                  className="inline-flex min-h-11 items-center rounded-xl gradient-bg px-5 py-2.5 text-sm font-bold text-black"
                >
                  Подробнее
                </Link>
                <a
                  href={club.mapUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex min-h-11 items-center rounded-xl border border-white/10 px-5 py-2.5 text-sm font-medium text-white transition-colors hover:bg-white/5"
                >
                  На карте
                </a>
              </div>
            </article>
          ))}
        </div>
      </div>
    </section>
  )
}
