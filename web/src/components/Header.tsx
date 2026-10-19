"use client"

import Image from "next/image"
import Link from "next/link"
import { usePathname } from "next/navigation"
import { useEffect, useState } from "react"

export function Header() {
  const pathname = usePathname()
  const [isOpen, setIsOpen] = useState(false)

  const navLinks = [
    { href: "/", label: "Главная" },
    { href: "/clubs/mirage-pro", label: "Мираж PRO" },
    { href: "/clubs/mirage", label: "Мираж" },
    { href: "/prices", label: "Цены" },
    { href: "/blog", label: "Статьи" },
    { href: "/faq", label: "Вопросы" },
    { href: "/cabinet", label: "Кабинет" },
  ]

  useEffect(() => {
    if (!isOpen) return
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setIsOpen(false)
    }
    document.addEventListener("keydown", handleKeyDown)
    document.body.style.overflow = "hidden"
    return () => {
      document.removeEventListener("keydown", handleKeyDown)
      document.body.style.overflow = ""
    }
  }, [isOpen])

  return (
    <header className="header-motion fixed left-0 right-0 top-0 z-50 p-2 sm:p-3">
      <div className="header-shell liquid-glass-nav relative max-w-5xl mx-auto px-2 h-14 flex items-center justify-between rounded-2xl">
        <div className="liquid-glass-shine" />

        <Link href="/" className="group relative z-10 flex min-w-0 items-center gap-2 pl-2 sm:gap-2.5 sm:pl-3">
          <Image
            src="/logo-icon.webp"
            alt="Мираж PRO"
            width={32}
            height={32}
            className="w-8 h-8 rounded-xl object-cover ring-1 ring-white/10"
            priority
          />
          <span className="truncate text-base font-bold tracking-tight sm:text-xl">
            <span className="gradient-text">Мираж PRO</span>
          </span>
        </Link>

        <nav className="hidden lg:flex items-center gap-1 relative z-10">
          {navLinks.map((link) => {
            const isActive =
              pathname === link.href ||
              (link.href !== "/" &&
                !link.href.startsWith("/clubs/") &&
                pathname.startsWith(`${link.href}/`)) ||
              (link.href.startsWith("/clubs/") && pathname === link.href)
            return (
              <Link
                key={link.href}
                href={link.href}
                aria-current={isActive ? "page" : undefined}
                className={`motion-nav-link relative px-4 py-2 rounded-xl text-base font-medium transition-all duration-300 ${
                  isActive
                    ? "text-white"
                    : "text-white/50 hover:text-white/80"
                }`}
              >
                {isActive && (
                  <div className="nav-active-pill absolute inset-0 rounded-xl liquid-glass-pill" />
                )}
                <span className="relative z-10">{link.label}</span>
              </Link>
            )
          })}
          <Link
            href="/cabinet"
            className="motion-header-cta ml-3 px-5 py-2 rounded-xl gradient-bg text-black font-semibold text-sm relative z-10"
          >
            Личный кабинет
          </Link>
        </nav>

        <button
          type="button"
          onClick={() => setIsOpen(!isOpen)}
          className="relative z-50 flex h-11 w-11 shrink-0 flex-col items-center justify-center gap-1.5 rounded-xl liquid-glass-mini lg:hidden"
          aria-label={isOpen ? "Закрыть меню" : "Открыть меню"}
          aria-expanded={isOpen}
          aria-controls="mobile-navigation"
        >
          <span
            className={`w-5 h-0.5 bg-white rounded-full transition-transform ${
              isOpen ? "translate-y-2 rotate-45" : ""
            }`}
          />
          <span
            className={`w-5 h-0.5 bg-white rounded-full transition-opacity ${
              isOpen ? "opacity-0" : ""
            }`}
          />
          <span
            className={`w-5 h-0.5 bg-white rounded-full transition-transform ${
              isOpen ? "-translate-y-2 -rotate-45" : ""
            }`}
          />
        </button>
      </div>

      {isOpen && (
          <div className="fixed inset-x-2 top-[4.75rem] z-40 sm:inset-x-3 sm:top-20 lg:hidden">
            <button
              type="button"
              aria-label="Закрыть меню"
              className="fixed inset-0 -z-10 cursor-default"
              onClick={() => setIsOpen(false)}
            />
            <div
              id="mobile-navigation"
              className="mobile-menu-motion liquid-glass-panel rounded-3xl p-6 mx-auto max-w-5xl"
            >
              <div className="liquid-glass-shine" />
              <nav className="flex flex-col items-center gap-2 relative z-10">
                {navLinks.map((link) => {
                  const isActive =
                    pathname === link.href ||
                    (link.href !== "/" &&
                      !link.href.startsWith("/clubs/") &&
                      pathname.startsWith(`${link.href}/`)) ||
                    (link.href.startsWith("/clubs/") && pathname === link.href)
                  return (
                    <Link
                      key={link.href}
                      href={link.href}
                      onClick={() => setIsOpen(false)}
                      aria-current={isActive ? "page" : undefined}
                      className={`w-full text-center py-3.5 rounded-2xl text-lg font-semibold transition-all duration-300 ${
                        isActive
                          ? "text-white liquid-glass-pill"
                          : "text-white/50 hover:text-white hover:bg-white/5"
                      }`}
                    >
                      {link.label}
                    </Link>
                  )
                })}
                <div className="w-full h-px bg-white/10 my-2" />
                <Link
                  href="/clubs/mirage-pro"
                  onClick={() => setIsOpen(false)}
                  className="w-full text-center py-3.5 rounded-2xl gradient-bg text-black font-bold text-lg hover:opacity-90 transition-opacity"
                >
                  Открыть Мираж PRO
                </Link>
              </nav>
            </div>
          </div>
      )}
    </header>
  )
}
