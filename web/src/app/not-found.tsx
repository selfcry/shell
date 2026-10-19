import type { Metadata } from "next"
import Link from "next/link"

export const metadata: Metadata = {
  title: "Страница не найдена",
  robots: { index: false, follow: false },
}

export default function NotFound() {
  return (
    <div className="min-h-dvh flex items-center justify-center px-4">
      <div className="text-center max-w-lg">
        <div className="text-9xl sm:text-[10rem] font-black gradient-text mb-6">
          404
        </div>
        <h1 className="text-3xl sm:text-4xl font-bold mb-4">
          Страница не найдена
        </h1>
        <p className="text-muted text-lg mb-10 leading-relaxed">
          Такой страницы нет или она была перемещена. Попробуй вернуться на главную
        </p>
        <Link
          href="/"
          className="inline-flex px-10 py-4 rounded-xl gradient-bg text-black font-bold text-lg hover:opacity-90 transition-all hover:scale-105 active:scale-95"
        >
          На главную
        </Link>
      </div>
    </div>
  )
}
