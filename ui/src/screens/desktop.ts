import { el, formatTime } from "../dom";
import { icons } from "../icons";
import type { CatalogItem } from "../protocol";

export interface DesktopHandlers {
  onLaunch(id: string): void;
  onStop(): void;
}

export interface DesktopScreen {
  root: HTMLElement;
  setPc(name: string, zone: string): void;
  setUser(name: string): void;
  setCatalog(items: CatalogItem[]): void;
  tick(remainingSec: number, balance: number): void;
  setBalance(balance: number): void;
  launchResult(id: string, ok: boolean, error?: string): void;
  notice(text: string): void;
}

type Tab = "game" | "app";
const CLUB_FILTER = "__club";

// Палитра обложек-заглушек в цветах клуба (оранжевый, красный, янтарь, бордо, тёмно-синий).
const COVER_PALETTE: [string, string][] = [
  ["#ff6b00", "#3b1500"],
  ["#e63946", "#3a0a10"],
  ["#ff9800", "#3d2400"],
  ["#c2185b", "#350817"],
  ["#5a4bff", "#10102e"],
  ["#00a8a8", "#062826"],
  ["#d84315", "#2e0f06"],
];

function paletteOf(text: string): [string, string] {
  let h = 0;
  for (const ch of text) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return COVER_PALETTE[h % COVER_PALETTE.length]!;
}

/** «Counter-Strike 2» → «CS2», «Dota 2» → «D2». */
function initialsOf(title: string): string {
  const parts = title.split(/[\s\-:.]+/).filter(Boolean);
  const letters = parts.map((p) => (/^\d+$/.test(p) ? p : p[0]!)).join("");
  return letters.slice(0, 3).toUpperCase();
}

