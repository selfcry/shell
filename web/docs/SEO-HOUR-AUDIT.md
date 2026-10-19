# SEO hour audit — Mirage (Пятигорск)

**Date:** 2026-07-29  
**Site:** https://mirage-ptg.ru  
**Deploy:** production on Vercel (aliased to mirage-ptg.ru)  
**Honest verdict:** on-site выжат сильно; **№1 по коммерческим запросам города без карт/отзывов невозможен**. Сейчас в органике по «компьютерный клуб Пятигорск» доминируют 2GIS/Яндекс Категории/LANGAME/VRV — сайт Мираж в топе не светился. Главный рычаг у владельца: **сайт в карточках Яндекс/2GIS** + отзывы на PRO.

---

## What was done this hour (on-site)

### New indexable pages (live, in sitemap)

| URL | Intent |
|-----|--------|
| [/kompyuternyy-klub-pyatigorsk](https://mirage-ptg.ru/kompyuternyy-klub-pyatigorsk) | Head term |
| [/kiberklub-pyatigorsk](https://mirage-ptg.ru/kiberklub-pyatigorsk) | Киберклуб |
| [/igrovoy-klub-pyatigorsk](https://mirage-ptg.ru/igrovoy-klub-pyatigorsk) | Игровой клуб |
| [/nochnoy-tarif-pyatigorsk](https://mirage-ptg.ru/nochnoy-tarif-pyatigorsk) | Ночной тариф |
| [/ps5-pyatigorsk](https://mirage-ptg.ru/ps5-pyatigorsk) | PS5 |
| [/mirage-vs-mirage-pro](https://mirage-ptg.ru/mirage-vs-mirage-pro) | Сравнение |
| [/kontakty](https://mirage-ptg.ru/kontakty) | NAP hub |
| [/bronirovanie](https://mirage-ptg.ru/bronirovanie) | How to book + FAQ schema |

**Sitemap now:** 13 URLs (was 5).

### On-page / technical

- Title/description главной: «Компьютерный клуб в Пятигорске…»
- Hero subtitle: «КОМПЬЮТЕРНЫЕ КЛУБЫ В ПЯТИГОРСКЕ»
- Club H1: keyword + city; about + how-to-get; visible ratings
- `/prices`: тарифы **обоих** клубов в HTML + Offer JSON-LD
- `AggregateRating` в InternetCafe schema (Yandex: PRO 4.9/28, Мираж 5.0/324)
- Organization `contactPoint` для обоих телефонов
- FAQ расширен + внутренние ссылки
- ClubsPreview: оба клуба в DOM; SeoHub «Полезное» на главной
- `public/llms.txt` + `public/prices.json` (AI SEO)
- Footer links на ключевые лендинги
- Яндекс: переобход новых URL отправлен

### Already done earlier (same day)

- GSC domain + sitemap (5 URLs processed)
- Yandex Webmaster verified + sitemap queued
- Meta `yandex-verification`

---

## Off-site findings

### 1. Yandex Maps org cards

#### Мираж PRO — [org/133093405689](https://yandex.ru/maps/org/mirazh_pro/133093405689/)

| Field | Status | Notes |
|-------|--------|-------|
| Rating | **4.9** | 28 ratings |
| Reviews | **23** | Owner replies present on sample reviews |
| Phone | Match | +7 (905) 444-60-95 |
| Address | Match | Панагюриште, 4 / 357532 |
| Hours | Match | 24 hours |
| Website | **GAP** | No website to mirage-ptg.ru (only messengers) |
| Products | Stale | “Hasn't been updated for a while” |
| Photos | OK | ~25 |

#### Мираж — [org/181089479974](https://yandex.ru/maps/org/mirazh/181089479974/)

| Field | Status | Notes |
|-------|--------|-------|
| Rating | **5.0** | 324 ratings |
| Reviews | **~297** | Strong vs PRO |
| Phone / address / hours | Match | Октябрьская 46 / 357502 / 24h |
| Website | **GAP** | No mirage-ptg.ru |
| Products | Stale | Same flag |
| Photos | Strong | ~50–59 |

**PRO vs Mirage:** PRO has ~13× fewer reviews. Website links missing on both — **#1 blocker** for organic site traffic from local pack.

---

### 2. Competitors («компьютерный клуб Пятигорск»)

| # | Competitor | Why they win visibility |
|---|------------|-------------------------|
| 1 | **VRV Club** (vrv-club.ru) | Full SEO site + LANGAME online booking |
| 2 | **LEET** (VK) | Strong local social + citations |
| 3 | **COLIZEUM** | Franchise domain authority |
| 4 | **Play Ground** | Yandex Business landing + offers |
| 5 | **Kiber26 / Space / K4** | Multi-directory presence |

Directories (2GIS, Yandex category, LANGAME) occupy top slots — maps pack > website for this niche.

---

### 3. Citations

| Platform | Status |
|----------|--------|
| LANGAME | Both exist; phones OK; **site missing**; 0 reviews; no online booking |
| 2GIS | Both exist; phones OK; **site missing / TG as site** |
| Zoon | **No Mirage cards**; stale **TeamTeaClub** at Октябрьская 46 |
| Flamp | PRO exists |

---

## Owner checklist (must do manually — bigger than any code)

### Critical (do today)

1. **Яндекс Бизнес** → Сайт PRO = `https://mirage-ptg.ru/clubs/mirage-pro`  
2. **Яндекс Бизнес** → Сайт Мираж = `https://mirage-ptg.ru/clubs/mirage`  
3. **2GIS** → website both clubs to club URLs (not Telegram)  
4. Обновить прайс/услуги в Яндекс (флаг «давно не обновлялось»)

### This week

5. Создать карточки **Zoon** на оба клуба; закрыть/исправить TeamTeaClub  
6. LANGAME: claim + сайт + online booking если возможно  
7. Качать отзывы на **PRO** (акция 50 бонусов уже на сайте)  
8. VK / IG / TG bio → `https://mirage-ptg.ru`

### Monitoring (1–2 weeks)

- `site:mirage-ptg.ru` в Яндекс/Google  
- Позиции: компьютерный клуб / киберклуб / игровой клуб / цены / Мираж PRO Пятигорск  
- GSC / Webmaster: indexed pages count for new landings  

---

## Expected outcome

| Layer | Status after hour |
|-------|-------------------|
| Crawl / index surface | **Strong** (13 URLs + schema + NAP hubs) |
| Local pack readiness | **Blocked** until website URLs on maps |
| Review moat | Мираж strong / PRO weak |
| Organic #1 guarantee | **None** — depends on maps + time to index landings (days–weeks) |

**If you do only one thing after reading this:** put `mirage-ptg.ru` club URLs into both Yandex and 2GIS cards. That single change connects the maps pack (where people already find you) to the site we just maximized.
