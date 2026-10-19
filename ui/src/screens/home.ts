import { el, formatTime, isNight } from "../dom";
import { icons } from "../icons";
import type { CatalogItem, TariffPackage } from "../protocol";

// Главный экран гостя. Показывается сразу после входа: игры и приложения, профиль, баланс и время сеанса.
// Время покупается здесь же (окно «Тарифы»), а не отдельным экраном перед играми.

export interface HomeHandlers {
  onLaunch(id: string): void;
  onStartSession(packageId: string): void;
  onEndSession(): void;
  onLogout(): void;
  onOpenExplorer(): void;
}

export interface HomeScreen {
  root: HTMLElement;
  setPc(name: string, zone: string): void;
  /** Гость вошёл (сеанса ещё нет): имя, баланс и тарифы. */
  setLogin(user: string, balance: number, packages: TariffPackage[]): void;
  setBalance(balance: number): void;
  setSession(active: boolean): void;
  tick(remainingSec: number, balance: number): void;
  setCatalog(items: CatalogItem[]): void;
  launchResult(id: string, ok: boolean, error?: string): void;
  notice(text: string): void;
  startError(text: string): void;
  /** Сеанс закончился или гость вышел: очистить всё личное. */
  reset(): void;
}

type Tab = "game" | "app";
const CLUB_FILTER = "__club";
const FAV_FILTER = "__fav";

// Палитра обложек-заглушек в цветах клуба.
const COVER_PALETTE: [string, string][] = [
  ["#ff6b00", "#3b1500"], ["#e63946", "#3a0a10"], ["#ff9800", "#3d2400"], ["#c2185b", "#350817"],
  ["#5a4bff", "#10102e"], ["#00a8a8", "#062826"], ["#d84315", "#2e0f06"],
];

function paletteOf(text: string): [string, string] {
  let h = 0;
  for (const ch of text) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return COVER_PALETTE[h % COVER_PALETTE.length]!;
}

/** «Counter-Strike 2» → «CS2». */
function initialsOf(title: string): string {
  const parts = title.split(/[\s\-:.]+/).filter(Boolean);
  return parts.map((p) => (/^\d+$/.test(p) ? p : p[0]!)).join("").slice(0, 3).toUpperCase();
}

const store = {
  get<T>(key: string, fallback: T): T {
    try { return JSON.parse(localStorage.getItem(key) ?? "") as T; } catch { return fallback; }
  },
  set(key: string, value: unknown): void {
    try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* без хранилища просто не запоминаем */ }
  },
};

