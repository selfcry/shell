import "@fontsource/inter/400.css";
import "@fontsource/inter/700.css";
import "@fontsource/russo-one/400.css";
import "./styles.css";

import { onMessage, send } from "./bridge";
import { el } from "./dom";
import type { FromHost, ProcessItem } from "./protocol";

// Диспетчер задач клуба: отдельное окно. Показывает приложения и фоновые процессы гостя, позволяет снять задачу.
// Системные процессы и сама оболочка в список не попадают (их отсеивает хост).

const app = document.getElementById("app")!;

type SortKey = "mem" | "cpu" | "name";
let sort: SortKey = "mem";
let items: ProcessItem[] = [];
let armed: number | null = null; // pid, для которого нажали «Снять» один раз
let armTimer: number | undefined;

const closeBtn = el("button", { class: "tm-close", text: "×", attrs: { title: "Закрыть" } });
closeBtn.addEventListener("click", () => send({ type: "closeTaskManager" }));
window.addEventListener("keydown", (e) => e.key === "Escape" && send({ type: "closeTaskManager" }));

const cpuVal = el("div", { class: "tm-stat-val", text: "0%" });
const cpuBar = el("div", { class: "tm-bar-fill" });
const memVal = el("div", { class: "tm-stat-val", text: "0%" });
const memBar = el("div", { class: "tm-bar-fill" });
const stat = (label: string, val: HTMLElement, bar: HTMLElement) =>
  el("div", { class: "tm-stat" }, [el("div", { class: "tm-stat-head" }, [el("span", { text: label }), val]), el("div", { class: "tm-bar" }, [bar])]);

const sortBtns = (["mem", "cpu", "name"] as SortKey[]).map((k) => {
  const b = el("button", { class: "tm-sort", text: { mem: "Память", cpu: "ЦП", name: "Имя" }[k] });
  b.addEventListener("click", () => ((sort = k), render()));
  return [k, b] as const;
});

const body = el("div", { class: "tm-body" });
app.append(
  el("div", { class: "tm" }, [
    el("div", { class: "tm-head" }, [el("div", { class: "tm-title", text: "Диспетчер задач" }), closeBtn]),
    el("div", { class: "tm-stats" }, [stat("Процессор", cpuVal, cpuBar), stat("Память", memVal, memBar)]),
    el("div", { class: "tm-sortbar" }, [el("span", { text: "Сортировка:" }), ...sortBtns.map(([, b]) => b)]),
    body,
  ]),
);

function compare(a: ProcessItem, b: ProcessItem): number {
  if (sort === "name") return (a.title ?? a.name).localeCompare(b.title ?? b.name);
  return sort === "cpu" ? b.cpu - a.cpu : b.memMb - a.memMb;
}

function fmtMem(mb: number): string {
  return mb >= 1024 ? `${(mb / 1024).toFixed(1)} ГБ` : `${Math.round(mb)} МБ`;
}

function row(p: ProcessItem): HTMLElement {
  const end = el("button", { class: "tm-end" + (armed === p.pid ? " armed" : ""), text: armed === p.pid ? "Точно?" : "Снять" });
  end.addEventListener("click", () => {
    if (armed === p.pid) {
      armed = null;
      send({ type: "endTask", pid: p.pid });
    } else {
      armed = p.pid;
      window.clearTimeout(armTimer);
      armTimer = window.setTimeout(() => ((armed = null), render()), 3000);
    }
    render();
  });
  return el("div", { class: "tm-row" }, [
    el("div", { class: "tm-name" }, [el("b", { text: p.title ?? p.name }), el("span", { text: p.title ? p.name : "" })]),
    el("div", { class: "tm-num", text: `${p.cpu}%` }),
    el("div", { class: "tm-num", text: fmtMem(p.memMb) }),
    end,
  ]);
}

function section(title: string, list: ProcessItem[]): HTMLElement[] {
  if (list.length === 0) return [];
  return [el("div", { class: "tm-section", text: `${title} (${list.length})` }), ...list.sort(compare).map(row)];
}

function render(): void {
  for (const [k, b] of sortBtns) b.classList.toggle("active", k === sort);
  body.replaceChildren(...section("Приложения", items.filter((p) => p.app)), ...section("Фоновые процессы", items.filter((p) => !p.app)));
}

onMessage((msg: FromHost) => {
  if (msg.type !== "processes") return;
  items = msg.items;
  cpuVal.textContent = `${Math.round(msg.cpu)}%`;
  cpuBar.style.width = `${Math.min(100, msg.cpu)}%`;
  const memPct = msg.memTotalMb > 0 ? (msg.memUsedMb / msg.memTotalMb) * 100 : 0;
  memVal.textContent = `${fmtMem(msg.memUsedMb)} из ${fmtMem(msg.memTotalMb)}`;
  memBar.style.width = `${Math.min(100, memPct)}%`;
  render();
});

window.addEventListener("contextmenu", (e) => e.preventDefault());
send({ type: "ready" });
