import "@fontsource/inter/400.css";
import "@fontsource/inter/700.css";
import "@fontsource/russo-one/400.css";
import "./styles.css";

import { onMessage, send } from "./bridge";
import { el } from "./dom";
import { icons } from "./icons";
import type { FromHost, FsEntry, FsRoot } from "./protocol";

// «Мои файлы»: собственный проводник гостя. Доступны только папки профиля и внешние диски, всё остальное хост не отдаёт.

type Result = Extract<FromHost, { type: "fsResult" }>;
const pending = new Map<number, (r: Result) => void>();
let nextId = 1;

function fs(op: "roots" | "list" | "open" | "mkdir" | "rename" | "delete" | "copy" | "move", extra: { path?: string; name?: string; dest?: string } = {}): Promise<Result> {
  return new Promise((resolve) => {
    const id = nextId++;
    pending.set(id, resolve);
    send({ type: "fs", id, op, ...extra });
  });
}

let roots: FsRoot[] = [];
let cwd = "";
let entries: FsEntry[] = [];
let selected: string | null = null;
let clip: { path: string; cut: boolean } | null = null;
let filter = "";
const history: string[] = [];

const sep = (p: string) => (p.includes("\\") ? "\\" : "/");
const join = (dir: string, name: string) => dir.replace(/[\\/]$/, "") + sep(dir) + name;
const parent = (p: string) => p.replace(/[\\/][^\\/]*$/, "");
const rootOf = (p: string) => roots.find((r) => p.toLowerCase().startsWith(r.path.toLowerCase().replace(/[\\/]$/, "")));

function size(n: number): string {
  if (n < 1024) return `${n} Б`;
  if (n < 1048576) return `${(n / 1024).toFixed(0)} КБ`;
  if (n < 1073741824) return `${(n / 1048576).toFixed(1)} МБ`;
  return `${(n / 1073741824).toFixed(1)} ГБ`;
}
const when = (s: number) => new Date(s * 1000).toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", year: "2-digit", hour: "2-digit", minute: "2-digit" });

function glyph(e: FsEntry): string {
  if (e.isDir) return "📁";
  const ext = e.name.split(".").pop()?.toLowerCase() ?? "";
  if (["png", "jpg", "jpeg", "gif", "webp", "bmp"].includes(ext)) return "🖼️";
  if (["mp4", "mkv", "avi", "mov", "webm"].includes(ext)) return "🎞️";
  if (["mp3", "wav", "flac", "ogg"].includes(ext)) return "🎵";
  if (["zip", "rar", "7z"].includes(ext)) return "🗜️";
  if (["exe", "msi"].includes(ext)) return "⚙️";
  return "📄";
}

// ---------- разметка ----------
const side = el("div", { class: "ex-side" });
const crumbs = el("div", { class: "ex-crumbs" });
const back = el("button", { class: "ex-btn icon", text: "←", attrs: { title: "Назад" } });
const up = el("button", { class: "ex-btn icon", text: "↑", attrs: { title: "Вверх" } });
const search = el("input", { class: "ex-search", attrs: { type: "text", placeholder: "Поиск в папке", autocomplete: "off" } });
const closeBtn = el("button", { class: "ex-btn icon", html: icons.close, attrs: { title: "Закрыть" } });
const list = el("div", { class: "ex-list" });
const status = el("div", { class: "ex-status" });
const toast = el("div", { class: "ex-toast" });
const dialog = el("div", { class: "ex-dlg", attrs: { hidden: "" } });

const mk = (label: string, fn: () => void) => {
  const b = el("button", { class: "ex-btn", text: label });
  b.addEventListener("click", fn);
  return b;
};
const bNew = mk("Новая папка", () => ask("Новая папка", "Имя папки", "", async (n) => act(await fs("mkdir", { path: cwd, name: n }))));
const bCopy = mk("Копировать", () => selected && (clip = { path: join(cwd, selected), cut: false }, note("Скопировано"), paint()));
const bCut = mk("Вырезать", () => selected && (clip = { path: join(cwd, selected), cut: true }, note("Вырезано"), paint()));
const bPaste = mk("Вставить", async () => clip && act(await fs(clip.cut ? "move" : "copy", { path: clip.path, dest: cwd }), () => { if (clip?.cut) clip = null; }));
const bRename = mk("Переименовать", () => selected && ask("Переименовать", "Новое имя", selected, async (n) => act(await fs("rename", { path: join(cwd, selected!), name: n }))));
const bDelete = mk("Удалить", () => selected && confirmDlg(`Удалить «${selected}»?`, "Файл будет удалён без возможности восстановления.", async () => act(await fs("delete", { path: join(cwd, selected!) }))));
const bOpen = mk("Открыть", () => open());

document.getElementById("app")!.append(
  el("div", { class: "ex" }, [
    el("div", { class: "ex-head" }, [el("div", { class: "ex-title", text: "Мои файлы" }), closeBtn]),
    el("div", { class: "ex-body" }, [
      side,
      el("div", { class: "ex-main" }, [
        el("div", { class: "ex-nav" }, [back, up, crumbs, search]),
        el("div", { class: "ex-tools" }, [bOpen, bNew, bCopy, bCut, bPaste, bRename, bDelete]),
        el("div", { class: "ex-cols" }, [el("span", { text: "Имя" }), el("span", { text: "Изменён" }), el("span", { text: "Размер" })]),
        list,
        status,
      ]),
    ]),
    toast,
    dialog,
  ]),
);

let toastTimer: number | undefined;
function note(text: string, error = false): void {
  toast.textContent = text;
  toast.className = "ex-toast show" + (error ? " error" : "");
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => (toast.className = "ex-toast"), 2800);
}

