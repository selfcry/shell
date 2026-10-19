"use client"

import { useState } from "react"
import { MapSkeleton } from "./Skeleton"

interface MapComponentProps {
  selectedId: string
}

const YANDEX_ORGANIZATION_IDS: Record<string, string> = {
  "mirage-pro": "133093405689",
  mirage: "181089479974",
}

export function MapComponent({ selectedId }: MapComponentProps) {
  const [loaded, setLoaded] = useState(false)
  const [mapError, setMapError] = useState(false)
  const organizationId = YANDEX_ORGANIZATION_IDS[selectedId] ?? YANDEX_ORGANIZATION_IDS["mirage-pro"]
  const mapUrl = `https://yandex.ru/map-widget/v1/?mode=search&oid=${organizationId}&ol=biz&z=16`

  return (
    <div className="map-brand-frame relative h-full min-h-[320px] w-full overflow-hidden sm:min-h-[400px]">
      {mapError && (
        <div className="absolute inset-0 z-20 flex items-center justify-center bg-surface p-6 text-center text-sm text-muted">
          Не удалось загрузить карту. Используйте ссылку «Построить маршрут» ниже.
        </div>
      )}
      <div
        className={`absolute inset-0 z-10 transition-opacity duration-500 pointer-events-none ${loaded ? "opacity-0" : "opacity-100"}`}
      >
        <MapSkeleton />
      </div>
      <iframe
        key={selectedId}
        src={mapUrl}
        title={`Яндекс Карты — ${selectedId === "mirage-pro" ? "Мираж PRO" : "Мираж"}`}
        className="map-brand-iframe h-full min-h-[320px] w-full border-0 sm:min-h-[400px]"
        loading="lazy"
        allowFullScreen
        onLoad={() => setLoaded(true)}
        onError={() => setMapError(true)}
      />
      <div className="map-brand-tint pointer-events-none absolute inset-0 z-[5]" aria-hidden="true" />
      <div className="pointer-events-none absolute bottom-3 left-3 z-[6] rounded-full border border-white/15 bg-[#17152b]/80 px-3 py-1.5 text-xs font-semibold text-white/85 backdrop-blur-md">
        Яндекс Карты
      </div>
    </div>
  )
}
