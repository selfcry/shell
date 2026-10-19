import { bonuses } from "@/data/bonuses"

const typeLabels: Record<string, string> = {
  earn: "Как получить",
  spend: "Как потратить",
  special: "Особые",
}

const typeColors: Record<string, string> = {
  earn: "bg-green-500/10 text-green-400 border-green-500/20",
  spend: "bg-blue-500/10 text-blue-400 border-blue-500/20",
  special: "bg-orange-500/10 text-orange-400 border-orange-500/20",
}

export function Bonuses() {
  return (
    <section className="relative overflow-hidden px-3 py-16 sm:px-4 sm:py-24">
      <div
        className="absolute inset-0 bg-cover bg-center opacity-10"
        style={{ backgroundImage: "url('/bg-blocks/2.webp')" }}
      />
      <div className="max-w-7xl mx-auto relative z-10">
        <div className="text-center mb-16">
          <h2 className="text-3xl font-black sm:text-6xl mb-4">
            Бонусная <span className="gradient-text">система</span>
          </h2>
          <p className="mx-auto max-w-xl text-base text-muted sm:text-lg">
            Копи бонусы, получай скидки и подарки
          </p>
        </div>

        <div className="bonus-grid grid sm:grid-cols-2 lg:grid-cols-3 gap-6">
          {bonuses.map((bonus) => (
            <div
              key={bonus.title}
            className="bonus-card-motion glass group rounded-3xl p-6 hover:glass-hover sm:p-10"
            >
              <div className="min-w-0">
                  <span
                    className={`bonus-label-motion inline-block text-[10px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded-full border mb-3 ${typeColors[bonus.type]}`}
                  >
                    {typeLabels[bonus.type]}
                  </span>
                  <h3 className="font-bold text-xl mb-2">{bonus.title}</h3>
                  <p className="text-base text-muted leading-relaxed">
                    {bonus.description}
                  </p>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}
