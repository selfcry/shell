import "@fontsource/inter/400.css";
import "@fontsource/inter/700.css";
import "@fontsource/russo-one/400.css";
import "./styles.css";
import "./admin.css";

import { isLiveAdmin, onMessage, send } from "./bridge";
import { el, formatTime } from "./dom";
import type { FromHost } from "./protocol";

// Админ-панель клуба. Все запросы идут через хост (ClubShell.Admin): он добавляет ключ администратора.

// ---------- типы ответов сервера ----------
interface Stats { pcs: number; activeSessions: number; guests: number; revenueToday: number; topupsToday: number }
interface PcRow {
  id: number; name: string; zone: string; online: boolean; lastSeen: number;
  session: null | { id: number; guest: string; tariff: string; started: number; remainingSec: number };
  booking?: null | { id: number; guest: string; starts: number; ends: number };
  call?: null | { ts: number; guest: string | null };
}
interface GuestRow { id: number; login: string; balance: number; birth: string | null; banned: boolean; created: number; bonus?: number; phone?: string | null }
interface TariffRow { id: string; name: string; minutes: number; price: number; nightOnly: boolean; zone: string; active: boolean; sort: number }
interface SessionRow { id: number; guestId: number; guest: string; pcId: number; pc: string; tariffId: string; started: number; ends: number; ended: number | null; price: number; reason: string | null }
interface TxRow { id: number; amount: number; kind: string; note: string | null; ts: number }
interface ApiReply<T> { status: number; data: T }

// ---------- вызовы API через хост ----------
let nextId = 1;
const pending = new Map<number, (r: { status: number; body: string }) => void>();

function api<T = { ok?: boolean; error?: string }>(method: "GET" | "POST" | "PUT" | "DELETE" | "UPLOAD", path: string, body?: unknown): Promise<ApiReply<T>> {
  return new Promise((resolve) => {
    const id = nextId++;
    // ответ не пришёл — не висим вечно: статус 0, как «сервер недоступен» (загрузка ждёт выбор файла дольше)
    const timer = window.setTimeout(() => {
      if (pending.delete(id)) resolve({ status: 0, data: { ok: false, error: "Сервер не ответил" } as T });
    }, method === "UPLOAD" ? 180_000 : 15_000);
    pending.set(id, (r) => {
      window.clearTimeout(timer);
      let data: unknown = {};
      try { data = JSON.parse(r.body); } catch { /* не JSON */ }
      resolve({ status: r.status, data: data as T });
    });
    send({ type: "api", id, method, path, body });
  });
}

// ---------- мелкие помощники ----------
const app = document.getElementById("app")!;
const rub = (n: number) => `${n.toLocaleString("ru-RU")} ₽`;
const when = (ts: number) => new Date(ts * 1000).toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", year: "2-digit", hour: "2-digit", minute: "2-digit" });

/** Кнопка. Если обработчик асинхронный, кнопка выключена до его завершения: двойной клик не проведёт оплату дважды. */
function button(text: string, cls = "", onClick?: () => unknown): HTMLButtonElement {
  const b = el("button", { class: "ad-btn " + cls, text });
  if (onClick) b.addEventListener("click", () => {
    if (b.disabled) return;
    const r = onClick();
    if (r instanceof Promise) {
      b.disabled = true;
      void r.finally(() => (b.disabled = false));
    }
  });
  return b;
}

/** Целое неотрицательное число из поля или null. */
function intOf(input: HTMLInputElement): number | null {
  const v = input.value.trim();
  if (!/^\d+$/.test(v)) return null;
  const n = Number(v);
  return Number.isSafeInteger(n) ? n : null;
}

