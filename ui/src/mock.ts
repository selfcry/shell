import type { CatalogItem, FromHost, TariffPackage, ToHost, WindowItem } from "./protocol";

// Макет хоста для разработки в браузере. Повторяет поведение ClubShell.Locker:
// заглушка вместо сервера, тарифы взяты с mirage-ptg.ru (зона ARENA),
// каталог игр читается из того же config/catalog.json, что и настоящий хост.
const PACKAGES: TariffPackage[] = [
  { id: "h1", name: "1 час", minutes: 60, price: 140 },
  { id: "h3", name: "3 часа", minutes: 180, price: 350 },
  { id: "h5", name: "5 часов", minutes: 300, price: 500 },
  { id: "night", name: "Ночь", minutes: 600, price: 550, nightOnly: true },
];

const USERS: Record<string, { password: string; balance: number }> = {
  demo: { password: "demo", balance: 600 },
};

export class MockHost {
  private timer: number | undefined;
  private balance = 0;
  private remaining = 0;
  private volume = 40;
  private muted = false;
  private windows: WindowItem[] = [
    { id: "1", title: "Counter-Strike 2", exe: "cs2.exe", active: true, minimized: false },
    { id: "2", title: "Discord", exe: "Discord.exe", active: false, minimized: false },
    { id: "3", title: "Google Chrome — Мираж PRO", exe: "chrome.exe", active: false, minimized: true },
  ];

  constructor(private emit: (m: FromHost) => void, private role: "shell" | "taskbar" | "taskmgr" | "admin" | "explorer" | "switcher" = "shell") {
    if (role === "switcher") {
      window.setTimeout(() => {
        this.emit({ type: "switcher", selected: 0, items: this.windows.map((w) => ({ id: w.id, title: w.title, exe: w.exe })) });
        let i = 0;
        window.setInterval(() => this.emit({ type: "select", selected: ++i % this.windows.length }), 1500);
      }, 100);
    }
  }

  private procs = [
    { pid: 4120, name: "cs2.exe", title: "Counter-Strike 2", cpu: 31, memMb: 4210, app: true },
    { pid: 5532, name: "Discord.exe", title: "Discord", cpu: 3, memMb: 612, app: true },
    { pid: 6044, name: "chrome.exe", title: "Google Chrome", cpu: 6, memMb: 1380, app: true },
    { pid: 2210, name: "steamwebhelper.exe", cpu: 1, memMb: 240, app: false },
    { pid: 2288, name: "RentalGames.exe", cpu: 0, memMb: 118, app: false },
    { pid: 3011, name: "ctfmon.exe", cpu: 0, memMb: 14, app: false },
  ];

  private emitProcesses(): void {
    const items = this.procs.map((p) => ({ ...p, cpu: Math.max(0, Math.round(p.cpu + (Math.random() - 0.5) * 4)) }));
    this.emit({ type: "processes", items, cpu: Math.min(100, items.reduce((a, p) => a + p.cpu, 0)), memUsedMb: items.reduce((a, p) => a + p.memMb, 0) + 3800, memTotalMb: 16384 });
  }

  private handleTaskMgr(msg: ToHost): void {
    if (msg.type === "ready") {
      this.emitProcesses();
      window.setInterval(() => this.emitProcesses(), 1500);
    } else if (msg.type === "endTask") {
      this.procs = this.procs.filter((p) => p.pid !== msg.pid);
      this.emitProcesses();
    }
  }

  private emitWindows(): void {
    this.emit({ type: "windows", items: this.windows.map((w) => ({ ...w })) });
  }

