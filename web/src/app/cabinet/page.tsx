import type { Metadata } from "next"
import { Cabinet } from "./Cabinet"

export const metadata: Metadata = {
  title: "Личный кабинет",
  description: "Баланс, бонусы, бронь компьютера и история операций в клубах Мираж и Мираж PRO.",
  robots: { index: false, follow: false },
}

export default function CabinetPage() {
  return (
    <section className="relative px-4 pb-20 pt-28 sm:pt-32">
      <Cabinet />
    </section>
  )
}
