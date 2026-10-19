import type { Metadata, Viewport } from "next"
import { Inter, Black_Ops_One, Russo_One } from "next/font/google"
import "./globals.css"
import { Header } from "@/components/Header"
import { Footer } from "@/components/Footer"
import { ScrollProgress } from "@/components/ScrollProgress"
import { BackToTop } from "@/components/BackToTop"
import { CopyGuard } from "@/components/CopyGuard"
import { clubs } from "@/data/clubs"
import { DEFAULT_DESCRIPTION, SITE_NAME, SITE_TITLE, SITE_URL } from "@/lib/site"

const inter = Inter({
  variable: "--font-inter",
  subsets: ["cyrillic"],
})

const blackOpsOne = Black_Ops_One({
  variable: "--font-black-ops",
  subsets: ["latin"],
  weight: "400",
})

const russoOne = Russo_One({
  variable: "--font-russo-one",
  subsets: ["cyrillic"],
  weight: "400",
})

export const metadata: Metadata = {
  title: {
    default: SITE_TITLE,
    template: `%s | ${SITE_NAME}`,
  },
  description: DEFAULT_DESCRIPTION,
  keywords: [
    "компьютерный клуб Пятигорск",
    "киберклуб Пятигорск",
    "игровой клуб Пятигорск",
    "Мираж Пятигорск",
    "Мираж PRO Пятигорск",
    "компьютерный клуб пятигорск цены",
    "ночной тариф компьютерный клуб",
    "PS5 Пятигорск",
  ],
  authors: [{ name: SITE_NAME, url: SITE_URL }],
  creator: SITE_NAME,
  publisher: SITE_NAME,
  metadataBase: new URL(SITE_URL),
  alternates: { canonical: "/" },
  openGraph: {
    type: "website",
    locale: "ru_RU",
    siteName: SITE_NAME,
    title: SITE_TITLE,
    description: DEFAULT_DESCRIPTION,
    url: SITE_URL,
    images: [
      {
        url: "/og-image.jpg",
        width: 1200,
        height: 630,
        alt: "Компьютерный клуб Мираж PRO в Пятигорске",
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: SITE_TITLE,
    description: DEFAULT_DESCRIPTION,
    images: ["/og-image.jpg"],
  },
  icons: {
    icon: [
      { url: "/favicon.ico", sizes: "16x16 32x32 48x48", type: "image/x-icon" },
      { url: "/favicon-16x16.png", sizes: "16x16", type: "image/png" },
      { url: "/favicon-32x32.png", sizes: "32x32", type: "image/png" },
      { url: "/favicon-48x48.png", sizes: "48x48", type: "image/png" },
    ],
    shortcut: "/favicon.ico",
    apple: [{ url: "/apple-touch-icon.png", sizes: "180x180" }],
  },
  manifest: "/manifest.json",
  robots: {
    index: true,
    follow: true,
    googleBot: {
      index: true,
      follow: true,
      "max-video-preview": -1,
      "max-image-preview": "large",
      "max-snippet": -1,
    },
  },
  category: "entertainment",
  verification: {
    yandex: "684fdd99c52bc751",
  },
}

export const viewport: Viewport = {
  themeColor: "#030303",
  colorScheme: "dark",
}

const localBusinessGraph = [
  {
    "@type": "Organization",
    "@id": `${SITE_URL}/#organization`,
    name: SITE_NAME,
    url: SITE_URL,
    logo: `${SITE_URL}/logo-icon.webp`,
    description: DEFAULT_DESCRIPTION,
    sameAs: [
      "https://t.me/mirage_cyber26",
      "https://vk.com/club229615472",
      "https://www.instagram.com/mirage_ptg/",
      "https://www.tiktok.com/@miragepro_pyatigorsk",
    ],
    contactPoint: clubs.map((club) => ({
      "@type": "ContactPoint",
      telephone: club.phone.replace(/[^\d+]/g, ""),
      contactType: "customer service",
      areaServed: "RU",
      availableLanguage: "Russian",
      name: club.name,
    })),
  },
  {
    "@type": "WebSite",
    "@id": `${SITE_URL}/#website`,
    url: SITE_URL,
    name: SITE_NAME,
    description: DEFAULT_DESCRIPTION,
    inLanguage: "ru-RU",
    publisher: { "@id": `${SITE_URL}/#organization` },
  },
  ...clubs.map((club) => ({
    "@type": "InternetCafe",
    "@id": `${SITE_URL}/clubs/${club.id}#business`,
    name: club.fullName,
    alternateName: club.name,
    description: club.description,
    url: `${SITE_URL}/clubs/${club.id}`,
    telephone: club.phone.replace(/[^\d+]/g, ""),
    email: club.email,
    image: [
      `${SITE_URL}${club.gallery[0] ?? "/og-image.jpg"}`,
      `${SITE_URL}/clubs/${club.id}-fast.jpg`,
    ],
    hasMap: club.mapUrl,
    areaServed: {
      "@type": "City",
      name: "Пятигорск",
    },
    address: {
      "@type": "PostalAddress",
      streetAddress: club.address.replace(", Пятигорск", ""),
      addressLocality: "Пятигорск",
      addressRegion: "Ставропольский край",
      postalCode: club.postalCode,
      addressCountry: "RU",
    },
    geo: {
      "@type": "GeoCoordinates",
      latitude: club.coords[1],
      longitude: club.coords[0],
    },
    openingHoursSpecification: {
      "@type": "OpeningHoursSpecification",
      dayOfWeek: [
        "Monday",
        "Tuesday",
        "Wednesday",
        "Thursday",
        "Friday",
        "Saturday",
        "Sunday",
      ],
      opens: "00:00",
      closes: "23:59",
    },
    aggregateRating: {
      "@type": "AggregateRating",
      ratingValue: club.rating.value,
      reviewCount: club.rating.count,
      bestRating: 5,
      worstRating: 1,
    },
    sameAs: [club.mapUrl, ...Object.values(club.socialLinks ?? {}).filter(Boolean)],
    priceRange: club.id === "mirage-pro" ? "₽₽₽" : "₽₽",
    parentOrganization: { "@id": `${SITE_URL}/#organization` },
  })),
]

export default function RootLayout({
  children,
}: {
  children: React.ReactNode
}) {
  return (
    <html
      lang="ru"
      className={`${inter.variable} ${blackOpsOne.variable} ${russoOne.variable}`}
    >
      <body className="min-h-dvh flex flex-col bg-background text-foreground antialiased">
        <script
          dangerouslySetInnerHTML={{
            __html:
              // cryvx.ru — копия сайта с личным кабинетом гостя (сервер клуба)
              '(function(){var h=["mirage-ptg.ru","www.mirage-ptg.ru","cryvx.ru","www.cryvx.ru","localhost","127.0.0.1"];' +
              "if(h.indexOf(location.hostname)<0){location.replace(\"https://mirage-ptg.ru\"+location.pathname+location.search)}})();",
          }}
        />
        <a className="skip-link" href="#main-content">
          Перейти к содержимому
        </a>
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{
            __html: JSON.stringify({
              "@context": "https://schema.org",
              "@graph": localBusinessGraph,
            }),
          }}
        />
        <ScrollProgress />
        <Header />
        <main id="main-content" className="flex-1">
          {children}
        </main>
        <Footer />
        <BackToTop />
        <CopyGuard />
      </body>
    </html>
  )
}
