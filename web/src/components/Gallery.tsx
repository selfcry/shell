"use client"

import Image from "next/image"
import { useState, useCallback, useEffect, useRef } from "react"
import { createPortal } from "react-dom"

interface GalleryProps {
  images: string[]
  clubName: string
}

export function Gallery({ images, clubName }: GalleryProps) {
  const [selected, setSelected] = useState<number | null>(null)
  const closeButtonRef = useRef<HTMLButtonElement>(null)
  const previousFocusRef = useRef<HTMLElement | null>(null)

  const close = useCallback(() => setSelected(null), [])
  const open = useCallback((index: number) => {
    previousFocusRef.current = document.activeElement as HTMLElement | null
    setSelected(index)
  }, [])

  useEffect(() => {
    if (selected === null) return
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") close()
      if (e.key === "ArrowRight") setSelected((s) => s !== null ? Math.min(s + 1, images.length - 1) : null)
      if (e.key === "ArrowLeft") setSelected((s) => s !== null ? Math.max(s - 1, 0) : null)
      if (e.key === "Tab") {
        const dialog = closeButtonRef.current?.closest('[role="dialog"]')
        const controls = dialog?.querySelectorAll<HTMLElement>(
          'button:not([disabled]), [href], [tabindex]:not([tabindex="-1"])'
        )
        if (!controls?.length) return
        const first = controls[0]
        const last = controls[controls.length - 1]
        if (e.shiftKey && document.activeElement === first) {
          e.preventDefault()
          last.focus()
        } else if (!e.shiftKey && document.activeElement === last) {
          e.preventDefault()
          first.focus()
        }
      }
    }
    document.addEventListener("keydown", handler)
    document.body.style.overflow = "hidden"
    requestAnimationFrame(() => closeButtonRef.current?.focus())
    return () => {
      document.removeEventListener("keydown", handler)
      document.body.style.overflow = ""
      previousFocusRef.current?.focus()
    }
  }, [selected, close, images.length])

  if (!images.length) return null

  return (
    <>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4 sm:gap-3">
        {images.map((src, i) => (
          <button
            key={src}
            onClick={() => open(i)}
            className={`relative overflow-hidden rounded-xl group cursor-pointer ${
              i === 0 ? "col-span-2 row-span-2" : ""
            }`}
          >
            <div className="aspect-[4/3]">
              <Image
                src={src}
                alt={`${clubName} — компьютерный клуб в Пятигорске, фото ${i + 1}`}
                fill
                sizes={i === 0 ? "(max-width: 640px) 100vw, 50vw" : "(max-width: 640px) 50vw, 25vw"}
                unoptimized
                className="object-cover transition-transform duration-500 group-hover:scale-110"
              />
            </div>
            <div className="absolute inset-0 bg-black/0 group-hover:bg-black/30 transition-colors duration-300" />
            <div className="absolute inset-0 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity duration-300">
              <span className="text-white text-sm font-medium bg-black/50 px-3 py-1.5 rounded-full backdrop-blur-sm">
                {i === 0 && images.length > 1 ? `+${images.length - 1} фото` : "Увеличить"}
              </span>
            </div>
          </button>
        ))}
      </div>

      {typeof document !== "undefined" &&
        createPortal(
          <>
            {selected !== null && (
              <div
                role="dialog"
                aria-modal="true"
                aria-label={`Фотогалерея ${clubName}`}
                onClick={close}
                className="fixed inset-0 z-[200] flex items-center justify-center bg-black/95 p-2 pb-20 backdrop-blur-xl sm:p-4"
              >
                <button
                  ref={closeButtonRef}
                  type="button"
                  onClick={close}
                  aria-label="Закрыть фотогалерею"
                  className="absolute right-3 top-3 z-10 flex h-11 w-11 items-center justify-center rounded-full bg-white/10 text-xl text-white transition-colors hover:bg-white/20 sm:right-4 sm:top-4"
                >
                  ✕
                </button>

                {images.length > 1 && (
                  <>
                    <button
                      type="button"
                      disabled={selected === 0}
                      aria-label="Предыдущее фото"
                      onClick={(event) => {
                        event.stopPropagation()
                        setSelected((current) =>
                          current !== null ? Math.max(current - 1, 0) : null
                        )
                      }}
                      className="absolute bottom-4 left-4 z-10 flex h-12 w-12 items-center justify-center rounded-full bg-white/10 text-xl text-white transition-colors hover:bg-white/20 disabled:cursor-not-allowed disabled:opacity-30 sm:bottom-auto sm:top-1/2 sm:h-11 sm:w-11 sm:-translate-y-1/2 sm:text-base"
                    >
                      ‹
                    </button>
                    <button
                      type="button"
                      disabled={selected === images.length - 1}
                      aria-label="Следующее фото"
                      onClick={(event) => {
                        event.stopPropagation()
                        setSelected((current) =>
                          current !== null
                            ? Math.min(current + 1, images.length - 1)
                            : 0
                        )
                      }}
                      className="absolute bottom-4 right-4 z-10 flex h-12 w-12 items-center justify-center rounded-full bg-white/10 text-xl text-white transition-colors hover:bg-white/20 disabled:cursor-not-allowed disabled:opacity-30 sm:bottom-auto sm:top-1/2 sm:h-11 sm:w-11 sm:-translate-y-1/2 sm:text-base"
                    >
                      ›
                    </button>
                  </>
                )}

                <div
                  key={selected}
                  onClick={(event) => event.stopPropagation()}
                  className="w-full max-w-5xl"
                >
                  <Image
                    src={images[selected]}
                    alt={`${clubName} — фото ${selected + 1}`}
                    width={1600}
                    height={1200}
                    sizes="100vw"
                    unoptimized
                    className="max-h-[70vh] w-full rounded-xl object-contain sm:max-h-[82vh] sm:rounded-2xl"
                  />
                  <div className="mt-3 text-center text-sm text-muted" aria-live="polite">
                    {selected + 1} / {images.length}
                  </div>
                </div>
              </div>
            )}
          </>,
          document.body
        )}
    </>
  )
}
