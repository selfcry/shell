"use client"

import { useState, useEffect, useCallback } from "react"
import { fallbackReviews, type Review } from "@/data/reviews"

export function ReviewsCarousel() {
  const [reviews, setReviews] = useState<Review[]>(fallbackReviews)
  const [current, setCurrent] = useState(0)
  const [direction, setDirection] = useState<1 | -1>(1)
  const [isHovered, setIsHovered] = useState(false)
  const [hasFocus, setHasFocus] = useState(false)
  const [prefersReducedMotion, setPrefersReducedMotion] = useState(false)

  useEffect(() => {
    const query = window.matchMedia("(prefers-reduced-motion: reduce)")
    const update = () => setPrefersReducedMotion(query.matches)
    update()
    query.addEventListener("change", update)
    return () => query.removeEventListener("change", update)
  }, [])

  useEffect(() => {
    const controller = new AbortController()

    fetch("/api/reviews", { signal: controller.signal })
      .then((response) => {
        if (!response.ok) {
          throw new Error(`Не удалось загрузить отзывы: ${response.status}`)
        }
        return response.json()
      })
      .then((data) => {
        if (Array.isArray(data?.reviews) && data.reviews.length > 0) {
          setReviews(data.reviews)
          setCurrent(0)
        }
      })
      .catch(() => {})

    return () => controller.abort()
  }, [])

  const next = useCallback(() => {
    setDirection(1)
    setCurrent((c) => (c + 1) % reviews.length)
  }, [reviews.length])

  const prev = useCallback(() => {
    setDirection(-1)
    setCurrent((c) => (c - 1 + reviews.length) % reviews.length)
  }, [reviews.length])

  useEffect(() => {
    if (reviews.length <= 1 || isHovered || hasFocus || prefersReducedMotion) return
    const timer = setInterval(next, 5000)
    return () => clearInterval(timer)
  }, [hasFocus, isHovered, next, prefersReducedMotion, reviews.length])

  if (reviews.length === 0) {
    return null
  }

  const review = reviews[current]

  return (
    <section
      className="relative overflow-hidden px-3 py-16 sm:px-4 sm:py-24"
      aria-labelledby="reviews-title"
      aria-roledescription="карусель"
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      onFocusCapture={() => setHasFocus(true)}
      onBlurCapture={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) setHasFocus(false)
      }}
    >
      <div className="absolute inset-0 opacity-20">
        <div
          className="absolute inset-0 bg-cover bg-center opacity-30"
          style={{ backgroundImage: "url('/bg-blocks/4.avif')" }}
        />
        <div className="review-orb review-orb-right absolute top-1/3 right-1/4 w-96 h-96 bg-orange-500/20 rounded-full blur-[200px]" />
        <div className="review-orb review-orb-left absolute bottom-1/3 left-1/4 w-96 h-96 bg-mirage-yellow/10 rounded-full blur-[200px]" />
      </div>

      <div className="relative max-w-4xl mx-auto">
        <div className="text-center mb-12">
          <h2 id="reviews-title" className="reviews-heading-motion text-3xl font-black sm:text-6xl mb-4">
            Что говорят <span className="gradient-text">игроки</span>
          </h2>
          <p className="text-sm text-muted sm:text-base">
            {reviews.length} отзывов с 2GIS, Яндекс и Google
          </p>
        </div>

        <div className="relative min-h-[280px]">
            <div
              key={current}
              className={`review-slide ${direction === 1 ? "review-slide-next" : "review-slide-prev"}`}
              role="group"
              aria-label={`Отзыв ${current + 1} из ${reviews.length}`}
            >
              <div className="review-card-motion glass flex min-h-[280px] max-h-[70vh] flex-col justify-between overflow-y-auto rounded-3xl p-6 sm:p-12">
                <div>
                  <div
                    className="flex items-center gap-1 mb-4"
                    aria-label={`Оценка: ${review.rating} из 5`}
                  >
                    {Array.from({ length: 5 }).map((_, i) => (
                      <span
                        key={i}
                        aria-hidden="true"
                        className={`review-star text-xl ${i < review.rating ? "text-[#f5ff00]" : "text-white/20"}`}
                      >
                        ★
                      </span>
                    ))}
                    <span className="text-xs text-muted ml-2">{review.date}</span>
                  </div>
                  <p className="review-quote mb-6 whitespace-pre-line break-words text-base leading-relaxed text-muted sm:text-xl">
                    &ldquo;{review.text}&rdquo;
                  </p>
                </div>
                <div className="review-author flex items-center justify-between shrink-0">
                  <div className="flex min-w-0 items-center gap-2">
                    <div
                      className="review-avatar flex h-10 w-10 shrink-0 items-center justify-center rounded-full gradient-bg text-sm font-bold text-black"
                      aria-hidden="true"
                    >
                      {review.name[0]}
                    </div>
                    <div className="min-w-0">
                      <div className="text-base font-medium">{review.name}</div>
                      <div className="break-words text-xs text-muted sm:text-sm">{review.source}{review.club ? ` · ${review.club}` : ""}</div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
        </div>

        <div className="flex items-center justify-center gap-4 mt-6">
          <button
            onClick={prev}
            className="review-control h-12 w-12 rounded-full glass flex items-center justify-center text-lg text-muted hover:text-white hover:bg-white/10 transition-all"
            aria-label="Предыдущий отзыв"
            disabled={reviews.length <= 1}
          >
            ←
          </button>
          <div
            className="review-counter min-w-24 text-center text-base text-muted tabular-nums"
            role="status"
            aria-live="polite"
            aria-atomic="true"
          >
            {current + 1} из {reviews.length}
          </div>
          <button
            onClick={next}
            className="review-control h-12 w-12 rounded-full glass flex items-center justify-center text-lg text-muted hover:text-white hover:bg-white/10 transition-all"
            aria-label="Следующий отзыв"
            disabled={reviews.length <= 1}
          >
            →
          </button>
        </div>
      </div>
    </section>
  )
}