const newRequestId = (): string => (globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`);

const toastEl = el("div", { class: "ad-toast" });
let toastTimer: number | undefined;
function toast(text: string, kind: "ok" | "error" = "ok"): void {
  toastEl.textContent = text;
  toastEl.className = `ad-toast show ${kind}`;
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => (toastEl.className = "ad-toast"), 3200);
}

/** Показывает окно с содержимым. Возвращает функцию закрытия. */
function modal(title: string, content: Node[], actions: HTMLElement[] = []): () => void {
  const close = (): void => overlay.remove();
  const box = el("div", { class: "ad-modal" }, [
    el("div", { class: "ad-modal-head" }, [el("div", { class: "ad-modal-title", text: title }), button("×", "icon", close)]),
    el("div", { class: "ad-modal-body" }, content),
    actions.length ? el("div", { class: "ad-modal-actions" }, actions) : el("span"),
  ]);
  const overlay = el("div", { class: "ad-overlay" }, [box]);
  overlay.addEventListener("mousedown", (e) => e.target === overlay && close());
  document.body.append(overlay);
  return close;
}

function confirmBox(title: string, text: string, okText: string, onOk: () => void): void {
  const close = modal(title, [el("p", { class: "ad-text", text })], [
    button("Отмена", "", () => close()),
    button(okText, "danger", () => { close(); onOk(); }),
  ]);
}

function field(label: string, input: HTMLElement): HTMLElement {
  return el("label", { class: "ad-field" }, [el("span", { text: label }), input]);
}
const textInput = (value = "", type = "text", placeholder = "") => el("input", { class: "ad-input", attrs: { type, value, placeholder, autocomplete: "off" } });

async function run(promise: Promise<ApiReply<{ ok?: boolean; error?: string }>>, success: string, after?: () => void): Promise<boolean> {
  const r = await promise;
  if (r.status === 200 && r.data.ok !== false) {
    toast(success);
    after?.();
    return true;
  }
  toast(r.data.error ?? (r.status === 401 ? "Неверный ключ администратора" : "Ошибка запроса"), "error");
  return false;
}

// ---------- каркас ----------
type ViewName = "dashboard" | "pcs" | "shift" | "guests" | "bookings" | "tariffs" | "sessions" | "loyalty" | "analytics" | "apps" | "blocked" | "audit" | "settings";
const VIEWS: { id: ViewName; title: string }[] = [
  { id: "dashboard", title: "Дашборд" },
  { id: "pcs", title: "Зал" },
  { id: "shift", title: "Смена и касса" },
  { id: "guests", title: "Гости" },
  { id: "bookings", title: "Брони" },
  { id: "tariffs", title: "Тарифы" },
  { id: "sessions", title: "Сеансы" },
  { id: "loyalty", title: "Бонусы и промокоды" },
  { id: "analytics", title: "Аналитика" },
  { id: "apps", title: "Игры и приложения" },
  { id: "blocked", title: "Запреты" },
  { id: "audit", title: "Журнал" },
  { id: "settings", title: "Настройки" },
];

let serverUrl = "";
let current: ViewName = "dashboard";
let timer: number | undefined;
let renderGen = 0; // устаревшие ответы не должны перерисовывать уже закрытую вкладку

const nav = el("nav", { class: "ad-nav" });
const content = el("main", { class: "ad-main" });
const serverLabel = el("div", { class: "ad-server" });
const shell = el("div", { class: "ad-shell" }, [
  el("aside", { class: "ad-side" }, [
    el("div", { class: "ad-brand" }, [el("span", { class: "brand-gradient", text: "МИРАЖ" }), el("small", { text: "PRO · админ" })]),
    nav,
    el("div", { class: "ad-side-foot" }, [serverLabel, isLiveAdmin
      ? button("Выйти", "ghost", () => send({ type: "saveSettings", serverUrl, key: "" }))
      : button("Сменить сервер", "ghost", () => showConnect())]),
  ]),
  content,
]);

function buildNav(): void {
  nav.replaceChildren(
    ...VIEWS.map((v) => {
      const b = el("button", { class: "ad-nav-item" + (v.id === current ? " active" : ""), text: v.title });
      b.addEventListener("click", () => go(v.id));
      return b;
    }),
  );
}

function go(view: ViewName): void {
  current = view;
  buildNav();
  content.replaceChildren(el("div", { class: "ad-empty", text: "Загрузка…" }));
  void render();
  window.clearInterval(timer);
  // дашборд, ПК и сеансы обновляются сами, гости и тарифы — по действию (иначе сбивается ввод)
  if (view === "dashboard" || view === "pcs" || view === "sessions" || view === "shift") timer = window.setInterval(() => void render(true), 3000);
}

async function render(silent = false): Promise<void> {
  const gen = ++renderGen;
  const views: Record<ViewName, () => Promise<HTMLElement | null>> = {
    dashboard: viewDashboard, pcs: viewPcs, shift: viewShift, guests: viewGuests, bookings: viewBookings, tariffs: viewTariffs, sessions: viewSessions,
    loyalty: viewLoyalty, analytics: viewAnalytics, apps: viewApps, blocked: viewBlocked, audit: viewAudit, settings: viewSettings,
  };
  const node = await views[current]();
  if (gen !== renderGen || !node) return;
  if (silent && document.querySelector(".ad-overlay")) return; // не перерисовываем под открытым окном
  content.replaceChildren(node);
}

function header(title: string, ...actions: HTMLElement[]): HTMLElement {
  return el("div", { class: "ad-head" }, [el("h1", { text: title }), el("div", { class: "ad-head-actions" }, actions)]);
}

function table(cols: string[], rows: (Node | string)[][], empty = "Нет данных"): HTMLElement {
  if (rows.length === 0) return el("div", { class: "ad-empty", text: empty });
  return el("table", { class: "ad-table" }, [
    el("thead", {}, [el("tr", {}, cols.map((c) => el("th", { text: c })))]),
    el("tbody", {}, rows.map((r) => el("tr", {}, r.map((c) => el("td", {}, [c]))))),
  ]);
}

const badge = (text: string, kind = "") => el("span", { class: "ad-badge " + kind, text });

// ---------- дашборд ----------
async function viewDashboard(): Promise<HTMLElement | null> {
  const [s, p, ss] = await Promise.all([
    api<Stats>("GET", "/api/admin/stats"),
    api<PcRow[]>("GET", "/api/admin/pcs"),
    api<SessionRow[]>("GET", "/api/admin/sessions"),
  ]);
  if (s.status !== 200) return connectionProblem(s.status);
  const pcs = Array.isArray(p.data) ? p.data : [];
  const online = pcs.filter((x) => x.online).length;
  const stat = (label: string, value: string, hint = "") =>
    el("div", { class: "ad-stat" }, [el("div", { class: "ad-stat-label", text: label }), el("div", { class: "ad-stat-value", text: value }), el("div", { class: "ad-stat-hint", text: hint })]);

  const recent = (Array.isArray(ss.data) ? ss.data : []).slice(0, 8);
  return el("div", {}, [
    header("Дашборд"),
    el("div", { class: "ad-stats" }, [
      stat("Компьютеры онлайн", `${online} / ${pcs.length}`, "на связи с сервером"),
      stat("Идут сеансы", String(s.data.activeSessions), "сейчас играют"),
      stat("Выручка сегодня", rub(s.data.revenueToday), "списано за сеансы"),
      stat("Пополнения сегодня", rub(s.data.topupsToday), "в кассу"),
      stat("Гостей в базе", String(s.data.guests)),
    ]),
    el("div", { class: "ad-cols" }, [
      el("section", { class: "ad-card" }, [el("h2", { text: "Компьютеры" }), pcMiniList(pcs)]),
      el("section", { class: "ad-card" }, [
        el("h2", { text: "Последние сеансы" }),
        table(["Гость", "ПК", "Начало", "Статус"], recent.map((x) => [x.guest, x.pc, when(x.started), x.ended ? badge(x.reason ?? "завершён") : badge("идёт", "ok")]), "Сеансов ещё не было"),
      ]),
    ]),
  ]);
}

function pcMiniList(pcs: PcRow[]): HTMLElement {
  if (pcs.length === 0) return el("div", { class: "ad-empty", text: "ПК ещё не добавлены" });
  return el("div", { class: "ad-mini" }, pcs.map((p) =>
    el("div", { class: "ad-mini-row" }, [
      el("span", { class: "ad-dot " + (p.online ? (p.session ? "busy" : "on") : "off") }),
      el("b", { text: p.name }),
      el("span", { class: "ad-muted", text: p.session ? `${p.session.guest} · ${formatTime(p.session.remainingSec)}` : p.online ? "свободен" : "не в сети" }),
    ])));
}

function connectionProblem(status: number): HTMLElement {
  const wrap = el("div", { class: "ad-problem" }, [
    el("h2", { text: status === 401 ? "Неверный ключ администратора" : "Нет связи с сервером" }),
    el("p", { class: "ad-muted", text: status === 401 ? "Проверьте ключ в файле admin-key.txt на сервере." : `Проверьте адрес сервера (${serverUrl}) и что сервер запущен.` }),
    button("Изменить подключение", "primary", () => showConnect()),
  ]);
  return wrap;
}

// ---------- компьютеры ----------
async function viewPcs(): Promise<HTMLElement | null> {
  const r = await api<PcRow[]>("GET", "/api/admin/pcs");
  if (r.status !== 200) return connectionProblem(r.status);
  const hhmm = (ts: number) => new Date(ts * 1000).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
  const card = (p: PcRow) => {
    const state = p.call ? "call" : p.online ? (p.session ? (p.session.remainingSec < 600 ? "busy low" : "busy") : p.booking && p.booking.starts - Date.now() / 1000 < 900 ? "booked" : "free") : "off";
    const time = el("div", { class: "ad-pc-time", text: p.session ? formatTime(p.session.remainingSec) : "—" });
    if (p.session) time.dataset.left = String(p.session.remainingSec); // тикает каждую секунду до следующего обновления
    const c = el("button", { class: "ad-pc " + state }, [
      el("div", { class: "ad-pc-top" }, [el("b", { text: p.name }), p.call ? el("span", { class: "ad-zone ad-call", text: "🔔 зовёт" }) : el("span", { class: "ad-zone", text: p.online ? "" : "нет связи" })]),
      el("div", { class: "ad-pc-guest", text: p.session ? p.session.guest : p.online ? "Свободен" : "Не в сети" }),
      time,
      p.booking ? el("div", { class: "ad-pc-book", text: `бронь ${hhmm(p.booking.starts)}–${hhmm(p.booking.ends)} · ${p.booking.guest}` }) : el("span"),
    ]);
    c.addEventListener("click", () => openPc(p));
    return c;
  };
  const zones = [...new Set(r.data.map((p) => p.zone))].sort();
  const busy = r.data.filter((p) => p.session).length, online = r.data.filter((p) => p.online).length;
  return el("div", {}, [
    header("Зал", button("Добавить ПК", "primary", addPc)),
    el("div", { class: "ad-legend" }, [
      el("span", { class: "ad-muted", text: `Занято ${busy} из ${r.data.length}, на связи ${online}` }),
      ...[["free", "свободен"], ["busy", "сеанс"], ["low", "меньше 10 минут"], ["booked", "скоро бронь"], ["call", "зовёт админа"], ["off", "нет связи"]].map(([k, t]) => el("span", { class: "ad-leg " + k, text: t })),
    ]),
    r.data.length
      ? el("div", {}, zones.map((z) => el("section", { class: "ad-zone-block" }, [el("h2", { text: `Зона ${z}` }), el("div", { class: "ad-pcs" }, r.data.filter((p) => p.zone === z).map(card))])))
      : el("div", { class: "ad-empty", text: "ПК ещё не добавлены. Нажмите «Добавить ПК» — появится команда установки для этого компьютера." }),
  ]);
}

function openPc(p: PcRow): void {
  if (p.call) void api("POST", `/api/admin/calls/${p.id}/ack`).then(() => refreshCalls()); // администратор подошёл
  const base = `/api/admin/pcs/${p.id}/command`;
  const cmd = (name: string, text?: string, ok = "Команда отправлена") =>
    run(api("POST", base, { name, text }).then((r) => {
      const d = r.data as { delivered?: boolean };
      if (r.status === 200 && d.delivered === false) toast("ПК не на связи: команда не доставлена", "error");
      return r;
    }), ok, () => void render());

  const rows: HTMLElement[] = [];
  const info = el("div", { class: "ad-kv" }, [
    el("div", {}, [el("span", { text: "Зона" }), el("b", { text: p.zone })]),
    el("div", {}, [el("span", { text: "Состояние" }), el("b", { text: p.online ? "в сети" : "не в сети" })]),
    el("div", {}, [el("span", { text: "Гость" }), el("b", { text: p.session?.guest ?? "—" })]),
    el("div", {}, [el("span", { text: "Осталось" }), el("b", { text: p.session ? formatTime(p.session.remainingSec) : "—" })]),
  ]);
  rows.push(info);

  if (p.session) {
    const sid = p.session.id;
    const addTime = (m: number) => button(`${m > 0 ? "+" : ""}${m} мин`, "", () => void run(api("POST", `/api/admin/sessions/${sid}/add-time`, { minutes: m }), "Время изменено", () => void render()));
    rows.push(el("div", { class: "ad-group" }, [el("span", { class: "ad-muted", text: "Время сеанса" }), el("div", { class: "ad-btnrow" }, [addTime(-15), addTime(15), addTime(30), addTime(60)])]));
  }
  rows.push(el("div", { class: "ad-btnrow wrap" }, [
    button("Заблокировать", "", () => void cmd("lock")),
    ...(p.session ? [button("Завершить сеанс", "danger", () => confirmBox("Завершить сеанс?", `Гость ${p.session!.guest} будет отключён, ПК перезагрузится.`, "Завершить", () => void cmd("endSession")))] : []),
    button("Сообщение", "", () => {
      const input = textInput("", "text", "Текст для гостя");
      const close = modal(`Сообщение для ${p.name}`, [field("Текст", input)], [button("Отправить", "primary", () => { if (input.value.trim()) { close(); void cmd("message", input.value.trim(), "Сообщение отправлено"); } })]);
      input.focus();
    }),
    button("Перезагрузить", "", () => confirmBox("Перезагрузить ПК?", `${p.name} будет перезагружен.`, "Перезагрузить", () => void cmd("reboot"))),
    button("Выключить", "", () => confirmBox("Выключить ПК?", `${p.name} будет выключен.`, "Выключить", () => void cmd("shutdown"))),
    button("Новый токен", "", () => confirmBox("Выпустить новый токен?", `Старый токен ${p.name} перестанет работать сразу. Новый нужно будет вписать на этом ПК.`, "Выпустить", async () => {
      const r = await api<{ ok: boolean; error?: string; token?: string }>("POST", `/api/admin/pcs/${p.id}/token`);
      if (r.status !== 200 || !r.data.token) return toast(r.data.error ?? "Не удалось", "error");
      showToken(p.name, r.data.token);
    })),
    button("Удалить из системы", "danger", () => confirmBox("Удалить ПК?", `${p.name} больше не сможет подключиться к серверу.`, "Удалить", () => void run(api("DELETE", `/api/admin/pcs/${p.id}`), "ПК удалён", () => void render()))),
  ]));
  modal(p.name, rows);
}

function addPc(): void {
  const name = textInput("", "text", "PC-05");
  const zone = textInput("ARENA", "text", "ARENA");
  const close = modal("Новый компьютер", [field("Имя", name), field("Зона", zone)], [
    button("Создать", "primary", async () => {
      const r = await api<{ ok: boolean; error?: string; name?: string; token?: string }>("POST", "/api/admin/pcs", { name: name.value.trim(), zone: zone.value.trim() || "ARENA" });
      if (r.status !== 200 || !r.data.ok) return toast(r.data.error ?? "Не удалось создать ПК", "error");
      close();
      showToken(r.data.name ?? name.value, r.data.token ?? "");
      void render();
    }),
  ]);
  name.focus();
}

function showToken(name: string, token: string): void {
  const ps = (v: string) => `'${v.replace(/'/g, "''")}'`; // строка PowerShell в одинарных кавычках: без подстановок
  const command = `.\\scripts\\install.ps1 -PcToken ${ps(token)} -PcName ${ps(name)} -ServerUrl ${ps(serverUrl)}`;
  const snippet = `"ServerUrl": "${serverUrl}",\n"PcToken": "${token}"`;
  const copyBtn = (label: string, text: string, cls = "") => button(label, cls, async () => {
    try { await navigator.clipboard.writeText(text); toast("Скопировано"); } catch { toast("Не удалось скопировать: выделите текст вручную", "error"); }
  });
  modal(`Токен для ${name}`, [
    el("p", { class: "ad-text", text: "Токен показывается один раз: на сервере хранится только его хеш. На этом компьютере в папке с программой запустите PowerShell от администратора и выполните команду:" }),
    el("pre", { class: "ad-code", text: command }),
    el("p", { class: "ad-muted", text: "Если служба уже установлена, можно вместо этого вписать две строки в C:\\ProgramData\\ClubShell\\config.json и перезапустить службу ClubShell:" }),
    el("pre", { class: "ad-code", text: snippet }),
  ], [copyBtn("Копировать строки", snippet), copyBtn("Копировать команду", command, "primary")]);
}

