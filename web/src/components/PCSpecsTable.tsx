import type { ClubZone } from "@/data/clubs"

interface PCSpecsTableProps {
  zones: ClubZone[]
  showTitle?: boolean
}

const specLabels: Record<string, string> = {
  cpu: "Процессор",
  gpu: "Видеокарта",
  ram: "Оперативная память",
  storage: "Накопитель",
  monitor: "Монитор",
  keyboard: "Клавиатура",
  mouse: "Мышь",
  headset: "Гарнитура",
  chair: "Кресло",
  internet: "Интернет",
}

export function PCSpecsTable({ zones, showTitle = true }: PCSpecsTableProps) {
  const pcZones = zones.filter(z => z.cpu)
  if (pcZones.length === 0) return null

  const specs = ["cpu", "gpu", "ram", "storage", "monitor", "keyboard", "mouse", "headset", "chair"] as const

  return (
    <div className="glass rounded-2xl p-4 sm:p-6 lg:p-8 overflow-hidden">
      {showTitle && (
        <h3 className="text-xl font-bold mb-6">
          Конфигурация <span className="gradient-text">ПК</span>
        </h3>
      )}

      <div className="-mx-4 overflow-x-auto overscroll-x-contain sm:mx-0">
        <table className="w-full text-xs sm:text-sm">
          <thead>
            <tr className="border-b border-white/10">
              <th className="text-left py-3 px-3 sm:px-4 text-muted font-medium whitespace-nowrap">Оборудование</th>
              {pcZones.map((zone) => (
                <th
                  key={zone.name}
                  className="text-left py-3 px-3 sm:px-4 text-muted font-medium whitespace-nowrap"
                >
                  <div className="flex flex-col">
                    <span>{zone.name}</span>
                    <span className="text-xs text-white/40">{zone.specs}</span>
                  </div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {specs.map((spec) => {
              const values = pcZones.map((z) => z[spec as keyof ClubZone])
              if (values.every((v) => !v)) return null

              return (
                <tr
                  key={spec}
                  className="border-b border-white/5 hover:bg-white/[0.02] transition-colors"
                >
                  <td className="py-2.5 px-3 sm:px-4 font-medium text-muted whitespace-nowrap">
                    {specLabels[spec]}
                  </td>
                  {values.map((v, i) => (
                    <td key={i} className="py-2.5 px-3 sm:px-4 whitespace-nowrap text-white/90">
                      {v || "—"}
                    </td>
                  ))}
                </tr>
              )
            })}
            <tr className="border-b border-white/5">
              <td className="py-2.5 px-3 sm:px-4 font-medium text-muted whitespace-nowrap">
                Интернет
              </td>
              {pcZones.map((_, i) => (
                <td key={i} className="py-2.5 px-3 sm:px-4 whitespace-nowrap text-white/90">
                  &gt;1 Гбит/с
                </td>
              ))}
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  )
}
