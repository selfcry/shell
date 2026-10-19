import type { Metadata } from "next"
import Link from "next/link"
import { clubs } from "@/data/clubs"
import { ParticleBackground } from "@/components/ParticleBackground"
import { SITE_NAME, SITE_URL } from "@/lib/site"

const path = "/kiberklub-pyatigorsk"
const title = "Киберклуб в Пятигорске — Мираж PRO и Мираж"
const description =
  "Киберклуб в Пятигорске: сеть Мираж — 2 локации, 79 ПК, 7 PS5, интернет >1 Гбит/с, круглосуточно. Адреса, цены, как добраться и забронировать."

export const metadata: Metadata = {
  title: { absolute: `${title} | ${SITE_NAME}` },
  description,
  alternates: { canonical: `${SITE_URL}${path}` },
  openGraph: {
    title,
    description,
    url: `${SITE_URL}${path}`,
    type: "article",
  },
}

const totalPC = clubs.reduce((sum, c) => sum + c.totalPC, 0)
const totalPS = clubs.reduce((sum, c) => sum + c.totalPS, 0)

export default function KiberklubLandingPage() {
  return (
    <>
      <ParticleBackground />
      <article className="relative z-10 mx-auto max-w-3xl px-3 pb-16 pt-24 sm:px-4 sm:pb-20 sm:pt-28">
        <nav className="mb-6 text-xs text-muted sm:text-sm" aria-label="Хлебные крошки">
          <Link href="/" className="hover:text-white">
            Главная
          </Link>
          <span className="mx-2">/</span>
          <span className="text-white/70">Киберклуб Пятигорск</span>
        </nav>

        <h1 className="mb-5 text-3xl font-black sm:text-5xl">
          Киберклуб в <span className="gradient-text">Пятигорске</span>
        </h1>
        <p className="mb-8 text-base leading-relaxed text-muted sm:text-lg">
          Ищете киберклуб в Пятигорске с современными ПК, стабильным пингом и нормальной
          атмосферой для соло и пати? Сеть Мираж PRO — два клуба в городе: флагман на
          Панагюриште и камерный Мираж в центре. Вместе — {totalPC} игровых ПК и{" "}
          {totalPS} консолей PlayStation 5, режим 24/7.
        </p>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-white/80 sm:text-base">
          <h2 className="text-2xl font-bold text-white">Что такое киберклуб Мираж</h2>
          <p>
            Киберклуб — это не просто «зало с компьютерами». Здесь важны железо под
            актуальные шутеры и ММО, мониторы с высокой частотой, бездисковая или
            гибридная система загрузки, клубные аккаунты и удобный режим работы. В сети
            Мираж оба клуба работают{" "}
            <strong className="text-white">круглосуточно без выходных</strong>, так что
            можно зайти после учёбы, ночной смены или собраться компанией на ночь.
          </p>
          <p>
            Если вы сравниваете запросы «киберклуб», «компьютерный клуб» и «игровой
            клуб» — по сути это одна потребность: место, где можно комфортно играть без
            покупки топового ПК дома. У нас закрыты оба сценария: максимальная мощность на
            флагмане и спокойный формат ближе к центру.
          </p>
          <ul className="list-disc space-y-2 pl-5 text-muted">
            <li>{totalPC} игровых ПК на двух локациях</li>
            <li>{totalPS} консолей PlayStation 5</li>
            <li>Интернет свыше 1 Гбит/с</li>
            <li>Ночные тарифы с 22:00 до 8:00</li>
            <li>Бронирование по телефону и в Telegram</li>
          </ul>
        </section>

        <section className="mb-10 grid gap-4 sm:grid-cols-2">
          {clubs.map((club) => (
            <div key={club.id} className="glass rounded-3xl p-6">
              <h2 className="mb-2 text-xl font-bold">{club.name}</h2>
              <p className="mb-3 text-sm text-muted">{club.address}</p>
              <p className="mb-4 text-sm leading-relaxed text-white/75">
                {club.totalPC} ПК · {club.totalPS} PS5 · ★ {club.rating.value.toFixed(1)} (
                {club.rating.count} оценок на {club.rating.source})
              </p>
              <a
                href={`tel:+${club.phone.replace(/\D/g, "")}`}
                className="mb-4 block text-sm font-semibold gradient-text"
              >
                {club.phone}
              </a>
              <Link
                href={`/clubs/${club.id}`}
                className="inline-flex rounded-xl gradient-bg px-4 py-2.5 text-sm font-bold text-black"
              >
                Страница клуба
              </Link>
            </div>
          ))}
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Какой киберклуб выбрать</h2>
          <p>
            <strong className="text-white">Мираж PRO</strong> на ул. Панагюриште, 4 —
            флагман сети: 51 ПК, зоны BOOTCAMP и DUO VIP, мониторы до 400 Гц, RTX 5060 /
            5070. Подходит для тренировок, больших пати и тех, кто хочет максимум FPS и
            свободных мест. Ориентир — рядом с Фармацевтической академией.
          </p>
          <p>
            <strong className="text-white">Мираж</strong> на Октябрьской ул., 46 — 28 ПК на
            RTX 3070, VIP-зоны и 3 PS5 в центре города. Удобно зайти после работы или
            учёбы, поиграть вдвоём или небольшой компанией в спокойном формате.
          </p>
          <p>
            Не уверены? Смотрите{" "}
            <Link
              href="/mirage-vs-mirage-pro"
              className="text-white underline-offset-2 hover:underline"
            >
              сравнение Мираж и Мираж PRO
            </Link>{" "}
            или общую подборку{" "}
            <Link
              href="/kompyuternyy-klub-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              компьютерных клубов в Пятигорске
            </Link>
            .
          </p>
        </section>

        <section className="mb-10 space-y-4 text-sm leading-relaxed text-muted sm:text-base">
          <h2 className="text-2xl font-bold text-white">Цены, ночь и бронирование</h2>
          <p>
            Актуальные пакеты на 1, 3 и 5 часов, а также ночные тарифы — на странице{" "}
            <Link href="/prices" className="text-white underline-offset-2 hover:underline">
              цен
            </Link>
            . Отдельный разбор ночного формата — на лендинге{" "}
            <Link
              href="/nochnoy-tarif-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              ночной тариф в Пятигорске
            </Link>
            .
          </p>
          <p>
            Забронировать ПК или PS5 можно по телефону клуба или через{" "}
            <Link
              href="/bronirovanie"
              className="text-white underline-offset-2 hover:underline"
            >
              инструкцию по бронированию
            </Link>
            . Частые вопросы — в{" "}
            <Link href="/faq" className="text-white underline-offset-2 hover:underline">
              FAQ
            </Link>
            . Контакты обоих клубов собраны на странице{" "}
            <Link href="/kontakty" className="text-white underline-offset-2 hover:underline">
              контактов
            </Link>
            .
          </p>
          <p>
            Хотите именно консоль? Смотрите{" "}
            <Link
              href="/ps5-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              PlayStation 5 в Пятигорске
            </Link>
            . Ищете формулировку «игровой клуб» — есть отдельная страница{" "}
            <Link
              href="/igrovoy-klub-pyatigorsk"
              className="text-white underline-offset-2 hover:underline"
            >
              игрового клуба в Пятигорске
            </Link>
            .
          </p>
        </section>

        <section className="glass rounded-3xl p-6 sm:p-8">
          <h2 className="mb-4 text-xl font-bold">Адреса киберклубов (NAP)</h2>
          <ul className="space-y-4 text-sm text-muted sm:text-base">
            {clubs.map((club) => (
              <li key={club.id}>
                <strong className="text-white">{club.fullName}</strong>
                <br />
                {club.address}, {club.postalCode}
                <br />
                <a href={`tel:+${club.phone.replace(/\D/g, "")}`} className="gradient-text">
                  {club.phone}
                </a>
                {" · "}
                <a href={`mailto:${club.email}`} className="text-white/80 hover:text-white">
                  {club.email}
                </a>
                <br />
                <a
                  href={club.mapUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-white underline-offset-2 hover:underline"
                >
                  Открыть на Яндекс Картах
                </a>
              </li>
            ))}
          </ul>
        </section>
      </article>
    </>
  )
}