  /** Поведение панели задач: клик по неактивному окну открывает его, по активному — сворачивает. */
  private handleTaskbar(msg: ToHost): void {
    switch (msg.type) {
      case "ready":
        this.emit({ type: "config", pcName: "PC-01", zone: "ARENA" });
        this.emit({ type: "audio", volume: this.volume, muted: this.muted });
        this.emit({ type: "network", online: true, name: "Ethernet", speedMbps: 1000 });
        this.emitWindows();
        this.emit({ type: "ping", ms: 14 });
        this.emit({ type: "session", remainingSec: 3540, balance: 460, bonus: 120 });
        this.emit({ type: "pinned", items: [{ id: "browser", title: "Браузер" }, { id: "discord", title: "Discord" }, { id: "telegram", title: "Telegram" }] });
        window.setInterval(() => this.emit({ type: "ping", ms: 10 + Math.round(Math.random() * 40) }), 4000);
        break;
      case "activateWindow": {
        const w = this.windows.find((x) => x.id === msg.id);
        if (!w) break;
        if (w.active && !w.minimized) w.minimized = true, (w.active = false);
        else {
          this.windows.forEach((x) => (x.active = false));
          w.active = true;
          w.minimized = false;
        }
        this.emitWindows();
        break;
      }
      case "closeWindow":
        this.windows = this.windows.filter((x) => x.id !== msg.id);
        this.emitWindows();
        break;
      case "speedTest":
        window.setTimeout(() => this.emit({ type: "speedResult", ok: true, downMbps: 487.3, upMbps: 212.8, pingMs: 14 }), 2500);
        break;
      case "showDesktop":
        this.windows.forEach((x) => ((x.minimized = true), (x.active = false)));
        this.emitWindows();
        break;
      case "setVolume":
        this.volume = Math.max(0, Math.min(100, Math.round(msg.value)));
        this.emit({ type: "audio", volume: this.volume, muted: this.muted });
        break;
      case "setMute":
        this.muted = msg.muted;
        this.emit({ type: "audio", volume: this.volume, muted: this.muted });
        break;
    }
  }

  handle(msg: ToHost): void {
    if (this.role === "taskbar") return this.handleTaskbar(msg);
    if (this.role === "taskmgr") return this.handleTaskMgr(msg);
    if (this.role === "admin") return handleAdmin(msg, this.emit);
    if (this.role === "explorer") return handleExplorer(msg, this.emit);
    if (this.role === "switcher") return;
    switch (msg.type) {
      case "ready":
        this.emit({ type: "config", pcName: "PC-01", zone: "ARENA" });
        this.emit({ type: "mode", mode: "locked" });
        void this.sendCatalog();
        break;
      case "login": {
        const u = USERS[msg.username];
        if (!u || u.password !== msg.password) {
          this.emit({ type: "loginResult", ok: false, error: "Неверный логин или пароль" });
          break;
        }
        this.balance = u.balance;
        this.emit({ type: "loginResult", ok: true, user: msg.username, balance: u.balance, packages: PACKAGES, bonus: 120,
          loyalty: { bonus: 120, spent: 2600, level: "Бронза", cashbackPct: 3, nextLevel: "Серебро", toNext: 2400 } });
        break;
      }
      case "shopOpen":
        window.setTimeout(() => this.emit({ type: "shop", items: [
          { id: 1, name: "Кола 0.5", category: "Напитки", price: 120, stock: 12 },
          { id: 2, name: "Энергетик Burn", category: "Напитки", price: 150, stock: 6 },
          { id: 3, name: "Чипсы Lay's", category: "Снеки", price: 140, stock: 9 },
          { id: 4, name: "Сникерс", category: "Снеки", price: 90, stock: 20 },
        ] }), 200);
        break;
      case "order": {
        const total = msg.items.reduce((s, i) => s + i.qty * ({ 1: 120, 2: 150, 3: 140, 4: 90 } as Record<number, number>)[i.productId]!, 0);
        if (total > this.balance) { this.emit({ type: "orderResult", ok: false, error: "Недостаточно средств" }); break; }
        this.balance -= total;
        window.setTimeout(() => { this.emit({ type: "balance", balance: this.balance }); this.emit({ type: "orderResult", ok: true, orderId: 7, total }); }, 300);
        break;
      }
      case "callAdmin":
        window.setTimeout(() => this.emit({ type: "callResult", ok: true, text: "Администратор вызван, он скоро подойдёт" }), 300);
        break;
      case "promo":
        window.setTimeout(() => this.emit(msg.code.toUpperCase() === "WELCOME"
          ? { type: "promoResult", ok: true, bonus: 220 }
          : { type: "promoResult", ok: false, error: "Промокод не найден" }), 300);
        break;
      case "register": {
        if (USERS[msg.username]) {
          this.emit({ type: "loginResult", ok: false, error: "Этот логин уже занят" });
          break;
        }
        USERS[msg.username] = { password: msg.password, balance: 0 };
        this.balance = 0;
        this.emit({ type: "loginResult", ok: true, user: msg.username, balance: 0, packages: PACKAGES });
        break;
      }
      case "startSession": {
        const p = PACKAGES.find((x) => x.id === msg.packageId);
        if (!p) return this.emit({ type: "startResult", ok: false, error: "Тариф не найден" });
        if (p.price > this.balance) return this.emit({ type: "startResult", ok: false, error: "Недостаточно средств" });
        this.balance -= p.price;
        this.remaining = p.minutes * 60;
        this.emit({ type: "startResult", ok: true });
        this.emit({ type: "mode", mode: "session" });
        this.emit({ type: "tick", remainingSec: this.remaining, balance: this.balance });
        this.timer = window.setInterval(() => {
          this.remaining -= 1;
          if (this.remaining <= 0) this.end("Время вышло");
          else this.emit({ type: "tick", remainingSec: this.remaining, balance: this.balance });
        }, 1000);
        break;
      }
      case "stopSession":
        this.end("Сессия завершена");
        break;
      case "launch":
        // В макете запуск имитируется: «Дота» не найдена — для проверки ошибки.
        window.setTimeout(
          () =>
            this.emit(
              msg.id === "dota2"
                ? { type: "launchResult", id: msg.id, ok: false, error: "Приложение не найдено на этом ПК" }
                : { type: "launchResult", id: msg.id, ok: true },
            ),
          500,
        );
        break;
      case "adminExit":
        this.emit({ type: "adminExitResult", ok: msg.pin === "0000" });
        break;
      case "reboot":
      case "logout":
      case "openExplorer":
        break;
    }
  }

