export function Skeleton({ className = "" }: { className?: string }) {
  return (
    <div
      className={`animate-pulse rounded-lg bg-white/5 ${className}`}
      style={{
        background: "linear-gradient(90deg, rgba(255,255,255,0.03) 25%, rgba(255,255,255,0.08) 50%, rgba(255,255,255,0.03) 75%)",
        backgroundSize: "200% 100%",
        animation: "shimmer 2s ease-in-out infinite",
      }}
    />
  )
}

export function ReviewSkeleton() {
  return (
    <div className="glass rounded-2xl p-6 sm:p-8">
      <div className="flex items-center gap-1 mb-4">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="w-4 h-4 rounded" />
        ))}
        <Skeleton className="w-16 h-3 rounded ml-2" />
      </div>
      <div className="space-y-2 mb-6">
        <Skeleton className="w-full h-3 rounded" />
        <Skeleton className="w-3/4 h-3 rounded" />
        <Skeleton className="w-5/6 h-3 rounded" />
        <Skeleton className="w-1/2 h-3 rounded" />
      </div>
      <div className="flex items-center gap-2">
        <Skeleton className="w-8 h-8 rounded-full" />
        <div className="space-y-1">
          <Skeleton className="w-20 h-3 rounded" />
          <Skeleton className="w-16 h-2 rounded" />
        </div>
      </div>
    </div>
  )
}

export function MapSkeleton() {
  return (
    <div className="relative h-full min-h-[320px] w-full overflow-hidden rounded-xl sm:min-h-[400px]" style={{ background: "#0a0a0a" }}>
      <div className="absolute inset-0 flex items-center justify-center">
        <div className="text-center">
          <div className="w-10 h-10 mx-auto mb-3 rounded-full border-2 border-orange-800/30 border-t-orange-500 animate-spin" />
          <p className="text-xs text-muted">Загрузка карты...</p>
        </div>
      </div>
      <svg className="absolute inset-0 w-full h-full opacity-[0.03]" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse">
            <path d="M 40 0 L 0 0 0 40" fill="none" stroke="#fff" strokeWidth="0.5" />
          </pattern>
        </defs>
        <rect width="100%" height="100%" fill="url(#grid)" />
      </svg>
    </div>
  )
}