export function buildHome(h: HomeHandlers): HomeScreen {
  let items: CatalogItem[] = [];
  let tab: Tab = "game";
  let group: string | null = null;
  let query = "";
  let user = "";
  let balance = 0;
  let packages: TariffPackage[] = [];
  let sessionActive = false;
  let buying = false; // запрос на покупку тарифа уже ушёл
  let totalSec = 0;
  let left = 0;
  let favs = new Set<string>();
  let recent: string[] = [];

  // ---------- левая колонка ----------
  const navGames = el("button", { class: "hm-nav", attrs: { title: "Игры" }, html: icons.gamepad });
  const navApps = el("button", { class: "hm-nav", attrs: { title: "Приложения" }, html: icons.grid });
  const navTime = el("button", { class: "hm-nav", attrs: { title: "Время и тарифы" }, html: icons.clock });
  const navFiles = el("button", { class: "hm-nav", attrs: { title: "Мои файлы" }, html: icons.folder });
  const navExit = el("button", { class: "hm-nav exit", attrs: { title: "Выйти" }, html: icons.power });
  const rail = el("aside", { class: "hm-rail" }, [
    el("div", { class: "hm-logo", html: icons.club }),
    el("div", { class: "hm-navs" }, [navGames, navApps, navTime, navFiles]),
    navExit,
  ]);

  // ---------- верхняя часть ----------
  const tabGames = el("button", { class: "hm-tab", text: "Игры" });
  const tabApps = el("button", { class: "hm-tab", text: "Приложения" });
  const pill = el("div", { class: "hm-pill" });
  const buy = el("button", { class: "hm-buy", text: "Тарифы" });
  const top = el("header", { class: "hm-top" }, [el("div", { class: "hm-tabs" }, [tabGames, tabApps]), el("div", { class: "hm-top-right" }, [pill, buy])]);

  const search = el("input", { attrs: { type: "text", placeholder: "Поиск по названию", autocomplete: "off" } });
  const chips = el("div", { class: "hm-chips" });
  const toolbar = el("div", { class: "hm-toolbar" }, [el("div", { class: "hm-search" }, [el("span", { html: icons.search }), search]), chips]);

  const grid = el("div", { class: "hm-grid" });
  const empty = el("div", { class: "hm-empty", text: "Ничего не найдено" });
  const main = el("main", { class: "hm-main" }, [top, toolbar, el("div", { class: "hm-scroll" }, [grid, empty])]);

  // ---------- правая колонка ----------
  const avatar = el("div", { class: "hm-avatar", text: "?" });
  const userName = el("div", { class: "hm-user-name", text: "Гость" });
  const pcInfo = el("div", { class: "hm-user-sub", text: "" });
  const balanceVal = el("div", { class: "hm-big", text: "0 ₽" });
  const sessState = el("div", { class: "hm-sess-state" });
  const timeVal = el("div", { class: "hm-time brand-gradient", text: "--:--" });
  const bar = el("div", { class: "hm-bar-fill" });
  const sessBtn = el("button", { class: "hm-wide" });
  const sessCard = el("section", { class: "hm-card" }, [el("div", { class: "hm-card-title", text: "Сеанс" }), sessState, timeVal, el("div", { class: "hm-bar" }, [bar]), sessBtn]);
  const recentList = el("div", { class: "hm-recent" });
  const recentCard = el("section", { class: "hm-card" }, [el("div", { class: "hm-card-title", text: "Недавно запускали" }), recentList]);
  const side = el("aside", { class: "hm-side" }, [
    el("section", { class: "hm-card hm-profile" }, [avatar, el("div", {}, [userName, pcInfo])]),
    el("section", { class: "hm-card" }, [el("div", { class: "hm-card-title", text: "Баланс" }), balanceVal, el("div", { class: "hm-hint", text: "Пополнить баланс можно на стойке администратора" })]),
    sessCard,
    recentCard,
  ]);

  // ---------- окна ----------
  const toastEl = el("div", { class: "hm-toast" });
  let toastTimer: number | undefined;
  const toast = (text: string, kind: "ok" | "error") => {
    toastEl.textContent = text;
    toastEl.className = `hm-toast show ${kind}`;
    window.clearTimeout(toastTimer);
    toastTimer = window.setTimeout(() => (toastEl.className = "hm-toast"), 3200);
  };

  const modalBody = el("div", { class: "hm-modal-body" });
  const modalErr = el("div", { class: "hm-modal-err" });
  const modal = el("div", { class: "hm-overlay", attrs: { hidden: "" } }, [el("div", { class: "hm-modal" }, [modalBody, modalErr])]);
  const closeModal = () => ((modal.hidden = true), (modalErr.textContent = ""));
  modal.addEventListener("mousedown", (e) => e.target === modal && closeModal());
  window.addEventListener("keydown", (e) => e.key === "Escape" && !modal.hidden && closeModal());

  const root = el("div", { class: "screen hm" }, [rail, main, side, toastEl, modal]);

  function openTariffs(): void {
    const close = el("button", { class: "hm-x", html: icons.close, attrs: { title: "Закрыть" } });
    close.addEventListener("click", closeModal);
    const cards = packages.map((p) => {
      const locked = sessionActive || (p.nightOnly && !isNight()) || p.price > balance;
      const note = sessionActive ? "сеанс уже идёт" : p.nightOnly && !isNight() ? "22:00 – 08:00" : p.price > balance ? "не хватает средств" : p.nightOnly ? "22:00 – 08:00" : "";
      const b = el("button", { class: "hm-pkg", attrs: locked ? { disabled: "" } : {} }, [
        el("div", { class: "hm-pkg-name", text: p.name }),
        el("div", { class: "hm-pkg-price brand-gradient", text: `${p.price} ₽` }),
        el("div", { class: "hm-pkg-note", text: note }),
      ]);
      b.addEventListener("click", () => {
        if (buying) return; // двойной клик: второй запрос пришёл бы с ошибкой «сеанс уже идёт»
        buying = true;
        window.setTimeout(() => (buying = false), 4000);
        modalErr.textContent = "";
        h.onStartSession(p.id);
      });
      return b;
    });
    modalBody.replaceChildren(
      el("div", { class: "hm-modal-head" }, [el("div", {}, [el("div", { class: "hm-modal-title", text: "Выберите тариф" }), el("div", { class: "hm-muted", text: `На балансе ${balance} ₽` })]), close]),
      el("div", { class: "hm-pkgs" }, cards),
      el("div", { class: "hm-hint", text: sessionActive ? "Сеанс уже идёт. Новый тариф можно выбрать после его завершения." : "Время начнёт идти сразу после выбора." }),
    );
    modal.hidden = false;
  }

  function openEndConfirm(): void {
    const cancel = el("button", { class: "hm-btn", text: "Остаться" });
    const ok = el("button", { class: "hm-btn danger", text: "Завершить сеанс" });
    cancel.addEventListener("click", closeModal);
    ok.addEventListener("click", () => (closeModal(), h.onEndSession()));
    modalBody.replaceChildren(
      el("div", { class: "hm-modal-title", text: "Завершить сеанс?" }),
      el("p", { class: "hm-muted hm-para", text: "Компьютер перезагрузится. Всё, что вы скачали и сохранили на этом ПК, будет удалено. Сохраните нужное в облако или на свой накопитель." }),
      el("div", { class: "hm-actions" }, [cancel, ok]),
    );
    modal.hidden = false;
    cancel.focus();
  }

  // ---------- карточки игр ----------
  const byTab = () => items.filter((i) => i.kind === tab);

  function renderChips(): void {
    const list = byTab();
    const groups = [...new Set(list.map((i) => i.group).filter((g): g is string => !!g))];
    const hasClub = list.some((i) => i.clubAccount);
    const hasFav = list.some((i) => favs.has(i.id));
    if (group && group !== CLUB_FILTER && group !== FAV_FILTER && !groups.includes(group)) group = null;
    if (group === CLUB_FILTER && !hasClub) group = null;
    if (group === FAV_FILTER && !hasFav) group = null;
    const mk = (label: string, value: string | null) => {
      const b = el("button", { class: "hm-chip" + (group === value ? " active" : ""), text: label });
      b.addEventListener("click", () => ((group = value), renderChips(), renderGrid()));
      return b;
    };
    chips.replaceChildren(mk("Все", null), ...(hasFav ? [mk("Избранное", FAV_FILTER)] : []), ...groups.map((g) => mk(g, g)), ...(hasClub ? [mk("С клубными аккаунтами", CLUB_FILTER)] : []));
  }

  function card(item: CatalogItem): HTMLElement {
    const [from, to] = paletteOf(item.title);
    const cover = el("div", { class: "hm-cover" });
    cover.style.background = `radial-gradient(120% 80% at 20% 0%, ${from}, transparent 60%), linear-gradient(165deg, ${to}, #070707)`;
    const fallback = el("div", { class: "hm-cover-title", text: initialsOf(item.title) });
    cover.append(fallback);
    if (item.cover) {
      const img = el("img", { attrs: { src: item.cover, alt: "", draggable: "false" } });
      img.addEventListener("load", () => fallback.remove());
      img.addEventListener("error", () => img.remove());
      cover.prepend(img);
    }
    const badges = el("div", { class: "hm-badges" });
    if (item.minAge) badges.append(el("span", { class: "hm-badge age", text: `${item.minAge}+` }));
    if (item.clubAccount) badges.append(el("span", { class: "hm-badge club", text: "Клубный аккаунт" }));
    const heart = el("button", { class: "hm-heart" + (favs.has(item.id) ? " on" : ""), html: favs.has(item.id) ? icons.heartFill : icons.heart, attrs: { title: "В избранное" } });
    heart.addEventListener("click", (e) => {
      e.stopPropagation();
      if (favs.has(item.id)) favs.delete(item.id); else favs.add(item.id);
      store.set(`cs-fav:${user}`, [...favs]);
      renderChips();
      renderGrid();
    });
    cover.append(badges, heart, el("div", { class: "hm-play", html: icons.play }));

    const btn = el("div", { class: "hm-card-item", attrs: { role: "button", tabindex: "0" } }, [cover, el("div", { class: "hm-card-title2", text: item.title })]);
    const run = () => {
      if (!sessionActive) {
        toast("Сначала выберите тариф: время ещё не идёт", "error");
        openTariffs();
        return;
      }
      if (btn.classList.contains("busy")) return; // уже запускается: второй клик не откроет игру дважды
      btn.classList.add("busy");
      h.onLaunch(item.id);
      window.setTimeout(() => btn.classList.remove("busy"), 1500);
    };
    btn.addEventListener("click", run);
    btn.addEventListener("keydown", (e) => (e.key === "Enter" || e.key === " ") && (e.preventDefault(), run()));
    return btn;
  }

  function renderGrid(): void {
    const q = query.trim().toLowerCase();
    const shown = byTab().filter(
      (i) =>
        (group === null || (group === CLUB_FILTER ? !!i.clubAccount : group === FAV_FILTER ? favs.has(i.id) : i.group === group)) &&
        (q === "" || i.title.toLowerCase().includes(q)),
    );
    grid.replaceChildren(...shown.map(card));
    empty.hidden = shown.length > 0;
  }

  function renderRecent(): void {
    const list = recent.map((id) => items.find((i) => i.id === id)).filter((i): i is CatalogItem => !!i).slice(0, 5);
    recentCard.hidden = list.length === 0;
    recentList.replaceChildren(
      ...list.map((i) => {
        const [from, to] = paletteOf(i.title);
        const mini = el("div", { class: "hm-mini" });
        mini.style.background = `linear-gradient(135deg, ${from}, ${to})`;
        if (i.cover) mini.append(el("img", { attrs: { src: i.cover, alt: "", draggable: "false" } }));
        else mini.textContent = initialsOf(i.title);
        const row = el("button", { class: "hm-recent-row" }, [mini, el("span", { text: i.title })]);
        row.addEventListener("click", () => (sessionActive ? h.onLaunch(i.id) : (toast("Сначала выберите тариф", "error"), openTariffs())));
        return row;
      }),
    );
  }

  function renderTabs(): void {
    tabGames.classList.toggle("active", tab === "game");
    tabApps.classList.toggle("active", tab === "app");
    navGames.classList.toggle("active", tab === "game");
    navApps.classList.toggle("active", tab === "app");
    group = null;
    renderChips();
    renderGrid();
  }

  function renderSession(): void {
    pill.className = "hm-pill " + (sessionActive ? "on" : "off");
    pill.textContent = sessionActive ? `Сеанс идёт · ${formatTime(left)}` : "Нет активного сеанса";
    buy.textContent = sessionActive ? "Тарифы" : "Купить время";
    buy.classList.toggle("accent", !sessionActive);
    sessState.textContent = sessionActive ? "Идёт" : "Не начат";
    timeVal.textContent = sessionActive ? formatTime(left) : "—";
    timeVal.classList.toggle("low", sessionActive && left <= 300);
    bar.style.width = sessionActive && totalSec > 0 ? `${Math.max(0, Math.min(100, (left / totalSec) * 100))}%` : "0%";
    sessBtn.textContent = sessionActive ? "Завершить сеанс" : "Выбрать тариф";
    sessBtn.className = "hm-wide " + (sessionActive ? "danger" : "primary");
    navExit.title = sessionActive ? "Завершить сеанс" : "Выйти";
  }

  // ---------- события ----------
  tabGames.addEventListener("click", () => ((tab = "game"), renderTabs()));
  tabApps.addEventListener("click", () => ((tab = "app"), renderTabs()));
  navGames.addEventListener("click", () => ((tab = "game"), renderTabs()));
  navApps.addEventListener("click", () => ((tab = "app"), renderTabs()));
  navTime.addEventListener("click", openTariffs);
  navFiles.addEventListener("click", () => (sessionActive ? h.onOpenExplorer() : toast("Файлы доступны во время сеанса", "error")));
  buy.addEventListener("click", openTariffs);
  const sessAction = () => (sessionActive ? openEndConfirm() : openTariffs());
  sessBtn.addEventListener("click", sessAction);
  navExit.addEventListener("click", () => (sessionActive ? openEndConfirm() : h.onLogout()));
  search.addEventListener("input", () => ((query = search.value), renderGrid()));

  renderTabs();
  renderSession();
  renderRecent();

  return {
    root,
    setPc: (name, zone) => (pcInfo.textContent = `${name} · ${zone}`),
    setLogin(name, bal, pkgs) {
      user = name;
      balance = bal;
      packages = pkgs;
      userName.textContent = name || "Гость";
      avatar.textContent = (name || "?").slice(0, 1).toUpperCase();
      balanceVal.textContent = `${bal} ₽`;
      favs = new Set(store.get<string[]>(`cs-fav:${name}`, []));
      recent = store.get<string[]>(`cs-recent:${name}`, []);
      sessionActive = false;
      left = totalSec = 0;
      renderSession();
      renderTabs();
      renderRecent();
    },
    setBalance(b) {
      balance = b;
      balanceVal.textContent = `${b} ₽`;
      if (!modal.hidden) openTariffs(); // окно тарифов открыто: обновляем доступность
    },
    setSession(active) {
      sessionActive = active;
      if (!active) { left = totalSec = 0; }
      if (!modal.hidden) closeModal();
      renderSession();
    },
    tick(remaining, bal) {
      left = remaining;
      if (remaining > totalSec) totalSec = remaining; // первое значение или добавили время
      balance = bal;
      balanceVal.textContent = `${bal} ₽`;
      renderSession();
    },
    setCatalog(list) {
      items = list;
      renderTabs();
      renderRecent();
    },
    launchResult(id, ok, error) {
      const title = items.find((i) => i.id === id)?.title ?? id;
      if (ok) {
        recent = [id, ...recent.filter((x) => x !== id)].slice(0, 8);
        store.set(`cs-recent:${user}`, recent);
        renderRecent();
      }
      toast(ok ? `Запускаем: ${title}` : `${title}: ${error ?? "не удалось запустить"}`, ok ? "ok" : "error");
    },
    notice: (text) => toast(text, "ok"),
    startError(text) {
      modalErr.textContent = text;
      if (modal.hidden) toast(text, "error");
    },
    reset() {
      user = "";
      balance = 0;
      packages = [];
      sessionActive = false;
      left = totalSec = 0;
      favs = new Set();
      recent = [];
      search.value = query = "";
      closeModal();
      renderSession();
      renderRecent();
    },
  };
}
