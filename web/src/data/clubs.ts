export interface ClubZone {
  name: string
  specs: string
  pc?: string
  monitor?: string
  gpu?: string
  cpu?: string
  ram?: string
  keyboard?: string
  mouse?: string
  headset?: string
  chair?: string
  storage?: string
}

export interface ClubPrice {
  name: string
  prices: {
    "1 час": number
    "3 часа": number
    "5 часов": number
    "Ночь (22:00-8:00)": number
  }
}

export interface ClubPromotion {
  title: string
  description: string
}

export interface ClubRating {
  value: number
  count: number
  source: string
  sourceUrl: string
}

export interface Club {
  id: string
  name: string
  fullName: string
  seoH1: string
  address: string
  postalCode: string
  phone: string
  email: string
  coords: [number, number]
  mapUrl: string
  description: string
  about: string[]
  howToGet: string
  rating: ClubRating
  specs: string[]
  zones: ClubZone[]
  prices: ClubPrice[]
  workTime: string
  paymentMethods: string[]
  internet: string
  totalPC: number
  totalPS: number
  features: string[]
  promotions: ClubPromotion[]
  gallery: string[]
  socialLinks?: {
    vk?: string
    telegram?: string
    instagram?: string
    tiktok?: string
  }
}

export const clubs: Club[] = [
  {
    id: "mirage-pro",
    name: "Мираж PRO",
    fullName: "Компьютерный клуб Мираж PRO",
    seoH1: "Компьютерный клуб Мираж PRO в Пятигорске",
    address: "ул. Панагюриште, 4, Пятигорск",
    postalCode: "357532",
    phone: "+7 (905) 444-60-95",
    email: "mirage_pro26@mail.ru",
    coords: [43.064686, 44.054873],
    mapUrl: "https://yandex.ru/maps/org/mirazh_pro/133093405689/",
    description:
      "Компьютерный клуб Мираж PRO в Пятигорске на ул. Панагюриште, 4: 51 игровой ПК, 4 комнаты PS5, 4 консоли PS5. Соседний клуб — Мираж на Октябрьской ул., 46: 28 ПК. Работаем круглосуточно.",
    about: [
      "Мираж PRO — флагманский компьютерный клуб сети Мираж в Пятигорске. Здесь 51 игровой ПК, мониторы до 400 Гц, зоны BOOTCAMP и DUO VIP, а также 4 комнаты PlayStation 5 с 4 консолями PS5 Slim.",
      "Клуб рассчитан на соло-игры, пати и тренировки: мощные RTX-сборки, бездисковая система и интернет свыше 1 Гбит/с. Работаем круглосуточно без выходных.",
      "Соседний клуб сети — Мираж на Октябрьской ул., 46: 28 игровых ПК, VIP-зоны и 3 PS5 в центре города.",
    ],
    howToGet:
      "Адрес: ул. Панагюриште, 4, Пятигорск, 357532. Ориентир — рядом с Фармацевтической академией (около 165 м). На общественном транспорте удобны остановки «Фармацевтическая академия», «Универсам» и «Улица Зорге». Парковка возле клуба.",
    rating: {
      value: 4.9,
      count: 28,
      source: "Яндекс Карты",
      sourceUrl: "https://yandex.ru/maps/org/mirazh_pro/133093405689/reviews/",
    },
    specs: [
      "51 ПК (Арена + VIP)",
      "AMD Ryzen 7 5700X / 7700",
      "RTX 5060 / RTX 5070",
      "Мониторы до 400Hz",
      "Бездисковая система",
    ],
    zones: [
      {
        name: "Общий зал (ARENA)",
        specs: "39 ПК",
        cpu: "AMD Ryzen 7 5700X",
        gpu: "NVIDIA RTX 5060",
        ram: "32GB DDR4",
        monitor: 'MSI 24.5" 300Hz',
        keyboard: "ZONE 51",
        mouse: "Redragon",
        headset: "Redragon",
        chair: "Knight",
        storage: "Бездисковая система",
      },
      {
        name: "VIP (BOOTCAMP / DUO)",
        specs: "12 ПК",
        cpu: "AMD Ryzen 7 7700",
        gpu: "NVIDIA RTX 5070",
        ram: "32GB DDR5",
        monitor: 'HKC 24.5" 400Hz',
        keyboard: "ZONE 51",
        mouse: "Logitech",
        headset: "Red Square",
        chair: "Knight",
        storage: "Бездисковая система",
      },
    ],
    prices: [
      {
        name: "ARENA",
        prices: {
          "1 час": 140,
          "3 часа": 350,
          "5 часов": 500,
          "Ночь (22:00-8:00)": 550,
        },
      },
      {
        name: "COMFORT",
        prices: {
          "1 час": 160,
          "3 часа": 420,
          "5 часов": 620,
          "Ночь (22:00-8:00)": 600,
        },
      },
      {
        name: "BOOTCAMP",
        prices: {
          "1 час": 240,
          "3 часа": 650,
          "5 часов": 1000,
          "Ночь (22:00-8:00)": 800,
        },
      },
      {
        name: "DUO VIP",
        prices: {
          "1 час": 270,
          "3 часа": 700,
          "5 часов": 1100,
          "Ночь (22:00-8:00)": 900,
        },
      },
      {
        name: "TV ARENA",
        prices: {
          "1 час": 300,
          "3 часа": 0,
          "5 часов": 0,
          "Ночь (22:00-8:00)": 0,
        },
      },
      {
        name: "TV VIP",
        prices: {
          "1 час": 500,
          "3 часа": 1200,
          "5 часов": 1800,
          "Ночь (22:00-8:00)": 2000,
        },
      },
    ],
    workTime: "Круглосуточно",
    paymentMethods: ["Наличные", "Карты Visa/Mastercard/Мир", "СБП"],
    internet: ">1 Гбит/с",
    totalPC: 51,
    totalPS: 4,
    features: [
      "Можно со своими девайсами",
      "Можно курить электронные сигареты, кроме IQOS",
      "Клубные аккаунты Steam/Epic",
      "Можно скачивать игры",
      "Wi-Fi",
      "Кондиционер в каждой зоне",
      "Бездисковая система",
      "Терминал самообслуживания",
      "Продажа напитков и снеков через администратора",
      "Санузел",
    ],
    promotions: [
      {
        title: "День рождения x2",
        description: "В свой день рождения получи х2 от своего депозита",
      },
      {
        title: "50 бонусов за отзыв",
        description: "Оставь отзыв на Яндекс Картах или 2GIS и получи 50 бонусных рублей",
      },
    ],
    gallery: [
      "/gallery/mirage-pro/1.jpg",
      "/gallery/mirage-pro/2.jpg",
      "/gallery/mirage-pro/3.jpg",
      "/gallery/mirage-pro/4.jpg",
      "/gallery/mirage-pro/5.jpg",
      "/gallery/mirage-pro/6.jpg",
      "/gallery/mirage-pro/7.jpg",
      "/gallery/mirage-pro/8.jpg",
      "/gallery/mirage-pro/9.jpg",
    ],
    socialLinks: {
      vk: "https://vk.com/club229615472",
      telegram: "https://t.me/mirage_cyber26",
      instagram: "https://www.instagram.com/mirage_ptg/",
      tiktok: "https://www.tiktok.com/@miragepro_pyatigorsk",
    },
  },
  {
    id: "mirage",
    name: "Мираж",
    fullName: "Компьютерный клуб Мираж",
    seoH1: "Компьютерный клуб Мираж в Пятигорске",
    address: "Октябрьская ул., 46, Пятигорск",
    postalCode: "357502",
    phone: "+7 (905) 499-00-95",
    email: "mirage_cyber26@mail.ru",
    coords: [43.061597, 44.037016],
    mapUrl: "https://yandex.ru/maps/org/mirazh/181089479974/",
    description:
      "Компьютерный клуб Мираж в Пятигорске на Октябрьской ул., 46: 28 игровых ПК, 3 комнаты VIP, 3 консоли PS5. Соседний клуб — Мираж PRO на Панагюриште, 4: 51 ПК, 4 комнаты PS5. Работаем 24/7.",
    about: [
      "Мираж — компьютерный клуб сети Мираж PRO в центре Пятигорска. На Октябрьской ул., 46 вас ждут 28 игровых ПК на RTX 3070, VIP-зоны и 3 консоли PlayStation 5.",
      "Формат спокойнее и камернее флагмана: удобно зайти после учёбы или работы, поиграть вдвоём или компанией. Интернет свыше 1 Гбит/с, клубные аккаунты Steam/Epic, работа 24/7.",
      "Соседний клуб сети — Мираж PRO на ул. Панагюриште, 4: 51 игровой ПК, 4 комнаты PS5 и мониторы до 400 Гц.",
    ],
    howToGet:
      "Адрес: Октябрьская ул., 46, Пятигорск, 357502. Ориентир — центр города, рядом остановка «Улица Малыгина» (около 135 м). Также удобны остановки «Проспект Горького» и «Улица Козлова».",
    rating: {
      value: 5.0,
      count: 324,
      source: "Яндекс Карты",
      sourceUrl: "https://yandex.ru/maps/org/mirazh/181089479974/reviews/",
    },
    specs: [
      "28 ПК (Арена + Комфорт)",
      "Intel Core i5-13400F",
      "SQUAD CAMP",
      "RTX 3070",
      "Беспроводная гарнитура",
    ],
    zones: [
      {
        name: "Общий зал (ARENA)",
        specs: "23 ПК",
        cpu: "Intel Core i5-13400F",
        gpu: "NVIDIA GeForce RTX 3070 8GB",
        ram: "32GB DDR4",
        monitor: 'HKC 27" 200Hz',
        keyboard: "Attack Shark",
        mouse: "Logitech",
        headset: "Redragon",
        chair: "Zombie",
        storage: "Гибридная система",
      },
      {
        name: "VIP",
        specs: "5 ПК",
        cpu: "Intel Core i5-13400F",
        gpu: "NVIDIA GeForce RTX 3070 8GB",
        ram: "32GB DDR4",
        monitor: 'MSI 25" 300Hz',
        keyboard: "Attack Shark",
        mouse: "A4Tech",
        headset: "A4Tech",
        chair: "Knight",
        storage: "Гибридная система",
      },
    ],
    prices: [
      {
        name: "ARENA",
        prices: {
          "1 час": 120,
          "3 часа": 299,
          "5 часов": 439,
          "Ночь (22:00-8:00)": 500,
        },
      },
      {
        name: "COMFORT",
        prices: {
          "1 час": 140,
          "3 часа": 329,
          "5 часов": 529,
          "Ночь (22:00-8:00)": 600,
        },
      },
      {
        name: "SQUAD CAMP",
        prices: {
          "1 час": 170,
          "3 часа": 470,
          "5 часов": 600,
          "Ночь (22:00-8:00)": 700,
        },
      },
      {
        name: "TV Arena",
        prices: {
          "1 час": 300,
          "3 часа": 700,
          "5 часов": 1100,
          "Ночь (22:00-8:00)": 1000,
        },
      },
      {
        name: "TV Comfort",
        prices: {
          "1 час": 350,
          "3 часа": 800,
          "5 часов": 1200,
          "Ночь (22:00-8:00)": 1100,
        },
      },
      {
        name: "TV VIP",
        prices: {
          "1 час": 400,
          "3 часа": 850,
          "5 часов": 1300,
          "Ночь (22:00-8:00)": 1200,
        },
      },
    ],
    workTime: "Круглосуточно",
    paymentMethods: ["Наличные", "Карты Visa/Mastercard/Мир", "СБП"],
    internet: ">1 Гбит/с",
    totalPC: 28,
    totalPS: 3,
    features: [
      "Можно курить электронные сигареты, кроме IQOS",
      "Клубные аккаунты Steam/Epic",
      "Можно скачивать игры",
      "Wi-Fi",
      "Кондиционер в каждой зоне",
      "Гибридная система",
      "Продажа напитков и снеков через администратора",
      "Санузел",
    ],
    promotions: [
      {
        title: "День рождения x2",
        description: "В свой день рождения получи х2 от своего депозита",
      },
      {
        title: "50 бонусов за отзыв",
        description: "Оставь отзыв на Яндекс Картах или 2GIS и получи 50 бонусных рублей",
      },
    ],
    gallery: [
      "/gallery/mirage/1.jpg",
      "/gallery/mirage/2.jpg",
      "/gallery/mirage/3.jpg",
      "/gallery/mirage/4.jpg",
      "/gallery/mirage/5.jpg",
      "/gallery/mirage/6.jpg",
      "/gallery/mirage/7.jpg",
      "/gallery/mirage/8.jpg",
      "/gallery/mirage/9.jpg",
      "/gallery/mirage/10.jpg",
    ],
    socialLinks: {
      vk: "https://vk.com/club229615472",
      telegram: "https://t.me/mirage_cyber26",
      instagram: "https://www.instagram.com/mirage_ptg/",
      tiktok: "https://www.tiktok.com/@miragepro_pyatigorsk",
    },
  },
]