  private async sendCatalog(): Promise<void> {
    try {
      const res = await fetch("./config/catalog.json");
      const data = (await res.json()) as { items: CatalogItem[] };
      // как и настоящий хост, не отдаём интерфейсу пути запуска
      const items = data.items.map(({ id, title, kind, group, cover, clubAccount, minAge }) => ({
        id, title, kind, group, cover, clubAccount, minAge,
      }));
      this.emit({ type: "catalog", items });
    } catch {
      this.emit({ type: "catalog", items: [] });
    }
  }

  private end(reason: string): void {
    window.clearInterval(this.timer);
    this.emit({ type: "sessionEnded", reason });
    this.emit({ type: "rebooting", test: true });
    this.emit({ type: "mode", mode: "locked" });
  }
}


// ---------- макет сервера для админ-панели (только для разработки в браузере) ----------
const now = () => Math.floor(Date.now() / 1000);
const adminData = {
  pcs: [
    { id: 1, name: "PC-01", zone: "ARENA", online: true, lastSeen: now(), session: { id: 11, guest: "demo", tariff: "h3", started: now() - 3000, remainingSec: 7800 } },
    { id: 2, name: "PC-02", zone: "ARENA", online: true, lastSeen: now(), session: null },
    { id: 3, name: "PC-03", zone: "ARENA", online: false, lastSeen: now() - 900, session: null },
    { id: 4, name: "PC-04", zone: "VIP", online: true, lastSeen: now(), session: { id: 12, guest: "vip", tariff: "h1", started: now() - 600, remainingSec: 3000 } },
  ] as Array<{ id: number; name: string; zone: string; online: boolean; lastSeen: number; session: null | { id: number; guest: string; tariff: string; started: number; remainingSec: number } }>,
  guests: [
    { id: 1, login: "demo", balance: 460, birth: null as string | null, banned: false, created: now() - 86400 * 3 },
    { id: 2, login: "vip", balance: 2000, birth: null, banned: false, created: now() - 86400 * 9 },
    { id: 3, login: "ivan_k", balance: 35, birth: "2001-04-12", banned: true, created: now() - 86400 * 30 },
  ],
  apps: [
    { id: "cs2", title: "Counter-Strike 2", kind: "game", group: "Шутеры", cover: null as string | null, paths: ["find:RentalGames.exe"], args: "-idgame 730 -autostart -user {user}", clubAccount: true, minAge: null as number | null, enabled: true, pinned: false, sort: 10 },
    { id: "valorant", title: "VALORANT", kind: "game", group: "Шутеры", cover: null, paths: ["C:\\Riot Games\\Riot Client\\RiotClientServices.exe"], args: null, clubAccount: false, minAge: 16, enabled: true, pinned: false, sort: 20 },
    { id: "browser", title: "Браузер", kind: "app", group: "Интернет", cover: null, paths: ["find:chrome.exe", "find:msedge.exe"], args: null, clubAccount: false, minAge: null, enabled: true, pinned: true, sort: 10 },
    { id: "discord", title: "Discord", kind: "app", group: "Общение", cover: null, paths: ["%LOCALAPPDATA%\\Discord\\Update.exe"], args: "--processStart Discord.exe", clubAccount: false, minAge: null, enabled: true, pinned: true, sort: 20 },
    { id: "telegram", title: "Telegram", kind: "app", group: "Общение", cover: null, paths: ["%APPDATA%\\Telegram Desktop\\Telegram.exe"], args: null, clubAccount: false, minAge: null, enabled: true, pinned: true, sort: 30 },
  ] as Array<{ id: string; title: string; kind: string; group: string | null; cover: string | null; paths: string[]; args: string | null; clubAccount: boolean; minAge: number | null; enabled: boolean; pinned: boolean; sort: number }>,
  blocked: [{ id: 1, process: "cheatengine-x86_64.exe", note: "читы" }] as Array<{ id: number; process: string; note: string | null }>,
  tariffs: [
    { id: "h1", name: "1 час", minutes: 60, price: 140, nightOnly: false, zone: "ARENA", active: true, sort: 1 },
    { id: "h3", name: "3 часа", minutes: 180, price: 350, nightOnly: false, zone: "ARENA", active: true, sort: 2 },
    { id: "h5", name: "5 часов", minutes: 300, price: 500, nightOnly: false, zone: "ARENA", active: true, sort: 3 },
    { id: "night", name: "Ночь", minutes: 600, price: 550, nightOnly: true, zone: "ARENA", active: true, sort: 4 },
  ],
};