// ---------- гости ----------
let guestQuery = "";

async function viewGuests(): Promise<HTMLElement | null> {
  const r = await api<GuestRow[]>("GET", "/api/admin/guests" + (guestQuery ? `?q=${encodeURIComponent(guestQuery)}` : ""));
  if (r.status !== 200) return connectionProblem(r.status);
  const search = textInput(guestQuery, "text", "Поиск по логину");
  search.addEventListener("keydown", (e) => { if ((e as KeyboardEvent).key === "Enter") { guestQuery = search.value.trim(); void render(); } });
  const rows = r.data.map((g) => [
    el("div", {}, [el("b", { text: g.login }), g.phone ? el("div", { class: "ad-muted ad-small", text: g.phone }) : el("span")]),
    rub(g.balance),
    el("span", { class: "ad-muted", text: `${g.bonus ?? 0} б.` }),
    g.banned ? badge("заблокирован", "bad") : badge("активен", "ok"),
    when(g.created),
    el("div", { class: "ad-btnrow" }, [
      button("Пополнить", "primary", () => moneyDialog(g, 1)),
      button("Списать", "", () => moneyDialog(g, -1)),
      button("Бонусы", "", () => bonusDialog(g)),
      button("Телефон", "", () => phoneDialog(g)),
      button("История", "", () => void history(g)),
      button(g.banned ? "Разблокировать" : "Блок", "", () => void run(api("PUT", `/api/admin/guests/${g.id}`, { banned: !g.banned }), g.banned ? "Разблокирован" : "Заблокирован", () => void render())),
      button("Пароль", "", () => passwordDialog(g)),
    ]),
  ]);
  return el("div", {}, [
    header("Гости", search, button("Новый гость", "primary", newGuest)),
    table(["Логин", "Баланс", "Бонусы", "Статус", "Создан", ""], rows, "Гости не найдены"),
  ]);
}

function newGuest(): void {
  const login = textInput("", "text", "Логин");
  const pass = textInput("", "text", "Пароль (от 6 символов)");
  const bal = textInput("0", "number", "0");
  const close = modal("Новый гость", [field("Логин", login), field("Пароль", pass), field("Начальный баланс, ₽", bal)], [
    button("Создать", "primary", async () => {
      const balance = bal.value.trim() === "" ? 0 : intOf(bal);
      if (balance === null) return toast("Начальный баланс — целое число рублей", "error");
      const ok = await run(api("POST", "/api/admin/guests", { login: login.value.trim(), password: pass.value, balance }), "Гость создан", () => void render());
      if (ok) close();
    }),
  ]);
  login.focus();
}

function moneyDialog(g: GuestRow, sign: 1 | -1): void {
  const requestId = newRequestId(); // один на окно: повторное нажатие сервер не проведёт второй раз
  const amount = textInput("100", "number");
  const note = textInput("", "text", "Комментарий (необязательно)");
  const method = el("select", { class: "ad-input" }, [["cash", "Наличные"], ["card", "Карта"], ["sbp", "СБП"], ["", "Без оплаты (бонус, исправление)"]].map(([v, t]) => el("option", { attrs: { value: v }, text: t })));
  const close = modal(`${sign > 0 ? "Пополнить" : "Списать"}: ${g.login}`, [
    el("p", { class: "ad-muted", text: `Сейчас на балансе ${rub(g.balance)}${shiftOpen ? "" : " · смена не открыта: операция не попадёт в отчёт смены"}` }),
    field("Сумма, ₽", amount), field(sign > 0 ? "Как оплатил" : "Как вернули", method), field("Комментарий", note)], [
    button(sign > 0 ? "Пополнить" : "Списать", "primary", async () => {
      const n = intOf(amount);
      if (!n) return toast("Сумма — целое число рублей больше нуля", "error");
      const after = g.balance + n * sign;
      const go = async () => {
        const ok = await run(api("POST", `/api/admin/guests/${g.id}/topup`, { amount: n * sign, note: note.value.trim() || null, requestId, method: (method as HTMLSelectElement).value || null }), "Готово", () => void render());
        if (ok) close();
      };
      // крупная сумма — лишняя проверка от опечатки (10000 вместо 1000)
      if (n >= 5000) return confirmBox(`${sign > 0 ? "Пополнить" : "Списать"} ${rub(n)}?`, `Баланс ${g.login}: ${rub(g.balance)} → ${rub(after)}`, "Да, провести", () => void go());
      await go();
    }),
  ]);
  amount.select();
}

