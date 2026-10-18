import "@fontsource/inter/400.css";
import "@fontsource/inter/700.css";
import "@fontsource/russo-one/400.css";
import "./styles.css";
import "./admin.css";

import { onMessage, send } from "./bridge";
import { el, formatTime } from "./dom";
import type { FromHost } from "./protocol";

// Админ-панель клуба. Все запросы идут через хост (ClubShell.Admin): он добавляет ключ администратора.

// ---------- типы ответов сервера ----------
interface Stats { pcs: number; activeSessions: number; guests: number; revenueToday: number; topupsToday: number }
interface PcRow { id: number; name: string; zone: string; online: boolean; lastSeen: number; session: null | { id: number; guest: string; tariff: string; started: number; remainingSec: number } }
interface GuestRow { id: number; login: string; balance: number; birth: string | null; banned: boolean; created: number; phone?: string | null }
interface SmsRow { ts: number; phone: string; text: string }
interface TariffRow { id: string; name: string; minutes: number; price: number; nightOnly: boolean; zone: string; active: boolean; sort: number }
interface SessionRow { id: number; guestId: number; guest: string; pcId: number; pc: string; tariffId: string; started: number; ends: number; ended: number | null; price: number; reason: string | null }
interface TxRow { id: number; amount: number; kind: string; note: string | null; ts: number }
interface ApiReply<T> { status: number; data: T }

// ---------- вызовы API через хост ----------
let nextId = 1;
const pending = new Map<number, (r: { status: number; body: string }) => void>();

