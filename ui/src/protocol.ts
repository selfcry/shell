// Протокол обмена между интерфейсом (WebView2) и хостом (ClubShell.Locker).
// Любая сторона шлёт JSON-объект с полем `type`.

export interface TariffPackage {
  id: string;
  name: string; // "1 час", "Ночь"
  minutes: number;
  price: number; // рубли
  nightOnly?: boolean; // доступно только 22:00–08:00
  fromHour?: number | null; // окно действия тарифа (по времени клуба), например 8 → 14
  toHour?: number | null;
}

/** Товар бара в меню на ПК. */
export interface ShopItem { id: number; name: string; category: string | null; price: number; stock: number }

/** Запись каталога, как её видит интерфейс. Пути и аргументы запуска знает только хост. */
export interface CatalogItem {
  id: string;
  title: string;
  kind: "game" | "app";
  group?: string;
  cover?: string; // относительный адрес обложки 2:3; если нет — рисуется заглушка
  clubAccount?: boolean; // выдаётся клубный аккаунт
  minAge?: number; // возрастное ограничение
  pinned?: boolean; // закреплено на панели задач
}

/** Процесс в диспетчере задач. app = у процесса есть окно (показывается в «Приложениях»). */
export interface ProcessItem {
  pid: number;
  name: string;
  title?: string;
  cpu: number; // % от всех ядер
  memMb: number;
  app: boolean;
}

/** Окно на панели задач. id — дескриптор окна, действителен только для последнего присланного списка. */
export interface WindowItem {
  id: string;
  title: string;
  exe?: string; // имя процесса, для заглушки значка
  icon?: string; // data:image/png;base64,… значок программы
  active: boolean;
  minimized: boolean;
}

// ---- админ-панель (ClubShell.Admin): страница не знает ключа, запросы к серверу делает хост ----
export interface ApiResultBody { status: number; body: string }

/** Запись в проводнике «Мои файлы». */
export interface FsEntry { name: string; isDir: boolean; size: number; modified: number }
export interface FsRoot { name: string; path: string }

/** Окно в переключателе Alt+Tab. */
export interface SwitcherItem {
  id: string;
  title: string;
  exe?: string;
  icon?: string;
}

/** Закреплённая программа на панели задач (браузер, Discord, Telegram…). */
export interface PinnedItem {
  id: string;
  title: string;
  icon?: string; // data:image/png;base64,… значок из exe
}

export type ToHost =
  | { type: "api"; id: number; method: "GET" | "POST" | "PUT" | "DELETE" | "UPLOAD"; path: string; body?: unknown } // UPLOAD: хост сам откроет выбор файла и отправит картинку (обложка)
  | { type: "saveSettings"; serverUrl: string; key: string }
  | { type: "ready" }
  | { type: "activateWindow"; id: string } // кликнули по окну: показать / свернуть, если оно уже активно
  | { type: "closeWindow"; id: string }
  | { type: "openTaskManager" }
  | { type: "closeTaskManager" }
  | { type: "endTask"; pid: number } // снять задачу; хост проверяет pid по последнему списку
  | { type: "speedTest" } // измерить скорость интернета
  | { type: "showDesktop" } // свернуть всё и показать рабочий стол клуба
  | { type: "login"; username: string; password: string }
  | { type: "register"; username: string; password: string; birth: string } // birth: yyyy-MM-dd
  | { type: "startSession"; packageId: string }
  | { type: "stopSession" }
  | { type: "logout" } // гость вышел, не начав сеанс
  | { type: "fs"; id: number; op: "roots" | "list" | "open" | "mkdir" | "rename" | "delete" | "copy" | "move"; path?: string; name?: string; dest?: string }
  | { type: "closeExplorer" }
  | { type: "openExplorer" } // открыть «Мои файлы»
  | { type: "launch"; id: string }
  | { type: "setVolume"; value: number } // 0..100
  | { type: "setMute"; muted: boolean }
  | { type: "reboot" }
  | { type: "callAdmin" } // «Позвать администратора»
  | { type: "promo"; code: string } // ввести промокод
  | { type: "shopOpen" } // загрузить меню бара
  | { type: "order"; items: { productId: number; qty: number }[] } // заказ в бар с баланса
  | { type: "adminExit"; pin: string };

/** Уровень гостя в программе лояльности. */
export interface LoyaltyInfo { bonus: number; spent: number; level: string | null; cashbackPct: number; nextLevel: string | null; toNext: number }

export type FromHost =
  | { type: "settings"; serverUrl: string; hasKey: boolean }
  | { type: "apiResult"; id: number; status: number; body: string }
  | { type: "config"; pcName: string; zone: string }
  | { type: "mode"; mode: "locked" | "session" } // session = рабочий стол гостя
  | { type: "link"; online: boolean; reason?: string } // связь с сервером/службой
  | {
      type: "loginResult";
      ok: boolean;
      error?: string;
      user?: string;
      balance?: number;
      packages?: TariffPackage[];
      bonus?: number;
      loyalty?: LoyaltyInfo | null;
    }
  | { type: "bonus"; bonus: number } // бонусов стало столько (после покупки или промокода)
  | { type: "promoResult"; ok: boolean; error?: string; bonus?: number }
  | { type: "callResult"; ok: boolean; text?: string }
  | { type: "shop"; items: ShopItem[]; error?: string }
  | { type: "orderResult"; ok: boolean; error?: string; orderId?: number; total?: number }
  | { type: "session"; remainingSec: number | null; balance: number; bonus: number } // для панели задач
  | { type: "startResult"; ok: boolean; error?: string }
  | { type: "tick"; remainingSec: number; balance: number }
  | { type: "sessionEnded"; reason: string }
  | { type: "fsResult"; id: number; ok: boolean; error?: string; roots?: FsRoot[]; path?: string; entries?: FsEntry[] }
  | { type: "pinned"; items: PinnedItem[] }
  | { type: "switcher"; items: SwitcherItem[]; selected: number }
  | { type: "select"; selected: number }
  | { type: "balance"; balance: number } // баланс гостя изменился на сервере (пополнение/списание)
  | { type: "notice"; text: string } // сообщение от администратора
  | { type: "rebooting"; test: boolean } // сеанс завершён: ПК перезагружается и чистится (test — только имитация)
  | { type: "catalog"; items: CatalogItem[] }
  | { type: "launchResult"; id: string; ok: boolean; error?: string }
  | { type: "windows"; items: WindowItem[] }
  | { type: "audio"; volume: number; muted: boolean }
  | { type: "network"; online: boolean; name?: string; speedMbps?: number }
  | { type: "processes"; items: ProcessItem[]; cpu: number; memUsedMb: number; memTotalMb: number }
  | { type: "ping"; ms: number | null } // задержка до узла; null — узел не отвечает
  | { type: "speedResult"; ok: boolean; downMbps?: number; upMbps?: number; pingMs?: number | null; error?: string; source?: string }
  | { type: "adminExitResult"; ok: boolean };