function passwordDialog(g: GuestRow): void {
  const pass = textInput("", "text", "Новый пароль");
  const close = modal(`Пароль: ${g.login}`, [field("Новый пароль (от 6 символов)", pass)], [
    button("Сменить", "primary", async () => { if (await run(api("PUT", `/api/admin/guests/${g.id}`, { password: pass.value }), "Пароль изменён")) close(); }),
  ]);
  pass.focus();
}

async function history(g: GuestRow): Promise<void> {
  const r = await api<TxRow[]>("GET", `/api/admin/guests/${g.id}/transactions`);
  if (r.status !== 200) return void toast(r.status === 401 ? "Неверный ключ администратора" : "Не удалось загрузить историю", "error");
  const rows = (Array.isArray(r.data) ? r.data : []).map((t) => [when(t.ts), el("b", { class: t.amount > 0 ? "ad-plus" : "ad-minus", text: `${t.amount > 0 ? "+" : ""}${t.amount} ₽` }), t.kind === "session" ? "сеанс" : t.kind === "topup" ? "пополнение" : "списание", t.note ?? ""]);
  modal(`История: ${g.login}`, [table(["Когда", "Сумма", "Тип", "Комментарий"], rows, "Операций нет")]);
}

// ---------- тарифы ----------
async function viewTariffs(): Promise<HTMLElement | null> {
  const r = await api<TariffRow[]>("GET", "/api/admin/tariffs");
  if (r.status !== 200) return connectionProblem(r.status);
  const rows = r.data.map((t) => [
    el("b", { text: t.name }),
    t.zone,
    t.minutes % 60 === 0 ? `${t.minutes / 60} ч` : `${t.minutes} мин`,
    rub(t.price),
    t.nightOnly ? badge("22:00–08:00", "warn") : "весь день",
    t.active ? badge("включён", "ok") : badge("выключен"),
    el("div", { class: "ad-btnrow" }, [
      button("Изменить", "", () => tariffDialog(t)),
      ...(t.active ? [button("Выключить", "", () => void run(api("DELETE", `/api/admin/tariffs/${encodeURIComponent(t.id)}`), "Тариф выключен", () => void render()))] : []),
    ]),
  ]);
  return el("div", {}, [header("Тарифы", button("Новый тариф", "primary", () => tariffDialog())), table(["Название", "Зона", "Время", "Цена", "Когда", "Статус", ""], rows)]);
}

function tariffDialog(t?: TariffRow): void {
  const id = textInput(t?.id ?? "", "text", "h2");
  if (t) id.disabled = true;
  const name = textInput(t?.name ?? "", "text", "2 часа");
  const minutes = textInput(String(t?.minutes ?? 60), "number");
  const price = textInput(String(t?.price ?? 100), "number");
  const zone = textInput(t?.zone ?? "ARENA");
  const sort = textInput(String(t?.sort ?? 0), "number");
  const night = el("input", { attrs: { type: "checkbox" } });
  night.checked = t?.nightOnly ?? false;
  const active = el("input", { attrs: { type: "checkbox" } });
  active.checked = t?.active ?? true;
  const close = modal(t ? `Тариф «${t.name}»` : "Новый тариф", [
    field("Код (латиницей, не меняется)", id), field("Название", name), field("Минут", minutes), field("Цена, ₽", price), field("Зона", zone), field("Порядок в списке", sort),
    el("label", { class: "ad-check" }, [night, el("span", { text: "Только ночью (22:00–08:00)" })]),
    el("label", { class: "ad-check" }, [active, el("span", { text: "Включён" })]),
  ], [
    button("Сохранить", "primary", async () => {
      const m = intOf(minutes), pr = intOf(price);
      if (!m) return toast("Минуты — целое число больше нуля", "error");
      if (pr === null) return toast("Цена — целое число рублей (0 — бесплатно)", "error");
      const ok = await run(api("PUT", "/api/admin/tariffs", {
        id: id.value.trim(), name: name.value.trim(), minutes: m, price: pr,
        nightOnly: night.checked, zone: zone.value.trim() || "ARENA", active: active.checked, sort: Number(sort.value) || 0,
      }), "Тариф сохранён", () => void render());
      if (ok) close();
    }),
  ]);
  (t ? name : id).focus();
}

// ---------- сеансы ----------
let onlyActive = true;

async function viewSessions(): Promise<HTMLElement | null> {
  const r = await api<SessionRow[]>("GET", `/api/admin/sessions?active=${onlyActive}`);
  if (r.status !== 200) return connectionProblem(r.status);
  const now = Math.floor(Date.now() / 1000);
  const rows = r.data.map((s) => [
    `#${s.id}`,
    el("b", { text: s.guest }),
    s.pc,
    s.tariffId,
    when(s.started),
    s.ended ? when(s.ended) : badge(formatTime(Math.max(0, s.ends - now)), "ok"),
    rub(s.price),
    s.ended ? (s.reason ?? "") : el("div", { class: "ad-btnrow" }, [
      button("+15", "", () => void run(api("POST", `/api/admin/sessions/${s.id}/add-time`, { minutes: 15 }), "Добавлено 15 минут", () => void render())),
      button("Завершить", "danger", () => confirmBox("Завершить сеанс?", `${s.guest} на ${s.pc} будет отключён.`, "Завершить", () => void run(api("POST", `/api/admin/sessions/${s.id}/stop`), "Сеанс завершён", () => void render()))),
    ]),
  ]);
  const toggle = el("div", { class: "ad-seg" }, [
    button("Активные", onlyActive ? "on" : "", () => { onlyActive = true; void render(); }),
    button("Все", onlyActive ? "" : "on", () => { onlyActive = false; void render(); }),
  ]);
  return el("div", {}, [header("Сеансы", toggle), table(["№", "Гость", "ПК", "Тариф", "Начало", "Конец / осталось", "Цена", ""], rows, onlyActive ? "Сейчас никто не играет" : "Сеансов ещё не было")]);
}

// ---------- игры и приложения ----------
interface AppRow { id: string; title: string; kind: "game" | "app"; group?: string | null; cover?: string | null; paths: string[]; args?: string | null; workDir?: string | null; clubAccount: boolean; minAge?: number | null; enabled: boolean; pinned: boolean; sort: number }

async function viewApps(): Promise<HTMLElement | null> {
  const r = await api<AppRow[]>("GET", "/api/admin/apps");
  if (r.status !== 200) return connectionProblem(r.status);
  const rows = r.data.map((a) => [
    a.cover
      ? el("img", { class: "ad-thumb", attrs: { src: serverUrl + a.cover, alt: "", draggable: "false" } })
      : el("div", { class: "ad-thumb ph", text: a.title.slice(0, 2).toUpperCase() }),
    el("div", {}, [el("b", { text: a.title }), el("div", { class: "ad-muted ad-small", text: [a.kind === "game" ? "Игра" : "Приложение", a.group].filter(Boolean).join(" · ") })]),
    el("div", { class: "ad-path", text: a.paths[0] + (a.paths.length > 1 ? `  (+${a.paths.length - 1})` : ""), attrs: { title: a.paths.join("\n") } }),
    el("div", { class: "ad-btnrow" }, [
      ...(a.minAge ? [badge(`${a.minAge}+`, "warn")] : []),
      ...(a.clubAccount ? [badge("клубный аккаунт", "ok")] : []),
      ...(a.pinned ? [badge("на панели")] : []),
    ]),
    a.enabled ? badge("включено", "ok") : badge("выключено"),
    el("div", { class: "ad-btnrow" }, [
      button("Изменить", "", () => appDialog(a)),
      button(a.enabled ? "Выключить" : "Включить", "", () => void run(api("PUT", "/api/admin/apps", { ...a, enabled: !a.enabled }), a.enabled ? "Выключено" : "Включено", () => void render())),
      button("Удалить", "danger", () => confirmBox("Удалить?", `«${a.title}» исчезнет из оболочек всех ПК.`, "Удалить", () => void run(api("DELETE", `/api/admin/apps/${encodeURIComponent(a.id)}`), "Удалено", () => void render()))),
    ]),
  ]);
  return el("div", {}, [
    header("Игры и приложения", button("Добавить", "primary", () => appDialog())),
    el("p", { class: "ad-muted ad-note", text: "Список видят гости на всех ПК. Игра показывается, только если на ПК найден хотя бы один из путей запуска. Изменения доходят до оболочек в течение минуты." }),
    table(["", "Название", "Путь запуска", "Метки", "Статус", ""], rows, "Пока пусто. Нажмите «Добавить»."),
  ]);
}

