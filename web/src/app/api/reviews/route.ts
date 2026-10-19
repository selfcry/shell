import { NextResponse, type NextRequest } from "next/server"
import { fallbackReviews, type Review } from "@/data/reviews"
import fetchedReviews from "@/data/fetched-reviews.json"

const PROVIDER_TIMEOUT_MS = 8_000
const OVERALL_TIMEOUT_MS = 12_000
const MAX_REVIEWS = 120
const GIS_REVIEWS_KEY = process.env.DGIS_REVIEWS_KEY

export const revalidate = 10_800

const RATE_LIMIT_WINDOW_MS = 60_000
const RATE_LIMIT_MAX_REQUESTS = 20
const requestLog = new Map<string, number[]>()

/** Minimal sliding-window limiter — each call to this route can trigger up to 6 outbound
 *  fetches to third-party providers, so it needs its own guard beyond the route's ISR cache. */
function isRateLimited(ip: string): boolean {
  const now = Date.now()
  const timestamps = (requestLog.get(ip) ?? []).filter((t) => now - t < RATE_LIMIT_WINDOW_MS)
  timestamps.push(now)
  requestLog.set(ip, timestamps)
  if (requestLog.size > 5000) {
    for (const [key, values] of requestLog) {
      if (values.every((t) => now - t >= RATE_LIMIT_WINDOW_MS)) requestLog.delete(key)
    }
  }
  return timestamps.length > RATE_LIMIT_MAX_REQUESTS
}

const GIS_ORG_IDS = [
  "70000001113803957", // Mirage PRO
  "70000001101776296", // Mirage
] as const

const CLUB_NAMES: Record<string, string> = {
  "70000001113803957": "Mirage PRO",
  "70000001101776296": "Mirage",
}

type ReviewsPayload = {
  reviews: Review[]
  source: "внешние источники" | "смешанные источники" | "резервные отзывы"
  totalFetched?: number
}

function fetchCached(url: string, init: RequestInit = {}) {
  return fetch(url, {
    ...init,
    next: { revalidate },
  })
}

async function withTimeout<T>(promise: Promise<T>, timeoutMs: number, fallback: T): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined
  const timeout = new Promise<T>((resolve) => {
    timer = setTimeout(() => resolve(fallback), timeoutMs)
  })

  try {
    return await Promise.race([promise, timeout])
  } finally {
    if (timer) clearTimeout(timer)
  }
}

function normalize(value: string | undefined) {
  return (value ?? "").trim().replace(/\s+/g, " ").toLocaleLowerCase("ru")
}

function reviewTimestamp(date: string) {
  if (!/^\d{4}-\d{2}-\d{2}/.test(date)) return 0
  const timestamp = Date.parse(date)
  return Number.isNaN(timestamp) ? 0 : timestamp
}

function prepareReviews(reviews: Review[]) {
  const unique = new Map<string, Review>()

  for (const review of reviews) {
    const name = review.name?.trim()
    const text = review.text?.trim()
    if (!name || !text || text.length < 5) continue

    const key = `${normalize(name)}|${normalize(text)}`
    if (unique.has(key)) continue

    unique.set(key, {
      ...review,
      name,
      text,
      rating: Math.max(1, Math.min(5, Number(review.rating) || 5)),
      date: review.date?.trim() ?? "",
      source: review.source?.trim() || "Источник не указан",
      club: review.club?.trim() || undefined,
      avatar: review.avatar?.trim() || undefined,
    })
  }

  return [...unique.values()]
    .sort((a, b) => {
      const byDate = reviewTimestamp(b.date) - reviewTimestamp(a.date)
      if (byDate !== 0) return byDate

      const byRating = b.rating - a.rating
      if (byRating !== 0) return byRating

      return (
        [
          a.source.localeCompare(b.source, "ru"),
          (a.club ?? "").localeCompare(b.club ?? "", "ru"),
          a.name.localeCompare(b.name, "ru"),
          a.text.localeCompare(b.text, "ru"),
        ].find((value) => value !== 0) ?? 0
      )
    })
    .slice(0, MAX_REVIEWS)
}

