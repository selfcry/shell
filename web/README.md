# Мираж

Сайт сети компьютерных клубов «Мираж» и «Мираж PRO» в Пятигорске.

## Локальный запуск

```bash
npm install
npm run dev
```

Сайт откроется на `http://localhost:3000`.

## Проверки

```bash
npm run lint
npm run build
```

## Стек

- Next.js 16 App Router
- React 19
- TypeScript
- Tailwind CSS 4
- Leaflet

Основные бизнес-данные филиалов, тарифы и характеристики находятся в
`src/data/clubs.ts`. Перед публикацией нужно проверить их соответствие
фактическим данным клубов.

## SEO

Чеклист Яндекс Вебмастер, Google Search Console, карт и цитирований:
[`docs/SEO.md`](docs/SEO.md).

Sitemap: `https://mirage-ptg.ru/sitemap.xml`

