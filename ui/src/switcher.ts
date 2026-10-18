import "@fontsource/inter/400.css";
import "@fontsource/inter/700.css";
import "./styles.css";

import { onMessage } from "./bridge";
import { el } from "./dom";
import type { FromHost, SwitcherItem } from "./protocol";

// Свой Alt+Tab. Выбор делает хост (клавиши идут через хук), страница только рисует.

const app = document.getElementById("app")!;
const row = el("div", { class: "sw-row" });
const title = el("div", { class: "sw-title" });
app.append(el("div", { class: "sw" }, [row, title]));

let items: SwitcherItem[] = [];
let selected = 0;

function render(): void {
  row.replaceChildren(
    ...items.map((w, i) => {
      const icon = w.icon ? el("img", { attrs: { src: w.icon, alt: "", draggable: "false" } }) : el("div", { class: "sw-ph", text: (w.exe ?? w.title).slice(0, 2).toUpperCase() });
      return el("div", { class: "sw-item" + (i === selected ? " on" : "") }, [icon]);
    }),
  );
  title.textContent = items[selected]?.title ?? "";
}

onMessage((msg: FromHost) => {
  if (msg.type === "switcher") {
    items = msg.items;
    selected = msg.selected;
    render();
  } else if (msg.type === "select") {
    selected = msg.selected;
    render();
  }
});
