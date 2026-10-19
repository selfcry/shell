export interface Bonus {
  title: string
  description: string
  icon: string
  color: string
  type: "earn" | "spend" | "special"
}

export const bonuses: Bonus[] = [
  {
    title: "День рождения",
    description: "x2 депозита в твой день рождения — пополняй и получай вдвое больше бонусов",
    icon: "🎂",
    color: "#ff6b00",
    type: "special",
  },
  {
    title: "Отзыв на Картах",
    description: "Оставь отзыв на Яндекс Картах или 2GIS и получи 50 бонусных рублей на счёт",
    icon: "⭐",
    color: "#ff8f00",
    type: "earn",
  },
  {
    title: "Клубный аккаунт",
    description: "Регистрируйся в клубной системе, копи бонусы, получай скидки и следи за историей игр",
    icon: "💳",
    color: "#ff6b00",
    type: "spend",
  },
  {
    title: "Ночные тарифы",
    description: "Специальные цены с 22:00 до 8:00 — играй всю ночь с максимальной выгодой",
    icon: "🌙",
    color: "#ff8f00",
    type: "special",
  },
]