function jsonResponse(payload: ReviewsPayload) {
  return NextResponse.json(payload, {
    headers: {
      "Cache-Control": `public, max-age=60, s-maxage=${revalidate}, stale-while-revalidate=${revalidate * 2}`,
    },
  })
}

async function fetchReviewsFrom2gisApi(orgId: string): Promise<Review[]> {
  if (!GIS_REVIEWS_KEY) {
    console.warn("[reviews] DGIS_REVIEWS_KEY not set, skipping 2GIS reviews")
    return []
  }

  const club = CLUB_NAMES[orgId]
  const collected: Review[] = []
  const pageSize = 50
  let offset = 0

  try {
    while (offset < 200) {
      const url =
        `https://public-api.reviews.2gis.com/2.0/branches/${orgId}/reviews` +
        `?limit=${pageSize}&offset=${offset}&key=${GIS_REVIEWS_KEY}` +
        `&rated=true&sort_by=date_created`

      const res = await fetchCached(url, {
        headers: {
          "User-Agent":
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
          Accept: "application/json",
          Referer: `https://2gis.ru/pyatigorsk/firm/${orgId}/tab/reviews`,
          Origin: "https://2gis.ru",
        },
        signal: AbortSignal.timeout(8000),
      })
      if (!res.ok) break

      const data = (await res.json()) as {
        reviews?: Array<{
          text?: string
          rating?: number
          date_created?: string
          user?: {
            name?: string
            photo_preview_urls?: { url?: string }
          }
        }>
      }

      const page = data.reviews ?? []
      for (const item of page) {
        const text = (item.text ?? "").trim()
        if (text.length < 5) continue
        collected.push({
          name: item.user?.name?.trim() || "Гость",
          text,
          rating: item.rating ?? 5,
          date: (item.date_created || "").split("T")[0],
          source: "2GIS",
          club,
          avatar: item.user?.photo_preview_urls?.url || undefined,
        })
      }

      if (page.length < pageSize) break
      offset += pageSize
    }
  } catch {
    /* fall through to HTML scrape */
  }

  if (collected.length >= 10) return collected
  return collected
}

async function fetchFromGoogle(): Promise<Review[]> {
  const key = process.env.GOOGLE_MAPS_API_KEY
  if (!key) return []

  const queries = [
    "Mirage компьютерный клуб Пятигорск Октябрьская 46",
    "Mirage PRO компьютерный клуб Пятигорск Панагюриште 4",
  ]
  const all: Review[] = []

  for (const query of queries) {
    try {
      const searchUrl = `https://maps.googleapis.com/maps/api/place/textsearch/json?query=${encodeURIComponent(query)}&key=${key}&language=ru`
      const res = await fetchCached(searchUrl, { signal: AbortSignal.timeout(5000) })
      if (!res.ok) continue
      const data = await res.json()
      const placeId = data?.results?.[0]?.place_id
      if (!placeId) continue

      const detailUrl = `https://maps.googleapis.com/maps/api/place/details/json?place_id=${placeId}&fields=reviews&key=${key}&language=ru`
      const detailRes = await fetchCached(detailUrl, { signal: AbortSignal.timeout(5000) })
      if (!detailRes.ok) continue
      const detailData = await detailRes.json()

      for (const r of detailData?.result?.reviews ?? []) {
        all.push({
          name: r.author_name || "Гость",
          text: r.text || "",
          rating: r.rating ?? 5,
          date: r.relative_time_description || "",
          source: "Google Карты",
          avatar: r.profile_photo_url || undefined,
        })
      }
    } catch {
      continue
    }
  }
  return all
}