function api<T = { ok?: boolean; error?: string }>(method: "GET" | "POST" | "PUT" | "DELETE", path: string, body?: unknown): Promise<ApiReply<T>> {
  return new Promise((resolve) => {
    const id = nextId++;
    pending.set(id, (r) => {
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
const when = (ts: number) => new Date(ts * 1000).toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });

function button(text: string, cls = "", onClick?: () => void): HTMLButtonElement {
  const b = el("button", { class: "ad-btn " + cls, text });
  if (onClick) b.addEventListener("click", onClick);
  return b;
}

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
  const close = () => overlay.remove();
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
type ViewName = "dashboard" | "pcs" | "guests" | "tariffs" | "sessions";
const VIEWS: { id: ViewName; title: string }[] = [
  { id: "dashboard", title: "Дашборд" },
  { id: "pcs", title: "Компьютеры" },
  { id: "guests", title: "Гости" },
  { id: "tariffs", title: "Тарифы" },
  { id: "sessions", title: "Сеансы" },
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
    el("div", { class: "ad-side-foot" }, [serverLabel, button("Сменить сервер", "ghost", () => showConnect())]),
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
  if (view === "dashboard" || view === "pcs" || view === "sessions") timer = window.setInterval(() => void render(true), 3000);
}

async function render(silent = false): Promise<void> {
  const gen = ++renderGen;
  const views: Record<ViewName, () => Promise<HTMLElement | null>> = {
    dashboard: viewDashboard, pcs: viewPcs, guests: viewGuests, tariffs: viewTariffs, sessions: viewSessions,
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
  const cards = r.data.map((p) => {
    const state = p.online ? (p.session ? "busy" : "free") : "off";
    const card = el("button", { class: "ad-pc " + state }, [
      el("div", { class: "ad-pc-top" }, [el("b", { text: p.name }), el("span", { class: "ad-zone", text: p.zone })]),
      el("div", { class: "ad-pc-guest", text: p.session ? p.session.guest : p.online ? "Свободен" : "Не в сети" }),
      el("div", { class: "ad-pc-time", text: p.session ? formatTime(p.session.remainingSec) : "—" }),
    ]);
    card.addEventListener("click", () => openPc(p));
    return card;
  });
  return el("div", {}, [
    header("Компьютеры", button("Добавить ПК", "primary", addPc)),
    cards.length ? el("div", { class: "ad-pcs" }, cards) : el("div", { class: "ad-empty", text: "ПК ещё не добавлены. Нажмите «Добавить ПК», токен вписывается в config.json на компьютере." }),
  ]);
}

function openPc(p: PcRow): void {
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
  const snippet = `"ServerUrl": "${serverUrl}",\n"PcToken": "${token}"`;
  const pre = el("pre", { class: "ad-code", text: snippet });
  const copy = button("Копировать", "primary", async () => {
    try { await navigator.clipboard.writeText(snippet); toast("Скопировано"); } catch { toast("Не удалось скопировать: выделите текст вручную", "error"); }
  });
  modal(`Токен для ${name}`, [
    el("p", { class: "ad-text", text: "Токен показывается один раз: на сервере хранится только его хеш. Впишите эти две строки в C:\\ProgramData\\ClubShell\\config.json на этом компьютере и перезапустите службу ClubShell." }),
    pre,
  ], [copy]);
}

// ---------- гости ----------
let guestQuery = "";

async function viewGuests(): Promise<HTMLElement | null> {
  const r = await api<GuestRow[]>("GET", "/api/admin/guests" + (guestQuery ? `?q=${encodeURIComponent(guestQuery)}` : ""));
  if (r.status !== 200) return connectionProblem(r.status);
  const search = textInput(guestQuery, "text", "Логин или телефон");
  search.addEventListener("keydown", (e) => { if ((e as KeyboardEvent).key === "Enter") { guestQuery = search.value.trim(); void render(); } });
  const rows = r.data.map((g) => [
    el("b", { text: g.login }),
    g.phone ?? "—",
    rub(g.balance),
    g.banned ? badge("заблокирован", "bad") : badge("активен", "ok"),
    when(g.created),
    el("div", { class: "ad-btnrow" }, [
      button("Пополнить", "", () => moneyDialog(g, 1)),
      button("Списать", "", () => moneyDialog(g, -1)),
      button("История", "", () => void history(g)),
      button(g.banned ? "Разблокировать" : "Блок", "", () => void run(api("PUT", `/api/admin/guests/${g.id}`, { banned: !g.banned }), g.banned ? "Разблокирован" : "Заблокирован", () => void render())),
      button("Пароль", "", () => passwordDialog(g)),
    ]),
  ]);
  return el("div", {}, [
    header("Гости", search, button("Журнал SMS", "", () => void smsLog()), button("Новый гость", "primary", newGuest)),
    table(["Логин", "Телефон", "Баланс", "Статус", "Создан", ""], rows, "Гости не найдены"),
  ]);
}

async function smsLog(): Promise<void> {
  const r = await api<SmsRow[]>("GET", "/api/admin/sms-outbox");
  const rows = (Array.isArray(r.data) ? r.data : []).map((m) => [when(m.ts), m.phone, m.text]);
  modal("Журнал SMS (заглушка: сообщения не уходят оператору)", [table(["Когда", "Номер", "Текст"], rows, "SMS ещё не отправлялись")]);
}

function newGuest(): void {
  const login = textInput("", "text", "Логин");
  const pass = textInput("", "text", "Пароль (от 4 символов)");
  const bal = textInput("0", "number", "0");
  const close = modal("Новый гость", [field("Логин", login), field("Пароль", pass), field("Начальный баланс, ₽", bal)], [
    button("Создать", "primary", async () => {
      const ok = await run(api("POST", "/api/admin/guests", { login: login.value.trim(), password: pass.value, balance: Number(bal.value) || 0 }), "Гость создан", () => void render());
      if (ok) close();
    }),
  ]);
  login.focus();
}

function moneyDialog(g: GuestRow, sign: 1 | -1): void {
  const amount = textInput("100", "number");
  const note = textInput("", "text", "Комментарий (необязательно)");
  const close = modal(`${sign > 0 ? "Пополнить" : "Списать"}: ${g.login}`, [el("p", { class: "ad-muted", text: `Сейчас на балансе ${rub(g.balance)}` }), field("Сумма, ₽", amount), field("Комментарий", note)], [
    button(sign > 0 ? "Пополнить" : "Списать", "primary", async () => {
      const n = Math.abs(Math.round(Number(amount.value)));
      if (!n) return toast("Введите сумму", "error");
      const ok = await run(api("POST", `/api/admin/guests/${g.id}/topup`, { amount: n * sign, note: note.value.trim() || null }), "Готово", () => void render());
      if (ok) close();
    }),
  ]);
  amount.select();
}

function passwordDialog(g: GuestRow): void {
  const pass = textInput("", "text", "Новый пароль");
  const close = modal(`Пароль: ${g.login}`, [field("Новый пароль (от 4 символов)", pass)], [
    button("Сменить", "primary", async () => { if (await run(api("PUT", `/api/admin/guests/${g.id}`, { password: pass.value }), "Пароль изменён")) close(); }),
  ]);
  pass.focus();
}

async function history(g: GuestRow): Promise<void> {
  const r = await api<TxRow[]>("GET", `/api/admin/guests/${g.id}/transactions`);
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
      const ok = await run(api("PUT", "/api/admin/tariffs", {
        id: id.value.trim(), name: name.value.trim(), minutes: Number(minutes.value), price: Number(price.value),
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
        field("Адрес сервера", url),
        field("Ключ администратора", key),
        err,
        go1,
        el("p", { class: "ad-muted", text: "Ключ хранится на этом компьютере в зашифрованном виде и не передаётся странице." }),
      ]),
    ]),
  );
  key.addEventListener("keydown", (e) => (e as KeyboardEvent).key === "Enter" && go1.click());
  (serverUrl ? key : url).focus();
}

let waitingConnect = false;

async function enter(): Promise<void> {
  const r = await api<Stats>("GET", "/api/admin/stats");
  if (r.status !== 200) {
    showConnect(r.status === 401 ? "Неверный ключ администратора" : "Сервер недоступен, проверьте адрес");
    return;
  }
  serverLabel.textContent = serverUrl;
  app.replaceChildren(shell);
  go(current);
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