export function buildDesktop(h: DesktopHandlers): DesktopScreen {
  let items: CatalogItem[] = [];
  let tab: Tab = "game";
  let group: string | null = null; // null = все, CLUB_FILTER = клубные аккаунты
  let query = "";

  // ---------- верхняя панель ----------
  const tabGames = el("button", { class: "dt-tab" }, [el("span", { html: icons.gamepad }), "Игры"]);
  const tabApps = el("button", { class: "dt-tab" }, [el("span", { html: icons.grid }), "Приложения"]);
  const pcPill = el("div", { class: "dt-pill", text: "…" });

  const userName = el("div", { class: "dt-user-name", text: "Гость" });
  const userMeta = el("div", { class: "dt-user-meta", text: "" });
  const remain = el("div", { class: "dt-remain brand-gradient", text: "--:--" });
  const stop = el("button", { class: "dt-stop", attrs: { title: "Завершить сеанс" }, html: icons.power });
  const userCard = el("div", { class: "dt-user" }, [
    el("div", { class: "dt-avatar", html: icons.user }),
    el("div", {}, [userName, userMeta]),
    el("div", { class: "dt-remain-box" }, [remain, el("div", { class: "dt-user-meta", text: "осталось" })]),
    stop,
  ]);

  const top = el("header", { class: "dt-top" }, [
    el("div", { class: "dt-brand" }, [el("span", { class: "dt-brand-word brand-gradient", text: "МИРАЖ" }), el("span", { class: "dt-brand-pro", text: "PRO" })]),
    el("nav", { class: "dt-tabs" }, [tabGames, tabApps]),
    pcPill,
    userCard,
  ]);

  // ---------- поиск и фильтры ----------
  const search = el("input", { attrs: { type: "text", placeholder: "Поиск по названию", autocomplete: "off" } });
  const chips = el("div", { class: "dt-chips" });
  const toolbar = el("div", { class: "dt-toolbar" }, [
    el("div", { class: "dt-search" }, [el("span", { html: icons.search }), search]),
    chips,
  ]);

  // ---------- сетка ----------
  const grid = el("div", { class: "dt-grid" });
  const empty = el("div", { class: "dt-empty", text: "Ничего не найдено" });
  const content = el("main", { class: "dt-content" }, [toolbar, grid, empty]);

  // ---------- уведомление ----------
  const toastEl = el("div", { class: "dt-toast" });
  let toastTimer: number | undefined;
  const toast = (text: string, kind: "ok" | "error") => {
    toastEl.textContent = text;
    toastEl.className = `dt-toast show ${kind}`;
    window.clearTimeout(toastTimer);
    toastTimer = window.setTimeout(() => (toastEl.className = "dt-toast"), 2800);
  };

  const root = el("div", { class: "screen dt" }, [top, content, toastEl]);

  // ---------- отрисовка ----------
  function tabItems(): CatalogItem[] {
    return items.filter((i) => i.kind === tab);
  }

  function renderChips(): void {
    const list = tabItems();
    const groups = [...new Set(list.map((i) => i.group).filter((g): g is string => !!g))];
    const hasClub = list.some((i) => i.clubAccount);
    if (group && group !== CLUB_FILTER && !groups.includes(group)) group = null;
    if (group === CLUB_FILTER && !hasClub) group = null;

    const mk = (label: string, value: string | null) => {
      const b = el("button", { class: "dt-chip" + (group === value ? " active" : ""), text: label });
      b.addEventListener("click", () => {
        group = value;
        renderChips();
        renderGrid();
      });
      return b;
    };
    chips.replaceChildren(mk("Все", null), ...groups.map((g) => mk(g, g)), ...(hasClub ? [mk("С клубными аккаунтами", CLUB_FILTER)] : []));
  }

  function card(item: CatalogItem): HTMLElement {
    const [from, to] = paletteOf(item.title);
    const cover = el("div", { class: "dt-cover" });
    cover.style.background = `radial-gradient(120% 80% at 20% 0%, ${from}, transparent 60%), linear-gradient(165deg, ${to}, #070707)`;
    const fallback = el("div", { class: "dt-cover-title", text: initialsOf(item.title) });
    cover.append(fallback);
    if (item.cover) {
      const img = el("img", { attrs: { src: item.cover, alt: "", draggable: "false" } });
      img.addEventListener("load", () => fallback.remove());
      img.addEventListener("error", () => img.remove());
      cover.prepend(img);
    }
    const badges = el("div", { class: "dt-badges" });
    if (item.minAge) badges.append(el("span", { class: "dt-badge age", text: `${item.minAge}+` }));
    if (item.clubAccount) badges.append(el("span", { class: "dt-badge club", text: "Клубный аккаунт" }));
    cover.append(badges);

    const btn = el("button", { class: "dt-card" }, [cover, el("div", { class: "dt-card-title", text: item.title })]);
    btn.addEventListener("click", () => {
      btn.classList.add("busy");
      h.onLaunch(item.id);
      window.setTimeout(() => btn.classList.remove("busy"), 1500);
    });
    return btn;
  }

  function renderGrid(): void {
    const q = query.trim().toLowerCase();
    const shown = tabItems().filter(
      (i) =>
        (group === null || (group === CLUB_FILTER ? !!i.clubAccount : i.group === group)) &&
        (q === "" || i.title.toLowerCase().includes(q)),
    );
    grid.replaceChildren(...shown.map(card));
    empty.hidden = shown.length > 0;
  }

  function renderTab(): void {
    tabGames.classList.toggle("active", tab === "game");
    tabApps.classList.toggle("active", tab === "app");
    group = null;
    renderChips();
    renderGrid();
  }

  // ---------- события ----------
  tabGames.addEventListener("click", () => ((tab = "game"), renderTab()));
  tabApps.addEventListener("click", () => ((tab = "app"), renderTab()));
  search.addEventListener("input", () => ((query = search.value), renderGrid()));

  // Завершение сеанса необратимо: ПК перезагружается, файлы гостя удаляются — спрашиваем подтверждение.
  const cancel = el("button", { class: "dlg-btn", text: "Остаться" });
  const confirm = el("button", { class: "dlg-btn danger", text: "Завершить сеанс" });
  const dialog = el("div", { class: "dlg", attrs: { hidden: "" } }, [
    el("div", { class: "dlg-box" }, [
      el("div", { class: "dlg-title", text: "Завершить сеанс?" }),
      el("div", {
        class: "dlg-text",
        text: "Компьютер перезагрузится. Всё, что вы скачали и сохранили на этом ПК, будет удалено. Сохраните нужное в облако или на свой накопитель.",
      }),
      el("div", { class: "dlg-actions" }, [cancel, confirm]),
    ]),
  ]);
  root.append(dialog);
  const closeDialog = () => (dialog.hidden = true);
  stop.addEventListener("click", () => ((dialog.hidden = false), cancel.focus()));
  cancel.addEventListener("click", closeDialog);
  dialog.addEventListener("click", (e) => e.target === dialog && closeDialog());
  confirm.addEventListener("click", () => {
    closeDialog();
    h.onStop();
  });

  renderTab();

  return {
    root,
    setPc: (name, zone) => (pcPill.textContent = `${name} · ${zone}`),
    setUser(name) {
      userName.textContent = name || "Гость";
    },
    setCatalog(list) {
      items = list;
      renderTab();
    },
    setBalance: (balance) => (userMeta.textContent = `Баланс ${balance} ₽`),
    tick(remainingSec, balance) {
      remain.textContent = formatTime(remainingSec);
      remain.classList.toggle("low", remainingSec <= 300);
      userMeta.textContent = `Баланс ${balance} ₽`;
    },
    notice: (text) => toast(text, "ok"),
    launchResult(id, ok, error) {
      const title = items.find((i) => i.id === id)?.title ?? id;
      toast(ok ? `Запускаем: ${title}` : `${title}: ${error ?? "не удалось запустить"}`, ok ? "ok" : "error");
    },
  };
}
