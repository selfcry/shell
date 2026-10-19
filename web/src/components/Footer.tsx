import Image from "next/image"
import Link from "next/link"
import { clubs } from "@/data/clubs"

function telHref(phone: string) {
  return `tel:+${phone.replace(/\D/g, "")}`
}

export function Footer() {
  const miragePro = clubs.find((c) => c.id === "mirage-pro")!
  const mirage = clubs.find((c) => c.id === "mirage")!

  const footerLinks: Array<{
    title: string
    links: Array<{ href: string; label: string; sublabel?: string; external?: boolean }>
  }> = [
    {
      title: "Навигация",
      links: [
        { href: "/", label: "Главная" },
        { href: "/clubs/mirage-pro", label: "Мираж PRO" },
        { href: "/clubs/mirage", label: "Мираж" },
        { href: "/prices", label: "Цены" },
        { href: "/blog", label: "Статьи" },
        { href: "/faq", label: "Вопросы" },
      ],
    },
    {
      title: "Мираж PRO",
      links: [
        { href: "/clubs/mirage-pro", label: "Страница клуба", sublabel: miragePro.address },
        { href: telHref(miragePro.phone), label: miragePro.phone, sublabel: "Телефон" },
        { href: `mailto:${miragePro.email}`, label: miragePro.email, sublabel: "Почта" },
      ],
    },
    {
      title: "Мираж",
      links: [
        { href: "/clubs/mirage", label: "Страница клуба", sublabel: mirage.address },
        { href: telHref(mirage.phone), label: mirage.phone, sublabel: "Телефон" },
        { href: `mailto:${mirage.email}`, label: mirage.email, sublabel: "Почта" },
      ],
    },
    {
      title: "Мы в соцсетях",
      links: [
        { href: "https://t.me/mirage_cyber26", label: "Telegram", external: true },
        { href: "https://vk.com/club229615472", label: "VK", external: true },
        { href: "https://www.instagram.com/mirage_ptg/", label: "Instagram", external: true },
        { href: "https://www.tiktok.com/@miragepro_pyatigorsk", label: "TikTok", external: true },
      ],
    },
  ]
  return (
    <footer className="relative border-t border-white/5 bg-background">
      <div className="absolute inset-0 bg-gradient-to-b from-transparent via-orange-500/5 to-transparent pointer-events-none" />
      <div className="relative mx-auto max-w-7xl px-4 py-12 sm:py-16">
        <div className="grid grid-cols-1 gap-9 sm:grid-cols-2 sm:gap-10 lg:grid-cols-4">
          {footerLinks.map((group) => (
            <div key={group.title} className="min-w-0">
              <h3 className="text-lg font-semibold text-muted uppercase tracking-wider mb-5">
                {group.title}
              </h3>
              <ul className="space-y-3">
                {group.links.map((link) => (
                  <li key={link.label}>
                    {link.external ? (
                      <a
                        href={link.href}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="text-sm text-muted transition-colors hover:text-white sm:text-base"
                      >
                        {link.label}
                      </a>
                    ) : (
                      <div>
                        <Link
                          href={link.href}
                          className="break-all text-sm text-muted transition-colors hover:text-white sm:text-base"
                        >
                          {link.label}
                        </Link>
                        {link.sublabel && (
                          <span className="block text-xs text-muted/50 mt-0.5">{link.sublabel}</span>
                        )}
                      </div>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="mt-10 flex flex-col items-center justify-between gap-4 border-t border-white/5 pt-7 text-center md:mt-12 md:flex-row md:pt-8">
          <div className="flex items-center gap-2">
            <Image
              src="/logo-icon.webp"
              alt="Мираж PRO"
              width={24}
              height={24}
              className="w-6 h-6 rounded object-cover"
            />
            <span className="text-base text-muted">
              Мираж PRO © {new Date().getFullYear()}
            </span>
          </div>
          <p className="text-xs text-muted/60 text-center">
            Пятигорск, Октябрьская ул., 46 / ул. Панагюриште, 4
          </p>
          <a
            href="https://yuro.pw"
            target="_blank"
            rel="noopener noreferrer"
            className="group inline-flex max-w-full flex-wrap items-center justify-center gap-2 rounded-full border border-white/10 bg-white/[0.04] px-4 py-2 text-sm text-muted transition-all hover:-translate-y-0.5 hover:border-orange-500/40 hover:bg-orange-500/10 hover:text-white"
          >
            <span>Сайт создали в</span>
            <span className="gradient-text font-bold transition-transform group-hover:scale-105">
              yuro.pw
            </span>
          </a>
        </div>
      </div>
      {/* Honeypot: invisible to sighted users and screen readers, unreachable by keyboard.
          Disallowed in robots.txt, so only a bot that ignores it and blindly follows every
          <a href> ends up here — see src/app/api/trap/route.ts */}
      <a
        href="/api/trap"
        aria-hidden="true"
        tabIndex={-1}
        style={{ position: "absolute", width: 1, height: 1, overflow: "hidden", clip: "rect(0,0,0,0)" }}
      >
        admin
      </a>
      <a
        href="/api/trap?src=wp-login"
        aria-hidden="true"
        tabIndex={-1}
        style={{ position: "absolute", width: 1, height: 1, overflow: "hidden", clip: "rect(0,0,0,0)" }}
      >
        wp-login
      </a>
    </footer>
  )
}