const slug = (t: string) => t.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 32);

function appDialog(a?: AppRow): void {
  const editing = !!a;
  const title = textInput(a?.title ?? "", "text", "Counter-Strike 2");
  const id = textInput(a?.id ?? "", "text", "cs2 (латиницей, не меняется)");
  if (editing) id.disabled = true;
  title.addEventListener("input", () => { if (!editing && !id.dataset.touched) id.value = slug(title.value); });
  id.addEventListener("input", () => (id.dataset.touched = "1"));
  const kind = el("select", { class: "ad-input" }, [el("option", { text: "Игра", attrs: { value: "game" } }), el("option", { text: "Приложение", attrs: { value: "app" } })]);
  kind.value = a?.kind ?? "game";
  const group = textInput(a?.group ?? "", "text", "Шутеры, MOBA, Общение…");
  const paths = [0, 1, 2].map((i) => textInput(a?.paths[i] ?? "", "text", i === 0 ? "C:\\Games\\game.exe, steam://rungameid/730 или find:Program.exe" : "Запасной путь (необязательно)"));
  const args = textInput(a?.args ?? "", "text", "Аргументы запуска (необязательно). Можно {user} и {pc}");
  const age = el("select", { class: "ad-input" }, ["", "6", "12", "16", "18"].map((v) => el("option", { text: v ? `${v}+` : "Без ограничения", attrs: { value: v } })));
  age.value = a?.minAge ? String(a.minAge) : "";
  const sort = textInput(String(a?.sort ?? 0), "number");
  const chk = (label: string, on: boolean) => {
    const i = el("input", { attrs: { type: "checkbox" } });
    i.checked = on;
    return [i, el("label", { class: "ad-check" }, [i, el("span", { text: label })])] as const;
  };
  const [club, clubRow] = chk("Клубный аккаунт (выдаёт RentalGames)", a?.clubAccount ?? false);
  const [pinned, pinnedRow] = chk("Закрепить на панели задач", a?.pinned ?? false);
  const [enabled, enabledRow] = chk("Включено", a?.enabled ?? true);

  // обложка: загружается после того, как игра сохранена (нужен её код)
  const cover = el("img", { class: "ad-cover", attrs: { alt: "", draggable: "false" } });
  const coverBox = el("div", { class: "ad-cover-box" });
  function paintCover(url?: string | null): void {
    coverBox.replaceChildren(url ? Object.assign(cover, { src: serverUrl + url }) : el("div", { class: "ad-cover ph", text: "нет обложки" }));
  }
  paintCover(a?.cover);
  const pick = button("Выбрать картинку…", "", async () => {
    const r = await api<{ ok?: boolean; error?: string; cover?: string }>("UPLOAD", `/api/admin/apps/${encodeURIComponent(id.value)}/cover`);
    if (r.status === 0 && r.data.error === "cancelled") return;
    if (r.status === 200 && r.data.ok) { toast("Обложка загружена"); paintCover(r.data.cover); void render(true); }
    else toast(r.data.error ?? "Не удалось загрузить", "error");
  });
  const unpick = button("Убрать", "", async () => {
    if (await run(api("DELETE", `/api/admin/apps/${encodeURIComponent(id.value)}/cover`), "Обложка убрана")) { paintCover(null); void render(true); }
  });
  pick.disabled = unpick.disabled = !editing;
  const coverHint = el("div", { class: "ad-muted ad-small", text: editing ? "JPG, PNG или WEBP до 2 МБ, лучше вертикальная 2:3 (600×900)." : "Обложку можно добавить после первого сохранения." });

  const close = modal(editing ? `«${a!.title}»` : "Новая игра или приложение", [
    el("div", { class: "ad-appform" }, [
      el("div", {}, [field("Название", title), field("Код", id), el("div", { class: "ad-two" }, [field("Тип", kind), field("Группа", group)]),
        field("Путь запуска (до 3, берётся первый найденный на ПК)", paths[0]!), paths[1]!, paths[2]!, field("Аргументы", args),
        el("div", { class: "ad-two" }, [field("Возраст", age), field("Порядок в списке", sort)]), clubRow, pinnedRow, enabledRow]),
      el("div", { class: "ad-coverside" }, [el("div", { class: "lbl", text: "Обложка" }), coverBox, el("div", { class: "ad-btnrow" }, [pick, unpick]), coverHint]),
    ]),
  ], [
    button("Сохранить", "primary", async () => {
      const list = paths.map((p) => p.value.trim()).filter(Boolean);
      const body = {
        id: id.value.trim(), title: title.value.trim(), kind: kind.value, group: group.value.trim() || null, paths: list, args: args.value.trim() || null,
        clubAccount: club.checked, minAge: age.value ? Number(age.value) : null, pinned: pinned.checked, enabled: enabled.checked, sort: Number(sort.value) || 0,
      };
      const ok = await run(api("PUT", "/api/admin/apps", body), "Сохранено", () => void render());
      if (!ok) return;
      close();
      if (!editing) {
        const fresh = (await api<AppRow[]>("GET", "/api/admin/apps")).data.find((x) => x.id === body.id);
        if (fresh) appDialog(fresh); // сразу открываем снова, чтобы можно было добавить обложку
      }
    }),
  ]);
  (editing ? paths[0]! : title).focus();
}

// ---------- запрещённые программы ----------
interface BlockedRow { id: number; process: string; note?: string | null }
const BLOCK_SUGGESTIONS = ["cheatengine-x86_64.exe", "processhacker.exe", "procexp.exe", "procexp64.exe", "autohotkey.exe", "wireshark.exe", "regedit.exe"];

async function viewBlocked(): Promise<HTMLElement | null> {
  const r = await api<BlockedRow[]>("GET", "/api/admin/blocked");
  if (r.status !== 200) return connectionProblem(r.status);
  const add = async (process: string, note: string) => run(api("POST", "/api/admin/blocked", { process, note }), "Добавлено", () => void render());
  const proc = textInput("", "text", "program.exe");
  const note = textInput("", "text", "Заметка (необязательно)");
  const addBtn = button("Запретить", "primary", () => void add(proc.value.trim(), note.value.trim()));
  proc.addEventListener("keydown", (e) => (e as KeyboardEvent).key === "Enter" && addBtn.click());
  const have = new Set(r.data.map((b) => b.process.toLowerCase()));
  const rows = r.data.map((b) => [el("b", { text: b.process }), b.note ?? "", button("Убрать", "", () => void run(api("DELETE", `/api/admin/blocked/${b.id}`), "Убрано", () => void render()))]);
  return el("div", {}, [
    header("Запрещённые программы"),
    el("p", { class: "ad-muted ad-note", text: "Эти программы закрываются сразу после запуска на ПК гостей (в сеансе), гость видит сообщение. Укажите имя процесса, например cheatengine-x86_64.exe. Изменения доходят до ПК в течение минуты." }),
    el("div", { class: "ad-addrow" }, [proc, note, addBtn]),
    el("div", { class: "ad-chips" }, [el("span", { class: "ad-muted ad-small", text: "Популярные:" }), ...BLOCK_SUGGESTIONS.filter((s) => !have.has(s)).map((s) => button(s, "ghost", () => void add(s, "")))]),
    table(["Процесс", "Заметка", ""], rows, "Список пуст"),
  ]);
}

// ---------- журнал действий администратора ----------
interface AuditRow { id: number; ts: number; ip: string | null; action: string; details: string | null }