function handleAdmin(msg: ToHost, emit: (m: FromHost) => void): void {
  if (msg.type === "ready") {
    emit({ type: "settings", serverUrl: "http://mock-server:5080", hasKey: true });
    return;
  }
  if (msg.type === "saveSettings") {
    emit({ type: "settings", serverUrl: msg.serverUrl, hasKey: true });
    return;
  }
  if (msg.type !== "api") return;
  const path = msg.path.replace("/api/admin", "");
  const [route, query = ""] = path.split("?");
  const body = (msg.body ?? {}) as Record<string, unknown>;
  const reply = (data: unknown, status = 200): void => {
    window.setTimeout(() => emit({ type: "apiResult", id: msg.id, status, body: JSON.stringify(data) }), 120);
  };
  const parts = route!.split("/").filter(Boolean);

  if (msg.method === "GET" && route === "/audit")
    return reply([{ id: 2, ts: now() - 300, ip: "192.168.1.10", action: "Пополнение", details: "гость #1: +500 ₽, баланс 960 ₽" }, { id: 1, ts: now() - 900, ip: "192.168.1.10", action: "POST /api/admin/pcs/2/command", details: null }]);
  if (msg.method === "GET" && route === "/stats")
    return reply({ pcs: adminData.pcs.length, activeSessions: adminData.pcs.filter((p) => p.session).length, guests: adminData.guests.length, revenueToday: 1240, topupsToday: 3000 });
  if (msg.method === "GET" && route === "/pcs") return reply(adminData.pcs);
  if (msg.method === "GET" && route === "/guests") {
    const q = new URLSearchParams(query).get("q")?.toLowerCase() ?? "";
    return reply(adminData.guests.filter((g) => g.login.includes(q)));
  }
  if (msg.method === "GET" && route === "/tariffs") return reply(adminData.tariffs);
  if (msg.method === "GET" && route === "/apps") return reply(adminData.apps);
  if (msg.method === "PUT" && route === "/apps") {
    const b = body as unknown as (typeof adminData.apps)[number];
    const i = adminData.apps.findIndex((x) => x.id === b.id);
    if (i >= 0) adminData.apps[i] = { ...adminData.apps[i]!, ...b, cover: adminData.apps[i]!.cover };
    else adminData.apps.push({ ...b, cover: null });
    return reply({ ok: true });
  }
  if (msg.method === "DELETE" && parts[0] === "apps" && parts[2] === "cover") {
    const x = adminData.apps.find((q) => q.id === parts[1]);
    if (x) x.cover = null;
    return reply({ ok: true });
  }
  if (msg.method === "DELETE" && parts[0] === "apps") {
    adminData.apps = adminData.apps.filter((x) => x.id !== parts[1]);
    return reply({ ok: true });
  }
  if (msg.method === "UPLOAD" && parts[0] === "apps") return reply({ ok: true, cover: "/covers/mock.png" });
  if (msg.method === "GET" && route === "/blocked") return reply(adminData.blocked);
  if (msg.method === "POST" && route === "/blocked") {
    adminData.blocked.push({ id: Date.now(), process: String(body.process), note: (body.note as string) || null });
    return reply({ ok: true });
  }
  if (msg.method === "DELETE" && parts[0] === "blocked") {
    adminData.blocked = adminData.blocked.filter((x) => x.id !== Number(parts[1]));
    return reply({ ok: true });
  }
  if (msg.method === "GET" && route === "/sessions") {
    const rows = adminData.pcs.filter((p) => p.session).map((p) => ({ id: p.session!.id, guestId: 1, guest: p.session!.guest, pcId: p.id, pc: p.name, tariffId: p.session!.tariff, started: p.session!.started, ends: now() + p.session!.remainingSec, ended: null, price: 350, reason: null }));
    const old = [{ id: 10, guestId: 2, guest: "vip", pcId: 4, pc: "PC-04", tariffId: "h5", started: now() - 90000, ends: now() - 72000, ended: now() - 72000, price: 500, reason: "Время вышло" }];
    return reply(new URLSearchParams(query).get("active") === "true" ? rows : [...rows, ...old]);
  }
  if (msg.method === "GET" && parts[0] === "guests" && parts[2] === "transactions")
    return reply([{ id: 3, amount: 100, kind: "topup", note: "касса", ts: now() - 600 }, { id: 2, amount: -350, kind: "session", note: "3 часа · PC-01", ts: now() - 3000 }]);
  if (msg.method === "POST" && route === "/pcs") {
    const id = adminData.pcs.length + 1;
    adminData.pcs.push({ id, name: String(body.name), zone: String(body.zone ?? "ARENA"), online: false, lastSeen: 0, session: null });
    return reply({ ok: true, id, name: body.name, zone: body.zone ?? "ARENA", token: "MOCK-TOKEN-" + id + "-abcdefghijklmnopqrstuvwxyz" });
  }
  if (msg.method === "POST" && parts[0] === "guests" && parts[2] === "topup") {
    const g = adminData.guests.find((x) => x.id === Number(parts[1]));
    if (!g) return reply({ ok: false, error: "Гость не найден" }, 404);
    if (g.balance + Number(body.amount) < 0) return reply({ ok: false, error: "Недостаточно средств для списания" }, 400);
    g.balance += Number(body.amount);
    return reply({ ok: true, balance: g.balance });
  }
  if (msg.method === "POST" && route === "/guests") {
    adminData.guests.push({ id: adminData.guests.length + 1, login: String(body.login), balance: Number(body.balance ?? 0), birth: null, banned: false, created: now() });
    return reply({ ok: true });
  }
  if (msg.method === "PUT" && parts[0] === "guests") {
    const g = adminData.guests.find((x) => x.id === Number(parts[1]));
    if (g && typeof body.banned === "boolean") g.banned = body.banned;
    return reply({ ok: true });
  }
  if (msg.method === "PUT" && route === "/tariffs") {
    const t = body as unknown as (typeof adminData.tariffs)[number];
    const i = adminData.tariffs.findIndex((x) => x.id === t.id);
    if (i >= 0) adminData.tariffs[i] = { ...t, active: t.active !== false };
    else adminData.tariffs.push({ ...t, active: t.active !== false });
    return reply({ ok: true });
  }
  if (msg.method === "DELETE" && parts[0] === "tariffs") {
    const t = adminData.tariffs.find((x) => x.id === parts[1]);
    if (t) t.active = false;
    return reply({ ok: true });
  }
  if (msg.method === "DELETE" && parts[0] === "pcs") {
    adminData.pcs = adminData.pcs.filter((p) => p.id !== Number(parts[1]));
    return reply({ ok: true });
  }
  return reply({ ok: true, delivered: true, remainingSec: 3600 });
}


