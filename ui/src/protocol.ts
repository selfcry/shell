// Протокол обмена между интерфейсом (WebView2) и хостом (ClubShell.Locker).
// Любая сторона шлёт JSON-объект с полем `type`.

export interface TariffPackage {
  id: string;
  name: string; // "1 час", "Ночь"
  minutes: number;
  price: number; // рубли
  nightOnly?: boolean; // доступно только 22:00–08:00
}

/** Запись каталога, как её видит интерфейс. Пути и аргументы запуска знает только хост. */
export interface CatalogItem {
  id: string;
  title: string;
  kind: "game" | "app";
  group?: string;
  cover?: string; // относительный адрес обложки 2:3; если нет — рисуется заглушка
  clubAccount?: boolean; // выдаётся клубный аккаунт
  minAge?: number; // возрастное ограничение
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

export type ToHost =
  | { type: "api"; id: number; method: "GET" | "POST" | "PUT" | "DELETE"; path: string; body?: unknown }
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
  | { type: "startSession"; packageId: string }
  | { type: "stopSession" }
  | { type: "launch"; id: string }
  | { type: "setVolume"; value: number } // 0..100
  | { type: "setMute"; muted: boolean }
  | { type: "reboot" }
  | { type: "adminExit"; pin: string };

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
    }
  | { type: "startResult"; ok: boolean; error?: string }
  | { type: "tick"; remainingSec: number; balance: number }
  | { type: "sessionEnded"; reason: string }
  | { type: "notice"; text: string } // сообщение от администратора
  | { type: "rebooting"; test: boolean } // сеанс завершён: ПК перезагружается и чистится (test — только имитация)
  | { type: "catalog"; items: CatalogItem[] }
  | { type: "launchResult"; id: string; ok: boolean; error?: string }
  | { type: "windows"; items: WindowItem[] }
  | { type: "audio"; volume: number; muted: boolean }
  | { type: "network"; online: boolean; name?: string; speedMbps?: number }
  | { type: "processes"; items: ProcessItem[]; cpu: number; memUsedMb: number; memTotalMb: number }
  | { type: "ping"; ms: number | null } // задержка до узла; null — узел не отвечает
  | { type: "speedResult"; ok: boolean; downMbps?: number; upMbps?: number; pingMs?: number | null; error?: string }
  | { type: "adminExitResult"; ok: boolean };