async function viewAudit(): Promise<HTMLElement | null> {
  const r = await api<AuditRow[]>("GET", "/api/admin/audit");
  if (r.status !== 200) return connectionProblem(r.status);
  const rows = (Array.isArray(r.data) ? r.data : []).map((a) => [when(a.ts), el("b", { text: a.action }), a.details ?? "", el("span", { class: "ad-muted", text: a.ip ?? "" })]);
  return el("div", {}, [
    header("Журнал действий"),
    el("p", { class: "ad-muted ad-note", text: "Последние 300 изменений из админки: пополнения и списания, команды ПК, правки гостей, тарифов и каталога." }),
    table(["Когда", "Действие", "Подробности", "IP"], rows, "Записей пока нет"),
  ]);
}

// ---------- смена и касса ----------
let shiftOpen = false;

interface ShiftInfo { id: number; opened: number; closed: number | null; cashier: string; cashOpen: number; cashClose: number | null; note: string | null }
interface ShiftReport {
  shift: ShiftInfo; topupCash: number; topupCard: number; topupSbp: number; topupOther: number; charges: number; chargesCash: number;
  sessionRevenue: number; sessions: number; bonusSpent: number; newGuests: number; cashExpected: number; cashDiff: number | null;
}

function reportBlock(r: ShiftReport): HTMLElement {
  const row = (k: string, v: string, cls = "") => el("div", { class: "ad-rep-row " + cls }, [el("span", { text: k }), el("b", { text: v })]);
  return el("div", { class: "ad-rep" }, [
    row("Наличными", rub(r.topupCash)), row("Картой", rub(r.topupCard)), row("СБП", rub(r.topupSbp)),
    ...(r.topupOther ? [row("Без оплаты", rub(r.topupOther))] : []),
    row("Списания и возвраты", rub(r.charges)),
    row("Выручка за сеансы", `${rub(r.sessionRevenue)} · ${r.sessions} сеансов`),
    row("Оплачено бонусами", rub(r.bonusSpent)),
    row("Новых гостей", String(r.newGuests)),
    row("Наличных должно быть в кассе", rub(r.cashExpected), "strong"),
    ...(r.cashDiff !== null ? [row("Расхождение", r.cashDiff === 0 ? "нет" : `${r.cashDiff > 0 ? "+" : ""}${rub(r.cashDiff)}`, r.cashDiff === 0 ? "ok" : "bad")] : []),
  ]);
}

async function viewShift(): Promise<HTMLElement | null> {
  const [cur, list] = await Promise.all([api<{ shift: ShiftInfo | null; report: ShiftReport | null }>("GET", "/api/admin/shift"), api<ShiftInfo[]>("GET", "/api/admin/shifts")]);
  if (cur.status !== 200) return connectionProblem(cur.status);
  shiftOpen = !!cur.data.shift;
  const history = (Array.isArray(list.data) ? list.data : []).filter((x) => x.closed).map((x) => [
    `#${x.id}`, x.cashier, when(x.opened), x.closed ? when(x.closed) : "", rub(x.cashClose ?? 0),
    button("Отчёт", "", async () => {
      const r = await api<ShiftReport>("GET", `/api/admin/shifts/${x.id}`);
      if (r.status === 200) modal(`Смена #${x.id} · ${x.cashier}`, [reportBlock(r.data)]);
    }),
  ]);
  let top: HTMLElement;
  if (!cur.data.shift) {
    const cashier = textInput("", "text", "Имя кассира");
    const cash = textInput("0", "number");
    top = el("section", { class: "ad-card" }, [
      el("h2", { text: "Смена не открыта" }),
      el("p", { class: "ad-muted", text: "Откройте смену в начале работы: все пополнения и списания попадут в её отчёт, при закрытии касса сверится с наличными." }),
      el("div", { class: "ad-two" }, [field("Кассир", cashier), field("Наличных в кассе на начало, ₽", cash)]),
      button("Открыть смену", "primary", async () => {
        const n = intOf(cash);
        if (n === null) return toast("Сумма в кассе — целое число", "error");
        await run(api("POST", "/api/admin/shift/open", { cashier: cashier.value.trim(), cash: n }), "Смена открыта", () => void render());
      }),
    ]);
  } else {
    const sh = cur.data.shift;
    top = el("section", { class: "ad-card" }, [
      el("h2", { text: `Смена #${sh.id} · ${sh.cashier}` }),
      el("p", { class: "ad-muted", text: `Открыта ${when(sh.opened)}, в кассе на начало ${rub(sh.cashOpen)}` }),
      cur.data.report ? reportBlock(cur.data.report) : el("span"),
      button("Закрыть смену", "danger", () => closeShiftDialog(cur.data.report)),
    ]);
  }
  return el("div", {}, [header("Смена и касса"), top, el("h2", { class: "ad-sub", text: "Прошлые смены" }), table(["№", "Кассир", "Открыта", "Закрыта", "В кассе", ""], history, "Закрытых смен ещё нет")]);
}

function closeShiftDialog(r: ShiftReport | null): void {
  const cash = textInput("", "number", r ? String(r.cashExpected) : "0");
  const note = textInput("", "text", "Комментарий (необязательно)");
  const close = modal("Закрыть смену", [
    el("p", { class: "ad-muted", text: r ? `По операциям наличных должно быть ${rub(r.cashExpected)}. Пересчитайте кассу и введите сумму.` : "" }),
    field("Посчитано наличных, ₽", cash), field("Комментарий", note),
  ], [button("Закрыть смену", "danger", async () => {
    const n = intOf(cash);
    if (n === null) return toast("Введите посчитанную сумму", "error");
    const res = await api<{ ok?: boolean; error?: string; report?: ShiftReport }>("POST", "/api/admin/shift/close", { cash: n, note: note.value.trim() || null });
    if (res.status !== 200 || !res.data.report) return toast(res.data.error ?? "Не удалось закрыть смену", "error");
    close();
    modal("Смена закрыта", [reportBlock(res.data.report), el("p", { class: "ad-muted", text: "Отчёт отправлен в Telegram, если он подключён в «Настройках»." })]);
    void render();
  })]);
  cash.focus();
}

// ---------- бонусы гостя, телефон ----------
function bonusDialog(g: GuestRow): void {
  const amount = textInput("100", "number");
  const note = textInput("", "text", "За что (необязательно)");
  const info = el("p", { class: "ad-muted", text: "Загрузка уровня…" });
  void api<{ bonus: number; spent: number; level: string | null; cashbackPct: number; nextLevel: string | null; toNext: number }>("GET", `/api/admin/guests/${g.id}/loyalty`).then((r) => {
    if (r.status === 200) info.textContent = `Бонусов ${r.data.bonus} · уровень ${r.data.level ?? "—"} (кешбэк ${r.data.cashbackPct}%) · потрачено ${rub(r.data.spent)}${r.data.nextLevel ? ` · до «${r.data.nextLevel}» ${rub(r.data.toNext)}` : ""}`;
  });
  const act = (sign: 1 | -1) => async () => {
    const n = intOf(amount);
    if (!n) return toast("Введите целое число бонусов", "error");
    if (await run(api("POST", `/api/admin/guests/${g.id}/bonus`, { amount: n * sign, note: note.value.trim() || null }), "Готово", () => void render())) close();
  };
  const close = modal(`Бонусы: ${g.login}`, [info, field("Количество (1 бонус = 1 ₽ при оплате времени)", amount), field("Комментарий", note)],
    [button("Списать", "", act(-1)), button("Начислить", "primary", act(1))]);
}

function phoneDialog(g: GuestRow): void {
  const phone = textInput(g.phone ?? "", "tel", "+7 900 000-00-00");
  const close = modal(`Телефон: ${g.login}`, [field("Телефон (пусто — удалить)", phone)], [
    button("Сохранить", "primary", async () => { if (await run(api("PUT", `/api/admin/guests/${g.id}`, { phone: phone.value.trim() }), "Сохранено", () => void render())) close(); }),
  ]);
  phone.focus();
}

// ---------- брони ----------
interface BookingRow { id: number; pcId: number; pc: string; guestId: number; guest: string; starts: number; ends: number; status: string; note: string | null }