// ---------- макет файлов гостя (только для разработки в браузере) ----------
const fsTree: Record<string, { name: string; isDir: boolean; size: number; modified: number }[]> = {
  "C:/Users/guest/Downloads": [
    { name: "Установщики", isDir: true, size: 0, modified: 1760000000 },
    { name: "report.pdf", isDir: false, size: 2_400_000, modified: 1760100000 },
    { name: "setup-discord.exe", isDir: false, size: 94_000_000, modified: 1760200000 },
    { name: "фото.png", isDir: false, size: 820_000, modified: 1760300000 },
  ],
  "C:/Users/guest/Downloads/Установщики": [{ name: "readme.txt", isDir: false, size: 1200, modified: 1760000100 }],
  "C:/Users/guest/Documents": [{ name: "Курсовая.docx", isDir: false, size: 56_000, modified: 1760400000 }],
  "C:/Users/guest/Desktop": [],
};

function handleExplorer(msg: ToHost, emit: (m: FromHost) => void): void {
  if (msg.type !== "fs") return;
  const reply = (r: Partial<Extract<FromHost, { type: "fsResult" }>>): void => {
    window.setTimeout(() => emit({ type: "fsResult", id: msg.id, ok: true, ...r }), 80);
  };
  if (msg.op === "roots") return reply({ roots: [
    { name: "Загрузки", path: "C:/Users/guest/Downloads" }, { name: "Документы", path: "C:/Users/guest/Documents" }, { name: "Рабочий стол", path: "C:/Users/guest/Desktop" }, { name: "Флешка (E:)", path: "E:/" } ] });
  if (msg.op === "list") return fsTree[msg.path ?? ""] ? reply({ path: msg.path, entries: fsTree[msg.path!] }) : reply({ ok: true, path: msg.path, entries: [] });
  if (msg.op === "mkdir" && msg.path) { (fsTree[msg.path] ??= []).unshift({ name: msg.name ?? "Новая папка", isDir: true, size: 0, modified: Date.now() / 1000 }); return reply({}); }
  if (msg.op === "delete" && msg.path) {
    const i = msg.path.lastIndexOf("/");
    const dir = msg.path.slice(0, i);
    fsTree[dir] = (fsTree[dir] ?? []).filter((e) => e.name !== msg.path!.slice(i + 1));
    return reply({});
  }
  return reply({});
}