async function fetchFromYandexPage(): Promise<Review[]> {
  const orgs = [
    {
      name: "Mirage",
      urls: [
        "https://yandex.ru/maps/org/mirazh/181089479974/reviews/",
        "https://yandex.ru/maps/org/mirazh/181089479974/",
      ],
    },
    {
      name: "Mirage PRO",
      urls: [
        "https://yandex.ru/maps/org/mirazh_pro/133093405689/reviews/",
        "https://yandex.ru/maps/org/mirazh_pro/133093405689/",
      ],
    },
  ]

  const all: Review[] = []
  const UA =
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

  for (const { name: club, urls } of orgs) {
    for (const url of urls) {
      try {
        const res = await fetchCached(url, {
          headers: { "User-Agent": UA },
          signal: AbortSignal.timeout(10000),
        })
        if (!res.ok) continue
        const html = await res.text()

        // schema.org Review blocks
        const blockRegex =
          /itemProp="review"[^>]*itemType="http:\/\/schema\.org\/Review"[^>]*>([\s\S]*?)(?=itemProp="review"|<\/section>|$)/g
        let m: RegExpExecArray | null
        while ((m = blockRegex.exec(html)) !== null) {
          const block = m[1]
          let text = ""
          const textMatch = block.match(
            /class="[^"]*spoiler-view__text-container[^"]*"[^>]*>([^<]+)/
          )
          if (textMatch) text = textMatch[1].trim()
          else {
            const bodyMatch = block.match(/itemProp="reviewBody"[^>]*>([\s\S]*?)<\/div>/)
            if (bodyMatch) text = bodyMatch[1].replace(/<[^>]+>/g, "").trim()
          }
          if (!text || text.length < 10) continue
          if (/Alice|Лента|Благодарим|Спасибо|Создано|created|лента/i.test(text)) continue

          const nameMatch = block.match(/itemProp="name"[^>]*>\s*([^<]+)/)
          const name = nameMatch ? nameMatch[1].trim() : "Гость"
          const avatarMatch = block.match(/<meta itemProp="image" content="([^"]+)"/)
          const ratingMatch = block.match(/itemProp="ratingValue" content="([^"]+)"/)
          const dateMatch = block.match(/itemProp="datePublished" content="([^"]+)"/)

          all.push({
            name,
            text,
            rating: ratingMatch ? parseFloat(ratingMatch[1]) : 5,
            date: dateMatch ? dateMatch[1].split("T")[0] : "",
            source: "Яндекс Карты",
            club,
            avatar: avatarMatch?.[1] || undefined,
          })
        }

        // JSON-LD Review objects embedded in page
        for (const script of html.matchAll(
          /<script[^>]*type="application\/ld\+json"[^>]*>([\s\S]*?)<\/script>/gi
        )) {
          try {
            const data = JSON.parse(script[1])
            const nodes = Array.isArray(data) ? data : [data]
            for (const node of nodes) {
              const graph = node?.["@graph"] ?? [node]
              for (const item of graph) {
                if (item?.["@type"] !== "Review" && item?.["@type"] !== "UserReview") continue
                const text = String(item.reviewBody || item.description || "").trim()
                if (text.length < 10) continue
                all.push({
                  name: item.author?.name || item.author || "Гость",
                  text,
                  rating: Number(item.reviewRating?.ratingValue ?? item.ratingValue ?? 5),
                  date: String(item.datePublished || "").split("T")[0],
                  source: "Яндекс Карты",
                  club,
                })
              }
            }
          } catch {
            /* ignore bad json-ld */
          }
        }
      } catch {
        continue
      }
    }
  }
  return all
}

export async function GET(request: NextRequest) {
  const ip = request.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ?? "unknown"
  if (isRateLimited(ip)) {
    return NextResponse.json({ error: "Too many requests" }, { status: 429 })
  }

  try {
    const providers = [
      Promise.all(GIS_ORG_IDS.map(fetchReviewsFrom2gisApi)).then((reviews) => reviews.flat()),
      fetchFromGoogle(),
      fetchFromYandexPage(),
    ].map((provider) => withTimeout(provider, PROVIDER_TIMEOUT_MS, []))

    const results = await withTimeout(Promise.all(providers), OVERALL_TIMEOUT_MS, [])
    const externalReviews = prepareReviews([
      ...results.flat(),
      ...(fetchedReviews as Review[]),
      ...fallbackReviews,
    ])

    return jsonResponse({
      reviews: externalReviews,
      source:
        results.flat().length > 0 || (fetchedReviews as Review[]).length > 0
          ? "внешние источники"
          : "резервные отзывы",
      totalFetched: externalReviews.length,
    })
  } catch {
    return jsonResponse({
      reviews: prepareReviews([...(fetchedReviews as Review[]), ...fallbackReviews]),
      source: "резервные отзывы",
      totalFetched: 0,
    })
  }
}