async function viewBookings(): Promise<HTMLElement | null> {
  const r = await api<BookingRow[]>("GET", "/api/admin/bookings");
  if (r.status !== 200) return connectionProblem(r.status);
  const rows = (Array.isArray(r.data) ? r.data : []).map((b) => [
    el("b", { text: b.pc }), b.guest, when(b.starts), `${Math.round((b.ends - b.starts) / 60)} мин`, b.note ?? "",
    button("Отменить", "", () => confirmBox("Отменить бронь?", `${b.pc}, ${when(b.starts)} для ${b.guest}`, "Отменить бронь", () => void run(api("DELETE", `/api/admin/bookings/${b.id}`), "Бронь отменена", () => void render()))),
  ]);
  return el("div", {}, [
    header("Брони", button("Новая бронь", "primary", () => void bookingDialog())),
    el("p", { class: "ad-muted ad-note", text: "За 15 минут до начала ПК держится за гостем: другие войти не смогут, а гость с броней сможет. Если гость не пришёл за 30 минут после начала, бронь снимается сама. Сеанс, который заходит на чужую бронь, купить нельзя." }),
    table(["ПК", "Гость", "Начало", "Длительность", "Заметка", ""], rows, "Предстоящих броней нет"),
  ]);
}

async function bookingDialog(): Promise<void> {
  const pcs = await api<PcRow[]>("GET", "/api/admin/pcs");
  if (pcs.status !== 200 || !Array.isArray(pcs.data) || pcs.data.length === 0) return void toast("Сначала добавьте ПК", "error");
  const pc = el("select", { class: "ad-input" }, pcs.data.map((p) => el("option", { attrs: { value: String(p.id) }, text: `${p.name} · ${p.zone}` })));
  const login = textInput("", "text", "Логин гостя");
  const d = new Date(Date.now() + 60 * 60 * 1000);
  d.setMinutes(0, 0, 0);
  const pad = (n: number) => String(n).padStart(2, "0");
  const start = textInput(`${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:00`, "datetime-local");
  const minutes = el("select", { class: "ad-input" }, [60, 120, 180, 300, 600].map((m) => el("option", { attrs: { value: String(m) }, text: m < 600 ? `${m / 60} ч` : "ночь (10 ч)" })));
  const note = textInput("", "text", "Заметка (необязательно)");
  const close = modal("Новая бронь", [field("Компьютер", pc), field("Гость", login), el("div", { class: "ad-two" }, [field("Начало", start), field("Длительность", minutes)]), field("Заметка", note)], [
    button("Забронировать", "primary", async () => {
      const ts = Math.floor(new Date((start as HTMLInputElement).value).getTime() / 1000);
      if (!ts) return toast("Укажите время начала", "error");
      const ok = await run(api("POST", "/api/admin/bookings", { pcId: Number((pc as HTMLSelectElement).value), login: login.value.trim(), starts: ts, minutes: Number((minutes as HTMLSelectElement).value), note: note.value.trim() || null }), "Бронь создана", () => void render());
      if (ok) close();
    }),
  ]);
  login.focus();
}

// ---------- бонусы и промокоды ----------
interface LevelRow { id: number; name: string; minSpent: number; cashbackPct: number }
interface PromoRow { code: string; bonus: number; maxUses: number; used: number; expires: number | null; active: boolean; created: number }

async function viewLoyalty(): Promise<HTMLElement | null> {
  const [lv, pr] = await Promise.all([api<LevelRow[]>("GET", "/api/admin/levels"), api<PromoRow[]>("GET", "/api/admin/promos")]);
  if (lv.status !== 200) return connectionProblem(lv.status);
  const levels = Array.isArray(lv.data) ? lv.data : [];
  const inputs = levels.map((l) => ({ name: textInput(l.name), min: textInput(String(l.minSpent), "number"), pct: textInput(String(l.cashbackPct), "number") }));
  const levelRows = el("div", { class: "ad-levels" });
  const paint = () => levelRows.replaceChildren(...inputs.map((i, n) => el("div", { class: "ad-level" }, [i.name, i.min, i.pct, button("×", "icon", () => { inputs.splice(n, 1); paint(); })])));
  paint();
  const saveLevels = button("Сохранить уровни", "primary", async () => {
    const body = inputs.map((i) => ({ name: i.name.value.trim(), minSpent: intOf(i.min) ?? -1, cashbackPct: intOf(i.pct) ?? -1 }));
    await run(api("PUT", "/api/admin/levels", body), "Уровни сохранены", () => void render());
  });
  const promos = (Array.isArray(pr.data) ? pr.data : []).map((p) => [
    el("b", { text: p.code }), `${p.bonus} б.`, p.maxUses ? `${p.used} / ${p.maxUses}` : `${p.used} / ∞`, p.expires ? when(p.expires) : "бессрочно",
    p.active ? badge("действует", "ok") : badge("выключен"),
    button(p.active ? "Выключить" : "Включить", "", () => void run(api("PUT", "/api/admin/promos", { ...p, active: !p.active }), "Готово", () => void render())),
  ]);
  return el("div", {}, [
    header("Бонусы и промокоды"),
    el("section", { class: "ad-card" }, [
      el("h2", { text: "Уровни гостей и кешбэк" }),
      el("p", { class: "ad-muted", text: "Уровень зависит от того, сколько гость потратил на время за всё время. С каждой покупки деньгами начисляется кешбэк бонусами. При покупке времени сначала тратятся бонусы (1 бонус = 1 ₽), потом баланс." }),
      el("div", { class: "ad-level ad-level-head" }, [el("span", { text: "Название" }), el("span", { text: "Потрачено от, ₽" }), el("span", { text: "Кешбэк, %" }), el("span")]),
      levelRows,
      el("div", { class: "ad-btnrow" }, [button("Добавить уровень", "", () => { inputs.push({ name: textInput("Новый"), min: textInput("0", "number"), pct: textInput("0", "number") }); paint(); }), saveLevels]),
    ]),
    el("h2", { class: "ad-sub", text: "Промокоды" }),
    el("div", { class: "ad-btnrow" }, [button("Новый промокод", "primary", promoDialog)]),
    table(["Код", "Бонус", "Использован", "До", "Статус", ""], promos, "Промокодов ещё нет"),
  ]);
}

function promoDialog(): void {
  const code = textInput("", "text", "WELCOME");
  const bonus = textInput("100", "number");
  const uses = textInput("0", "number");
  const until = textInput("", "date");
  const close = modal("Новый промокод", [
    field("Код (гость вводит его на ПК)", code), el("div", { class: "ad-two" }, [field("Бонусов", bonus), field("Сколько раз всего (0 — без ограничений)", uses)]),
    field("Действует до (пусто — бессрочно)", until),
  ], [button("Создать", "primary", async () => {
    const b = intOf(bonus), u = intOf(uses);
    if (!b || u === null) return toast("Бонус и лимит — целые числа", "error");
    const exp = (until as HTMLInputElement).value ? Math.floor(new Date((until as HTMLInputElement).value + "T23:59:59").getTime() / 1000) : null;
    if (await run(api("PUT", "/api/admin/promos", { code: code.value.trim(), bonus: b, maxUses: u, expires: exp, active: true }), "Промокод создан", () => void render())) close();
  })]);
  code.focus();
}

// ---------- аналитика ----------
interface Analytics {
  days: number; revenue: number; sessions: number; avgCheck: number;
  byDay: { day: string; revenue: number; topups: number }[]; byHour: { hour: number; sessions: number }[];
  tariffs: { id: string; name: string; sessions: number; sum: number }[]; pcs: { pc: string; sessions: number; hours: number }[]; top: { login: string; spent: number }[];
}
let analyticsDays = 7;

function bars(items: { label: string; value: number; hint?: string }[]): HTMLElement {
  const max = Math.max(1, ...items.map((i) => i.value));
  return el("div", { class: "ad-bars" }, items.map((i) => el("div", { class: "ad-bar-row" }, [
    el("span", { class: "ad-bar-label", text: i.label }),
    el("div", { class: "ad-bar-track" }, [Object.assign(el("div", { class: "ad-bar" }), { style: `width:${(i.value / max) * 100}%` })]),
    el("span", { class: "ad-bar-val", text: i.hint ?? String(i.value) }),
  ])));
}

