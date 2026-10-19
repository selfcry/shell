import Link from "next/link"

const links = [
  { href: "/kontakty", label: "Контакты клубов" },
  { href: "/bronirovanie", label: "Бронирование ПК" },
  { href: "/kiberklub-pyatigorsk", label: "Киберклуб Пятигорск" },
  { href: "/igrovoy-klub-pyatigorsk", label: "Игровой клуб" },
  { href: "/nochnoy-tarif-pyatigorsk", label: "Ночной тариф" },
  { href: "/kompyuternyy-klub-pyatigorsk", label: "Компьютерный клуб" },
  { href: "/ps5-pyatigorsk", label: "PS5 Пятигорск" },
  { href: "/mirage-vs-mirage-pro", label: "Сравнение клубов" },
  { href: "/prices", label: "Цены" },
  { href: "/faq", label: "Вопросы" },
]

export function SeoHub() {
  return (
    <section
      className="relative px-3 py-12 sm:px-4 sm:py-16"
      aria-labelledby="seo-hub-title"
    >
      <div className="mx-auto max-w-7xl">
        <div className="glass rounded-3xl px-5 py-7 sm:px-8 sm:py-9">
          <h2
            id="seo-hub-title"
            className="mb-2 text-xl font-bold sm:text-2xl"
          >
            Полезное
          </h2>
          <p className="mb-5 max-w-2xl text-sm text-muted sm:text-base">
            Адреса, бронирование, тарифы и подборки по запросам — всё в одном блоке.
          </p>
          <ul className="flex flex-wrap gap-2 sm:gap-3">
            {links.map((link) => (
              <li key={link.href}>
                <Link
                  href={link.href}
                  className="inline-flex rounded-xl border border-white/10 bg-white/[0.03] px-3.5 py-2 text-sm text-white/85 transition-colors hover:border-orange-500/30 hover:bg-orange-500/10 hover:text-white"
                >
                  {link.label}
                </Link>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  )
}