function ask(title: string, label: string, value: string, ok: (v: string) => void): void {
  const input = el("input", { class: "ex-input", attrs: { type: "text", value, placeholder: label } });
  const go = () => (dialog.hidden = true, ok(input.value.trim()));
  input.addEventListener("keydown", (e) => (e.key === "Enter" ? go() : e.key === "Escape" && (dialog.hidden = true)));
  dialog.replaceChildren(el("div", { class: "ex-box" }, [el("h3", { text: title }), input, el("div", { class: "ex-actions" }, [mk("Отмена", () => (dialog.hidden = true)), mk("ОК", go)])]));
  dialog.hidden = false;
  input.focus();
  input.select();
}

function confirmDlg(title: string, text: string, ok: () => void): void {
  dialog.replaceChildren(el("div", { class: "ex-box" }, [el("h3", { text: title }), el("p", { text }), el("div", { class: "ex-actions" }, [mk("Отмена", () => (dialog.hidden = true)), mk("Удалить", () => ((dialog.hidden = true), ok()))])]));
  dialog.hidden = false;
}

async function act(r: Result, after?: () => void): Promise<void> {
  if (!r.ok) return note(r.error ?? "Не получилось", true);
  after?.();
  await go(cwd, false);
}

async function open(): Promise<void> {
  if (!selected) return;
  const e = entries.find((x) => x.name === selected);
  if (!e) return;
  if (e.isDir) return void (await go(join(cwd, e.name)));
  const r = await fs("open", { path: join(cwd, e.name) });
  if (!r.ok) note(r.error ?? "Не удалось открыть", true);
}

async function go(path: string, push = true): Promise<void> {
  const r = await fs("list", { path });
  if (!r.ok) return note(r.error ?? "Папка недоступна", true);
  if (push && cwd && cwd !== path) history.push(cwd);
  cwd = path;
  entries = r.entries ?? [];
  selected = null;
  filter = "";
  search.value = "";
  paint();
}

function paint(): void {
  side.replaceChildren(
    el("div", { class: "ex-side-title", text: "Быстрый доступ" }),
    ...roots.map((r) => {
      const b = el("button", { class: "ex-root" + (rootOf(cwd)?.path === r.path ? " on" : ""), text: r.name });
      b.addEventListener("click", () => void go(r.path));
      return b;
    }),
  );
  const root = rootOf(cwd);
  const rel = root ? cwd.slice(root.path.replace(/[\\/]$/, "").length).split(/[\\/]/).filter(Boolean) : [];
  crumbs.replaceChildren(
    ...(root ? [Object.assign(el("button", { class: "ex-crumb", text: root.name }), { onclick: () => void go(root.path) })] : []),
    ...rel.flatMap((part, i) => {
      const target = join(root!.path, rel.slice(0, i + 1).join(sep(root!.path)));
      const b = el("button", { class: "ex-crumb", text: part });
      b.addEventListener("click", () => void go(target));
      return [el("span", { class: "ex-sepc", text: "›" }), b];
    }),
  );
  const shown = entries.filter((e) => e.name.toLowerCase().includes(filter.toLowerCase()));
  list.replaceChildren(
    ...shown.map((e) => {
      const row = el("div", { class: "ex-row" + (e.name === selected ? " sel" : "") + (clip?.cut && clip.path === join(cwd, e.name) ? " cut" : "") }, [
        el("span", { class: "ex-name" }, [el("i", { text: glyph(e) }), e.name]),
        el("span", { class: "ex-muted", text: when(e.modified) }),
        el("span", { class: "ex-muted", text: e.isDir ? "" : size(e.size) }),
      ]);
      row.addEventListener("click", () => ((selected = e.name), paint()));
      row.addEventListener("dblclick", () => ((selected = e.name), void open()));
      return row;
    }),
  );
  status.textContent = shown.length === 0 ? "Папка пуста. Файлы на этом компьютере удаляются после завершения сеанса." : `Элементов: ${shown.length}. Файлы на этом компьютере удаляются после завершения сеанса.`;
  for (const b of [bCopy, bCut, bRename, bDelete, bOpen]) b.disabled = !selected;
  bPaste.disabled = !clip;
  up.disabled = !root || cwd.replace(/[\\/]$/, "") === root.path.replace(/[\\/]$/, "");
  back.disabled = history.length === 0;
}

back.addEventListener("click", () => { const p = history.pop(); if (p) void go(p, false); });
up.addEventListener("click", () => void go(parent(cwd)));
search.addEventListener("input", () => ((filter = search.value), paint()));
closeBtn.addEventListener("click", () => send({ type: "closeExplorer" }));
window.addEventListener("keydown", (e) => {
  if (!dialog.hidden || document.activeElement === search) return;
  if (e.key === "Escape") send({ type: "closeExplorer" });
  else if (e.key === "Enter") void open();
  else if (e.key === "Delete") bDelete.click();
  else if (e.key === "F2") bRename.click();
  else if (e.key === "Backspace") up.click();
  else if (e.ctrlKey && e.key.toLowerCase() === "c") bCopy.click();
  else if (e.ctrlKey && e.key.toLowerCase() === "x") bCut.click();
  else if (e.ctrlKey && e.key.toLowerCase() === "v") bPaste.click();
});

onMessage((msg: FromHost) => {
  if (msg.type !== "fsResult") return;
  pending.get(msg.id)?.(msg);
  pending.delete(msg.id);
});

window.addEventListener("contextmenu", (e) => e.preventDefault());
void (async () => {
  const r = await fs("roots");
  roots = r.roots ?? [];
  if (roots[0]) await go(roots[0].path);
  else paint();
})();