async function viewAnalytics(): Promise<HTMLElement | null> {
  const r = await api<Analytics>("GET", `/api/admin/analytics?days=${analyticsDays}`);
  if (r.status !== 200) return connectionProblem(r.status);
  const a = r.data;
  const period = el("div", { class: "ad-btnrow" }, [1, 7, 30, 90].map((d) => button(d === 1 ? "Сегодня" : `${d} дней`, d === analyticsDays ? "primary" : "", () => { analyticsDays = d; void render(); })));
  const stat = (label: string, value: string) => el("div", { class: "ad-stat" }, [el("div", { class: "ad-stat-label", text: label }), el("div", { class: "ad-stat-value", text: value })]);
  const hours = Array.from({ length: 24 }, (_, h) => ({ label: `${String(h).padStart(2, "0")}:00`, value: a.byHour.find((x) => x.hour === h)?.sessions ?? 0 }));
  return el("div", {}, [
    header("Аналитика", period),
    el("div", { class: "ad-stats" }, [stat("Выручка за сеансы", rub(a.revenue)), stat("Сеансов", String(a.sessions)), stat("Средний чек", rub(a.avgCheck))]),
    el("div", { class: "ad-cols" }, [
      el("section", { class: "ad-card" }, [el("h2", { text: "Выручка по дням" }), bars(a.byDay.map((d) => ({ label: d.day.slice(5).split("-").reverse().join("."), value: d.revenue, hint: rub(d.revenue) })))]),
      el("section", { class: "ad-card" }, [el("h2", { text: "Начало сеансов по часам" }), bars(hours)]),
    ]),
    el("div", { class: "ad-cols" }, [
      el("section", { class: "ad-card" }, [el("h2", { text: "Тарифы" }), bars(a.tariffs.map((t) => ({ label: t.name, value: t.sessions, hint: `${t.sessions} · ${rub(t.sum)}` })))]),
      el("section", { class: "ad-card" }, [el("h2", { text: "Загрузка ПК, часов" }), bars(a.pcs.map((p) => ({ label: p.pc, value: p.hours, hint: `${p.hours} ч` })))]),
    ]),
    el("section", { class: "ad-card" }, [el("h2", { text: "Лучшие гости" }), table(["Гость", "Потратил"], a.top.map((t) => [t.login, rub(t.spent)]), "Пока нет данных")]),
  ]);
}

// ---------- настройки ----------
async function viewSettings(): Promise<HTMLElement | null> {
  const r = await api<{ configured: boolean; chat: string | null }>("GET", "/api/admin/settings/telegram");
  if (r.status !== 200) return connectionProblem(r.status);
  const token = textInput("", "password", r.data.configured ? "•••••• (сохранён, введите новый, чтобы заменить)" : "123456:ABC… от @BotFather");
  const chat = textInput(r.data.chat ?? "", "text", "ID чата, например -1001234567890");
  return el("div", {}, [
    header("Настройки"),
    el("section", { class: "ad-card" }, [
      el("h2", { text: "Уведомления в Telegram" }),
      el("p", { class: "ad-muted", text: "Сюда придут вызовы администратора с ПК и отчёты при закрытии смены. Создайте бота у @BotFather, добавьте его в чат администраторов и укажите ID чата (его покажет, например, @getmyid_bot)." }),
      field("Токен бота", token), field("Чат", chat),
      el("div", { class: "ad-btnrow" }, [
        button("Сохранить", "primary", async () => {
          await run(api("PUT", "/api/admin/settings/telegram", { token: token.value.trim() || null, chat: chat.value.trim() }), "Сохранено", () => void render());
        }),
        button("Отправить проверку", "", async () => {
          const t = await api<{ ok: boolean; error?: string }>("POST", "/api/admin/settings/telegram/test");
          toast(t.data.ok ? "Сообщение отправлено" : t.data.error ?? "Не отправилось", t.data.ok ? "ok" : "error");
        }),
      ]),
      el("p", { class: "ad-muted ad-small", text: r.data.configured ? "Статус: подключено" : "Статус: не настроено" }),
    ]),
  ]);
}

// ---------- вызовы администратора: видно на любой вкладке ----------
interface CallRow { pcId: number; pc: string; guest: string | null; ts: number }
const callBar = el("div", { class: "ad-callbar", attrs: { hidden: "" } });
let knownCalls = new Set<number>();

function beep(): void {
  try {
    const ctx = new AudioContext();
    [0, 0.25].forEach((t) => {
      const o = ctx.createOscillator(), g = ctx.createGain();
      o.frequency.value = 880; o.connect(g); g.connect(ctx.destination);
      g.gain.setValueAtTime(0.25, ctx.currentTime + t); g.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + t + 0.2);
      o.start(ctx.currentTime + t); o.stop(ctx.currentTime + t + 0.22);
    });
  } catch { /* без звука */ }
}

async function refreshCalls(): Promise<void> {
  const r = await api<CallRow[]>("GET", "/api/admin/calls");
  if (r.status !== 200 || !Array.isArray(r.data)) return;
  const fresh = r.data.filter((c) => !knownCalls.has(c.pcId));
  if (fresh.length) beep();
  knownCalls = new Set(r.data.map((c) => c.pcId));
  callBar.hidden = r.data.length === 0;
  callBar.replaceChildren(...r.data.map((c) => el("div", { class: "ad-callitem" }, [
    el("b", { text: `🔔 ${c.pc}` }), el("span", { text: `${c.guest ?? "гость"} зовёт администратора · ${new Date(c.ts * 1000).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })}` }),
    button("Иду", "primary", async () => { await api("POST", `/api/admin/calls/${c.pcId}/ack`); void refreshCalls(); if (current === "pcs") void render(true); }),
  ])));
}

// живые таймеры на карточках зала: между обновлениями с сервера (раз в 3 с) секунды идут сами
window.setInterval(() => {
  document.querySelectorAll<HTMLElement>(".ad-pc-time[data-left]").forEach((t) => {
    const left = Math.max(0, Number(t.dataset.left) - 1);
    t.dataset.left = String(left);
    t.textContent = formatTime(left);
  });
}, 1000);

// ---------- подключение ----------
function showConnect(message = ""): void {
  window.clearInterval(timer);
  const url = textInput(serverUrl || "https://", "text", "https://club.example.ru");
  const key = textInput("", "password", "Ключ из admin-key.txt на сервере");
  const err = el("div", { class: "ad-connect-err", text: message });
  const go1 = button("Подключиться", "primary", () => {
    if (!url.value.trim() || !key.value.trim()) { err.textContent = "Заполните адрес и ключ"; return; }
    go1.disabled = true;
    err.textContent = "";
    waitingConnect = true;
    send({ type: "saveSettings", serverUrl: url.value.trim(), key: key.value.trim() });
  });
  app.replaceChildren(
    el("div", { class: "ad-connect" }, [
      el("div", { class: "ad-brand big" }, [el("span", { class: "brand-gradient", text: "МИРАЖ" }), el("small", { text: "PRO · админ" })]),
      el("div", { class: "ad-connect-box" }, [
        el("h1", { text: "Подключение к серверу" }),
        ...(isLiveAdmin ? [] : [field("Адрес сервера", url)]),
        field("Ключ администратора", key),
        err,
        go1,
        el("p", { class: "ad-muted", text: isLiveAdmin
          ? "Ключ запоминается в этом браузере. На чужом компьютере после работы нажмите «Выйти»."
          : "Ключ хранится на этом компьютере в зашифрованном виде и не передаётся странице." }),
      ]),
    ]),
  );
  key.addEventListener("keydown", (e) => (e as KeyboardEvent).key === "Enter" && go1.click());
  (serverUrl || isLiveAdmin ? key : url).focus();
}

let waitingConnect = false;
let callTimer: number | undefined;

async function enter(): Promise<void> {
  const r = await api<Stats>("GET", "/api/admin/stats");
  if (r.status !== 200) {
    showConnect(r.status === 401 ? "Неверный ключ администратора" : "Сервер недоступен, проверьте адрес");
    return;
  }
  serverLabel.textContent = serverUrl;
  app.replaceChildren(shell);
  document.body.append(callBar);
  go(current);
  void refreshCalls();
  window.clearInterval(callTimer);
  callTimer = window.setInterval(() => void refreshCalls(), 4000);
  void api<{ shift: unknown }>("GET", "/api/admin/shift").then((r) => (shiftOpen = r.status === 200 && !!r.data.shift));
}

onMessage((msg: FromHost) => {
  if (msg.type === "apiResult") {
    pending.get(msg.id)?.({ status: msg.status, body: msg.body });
    pending.delete(msg.id);
  } else if (msg.type === "settings") {
    serverUrl = msg.serverUrl;
    if (!msg.hasKey || !msg.serverUrl) showConnect();
    else {
      waitingConnect = false;
      void enter();
    }
  }
});

document.body.append(toastEl);
window.addEventListener("contextmenu", (e) => e.preventDefault());
send({ type: "ready" });
void waitingConnect;
