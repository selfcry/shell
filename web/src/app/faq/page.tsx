import Link from "next/link"
import { faqData } from "@/data/faq"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"

const miragePro = clubs.find((c) => c.id === "mirage-pro")!

export default function FAQPage() {
  return (
    <>
      <ParticleBackground />
      <div className="relative z-10 pb-12 pt-20 sm:pb-16 sm:pt-24">
        <div
          className="absolute inset-0 bg-cover bg-center opacity-10"
          style={{ backgroundImage: "url('/bg-blocks/3.webp')" }}
        />
        <div className="relative z-10 mx-auto max-w-4xl px-3 sm:px-4">
          <div className="text-center mb-12">
            <h1 className="mb-4 text-4xl font-black sm:text-7xl">
              Вопросы о компьютерных клубах в{" "}
              <span className="gradient-text">Пятигорске</span>
            </h1>
            <p className="text-muted text-lg">
              Всё о посещении Мираж PRO (Панагюриште, 4) и Мираж (Октябрьская, 46)
            </p>
            <div className="mt-5 flex flex-wrap items-center justify-center gap-3 text-sm">
              <Link
                href="/clubs/mirage-pro"
                className="rounded-xl border border-white/10 px-4 py-2 text-muted transition-colors hover:text-white"
              >
                Мираж PRO
              </Link>
              <Link
                href="/clubs/mirage"
                className="rounded-xl border border-white/10 px-4 py-2 text-muted transition-colors hover:text-white"
              >
                Мираж
              </Link>
              <Link
                href="/prices"
                className="rounded-xl border border-white/10 px-4 py-2 text-muted transition-colors hover:text-white"
              >
                Цены
              </Link>
            </div>
          </div>

          <div className="space-y-3">
            {faqData.map((item) => (
              <details
                key={item.question}
                className="group glass rounded-3xl transition-colors open:bg-white/[0.06]"
              >
                <summary className="flex min-h-16 cursor-pointer list-none items-center justify-between gap-3 p-5 font-medium focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-orange-500 sm:min-h-20 sm:gap-4 sm:p-9">
                  <span className="text-sm sm:text-lg">{item.question}</span>
                  <span
                    aria-hidden="true"
                    className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full gradient-bg text-sm font-bold text-black transition-transform group-open:rotate-45"
                  >
                    +
                  </span>
                </summary>
                <p className="px-5 pb-3 text-sm leading-relaxed text-muted sm:px-9 sm:text-base">
                  {item.answer}
                </p>
                {item.links && item.links.length > 0 && (
                  <div className="flex flex-wrap gap-2 px-5 pb-5 sm:px-9 sm:pb-9">
                    {item.links.map((link) => (
                      <Link
                        key={link.href}
                        href={link.href}
                        className="rounded-lg border border-white/10 px-3 py-1.5 text-xs text-white/80 transition-colors hover:bg-white/5 hover:text-white sm:text-sm"
                      >
                        {link.label}
                      </Link>
                    ))}
                  </div>
                )}
              </details>
            ))}
          </div>

          <div className="mt-10 rounded-3xl p-5 text-center glass sm:mt-12 sm:p-8">
            <p className="text-muted mb-4">
              Остались вопросы? Свяжись с нами напрямую
            </p>
            <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
              <a
                href="https://t.me/mirage_cyber26"
                target="_blank"
                rel="noopener noreferrer"
                className="w-full sm:w-auto px-8 py-4 rounded-xl gradient-bg text-black font-bold text-lg hover:opacity-90 transition-all"
              >
                Написать в Telegram
              </a>
              <a
                href={`tel:+${miragePro.phone.replace(/\D/g, "")}`}
                className="w-full sm:w-auto px-8 py-4 rounded-xl border border-white/10 text-white font-bold text-lg hover:bg-white/5 transition-all"
              >
                Позвонить
              </a>
              <a
                href="https://vk.com/club229615472"
                target="_blank"
                rel="noopener noreferrer"
                className="px-6 py-3 rounded-xl border border-white/10 text-white font-medium text-sm hover:bg-white/5 transition-all"
              >
                VK
              </a>
            </div>
          </div>
        </div>
      </div>
    </>
  )
}
