import "@fontsource/inter/400.css";
import "@fontsource/inter/700.css";
import "@fontsource/russo-one/400.css";
import "./styles.css";

import { onMessage, send } from "./bridge";
import { el } from "./dom";
import { icons } from "./icons";
import type { FromHost, WindowItem } from "./protocol";

// Панель задач: отдельное окно Windows (AppBar) со своей страницей.
// Слева — кнопка «Рабочий стол» и открытые окна, справа — язык, звук, сеть, часы.

const app = document.getElementById("app")!;

// ---------- левая часть ----------
const deskBtn = el("button", { class: "tb-desk", attrs: { title: "Рабочий стол клуба" } }, [el("span", { html: icons.club }), "Рабочий стол"]);
deskBtn.addEventListener("click", () => send({ type: "showDesktop" }));
const list = el("div", { class: "tb-list" });
const mgrBtn = el("button", { class: "tb-mgr", text: "Диспетчер", attrs: { title: "Диспетчер задач (Ctrl+Shift+Esc)" } });
mgrBtn.addEventListener("click", () => send({ type: "openTaskManager" }));

// ---------- правая часть ----------
const lang = el("span", { class: "tb-text", text: "РУС" });

const volIcon = el("button", { class: "tb-btn", attrs: { title: "Выключить / включить звук" } });
const volRange = el("input", { class: "tb-range", attrs: { type: "range", min: "0", max: "100", step: "1", title: "Громкость" } });
const volLabel = el("span", { class: "tb-vol", text: "0" });
const volGroup = el("div", { class: "tb-vol-group" }, [volIcon, volRange, volLabel]);

const netIcon = el("div", { class: "tb-btn static", html: icons.network });
const netText = el("span", { class: "tb-text", text: "Сеть" });
const pingChip = el("span", { class: "tb-ping", text: "— мс", attrs: { title: "Задержка (пинг)" } });
const speedBtn = el("button", { class: "tb-speed", text: "Тест скорости", attrs: { title: "Измерить скорость интернета" } });
const clock = el("div", { class: "tb-clock" });

const tray = el("div", { class: "tb-tray" }, [mgrBtn, lang, volGroup, el("div", { class: "tb-net" }, [netIcon, netText, pingChip, speedBtn]), clock]);
app.append(el("div", { class: "tb" }, [deskBtn, list, tray]));

// ---------- окна ----------
let windows: WindowItem[] = [];

function initials(w: WindowItem): string {
  const name = (w.exe ?? w.title).replace(/\.exe$/i, "");
  return name.slice(0, 2).toUpperCase();
}

function renderWindows(): void {
  list.replaceChildren(
    ...windows.map((w) => {
      const icon = w.icon
        ? el("img", { class: "tb-ico", attrs: { src: w.icon, alt: "", draggable: "false" } })
        : el("span", { class: "tb-ico ph", text: initials(w) });
      const close = el("span", { class: "tb-close", text: "×", attrs: { title: "Закрыть окно" } });
      close.addEventListener("click", (e) => {
        e.stopPropagation();
        send({ type: "closeWindow", id: w.id });
      });
      const b = el("button", { class: "tb-win" + (w.active ? " active" : "") + (w.minimized ? " min" : ""), attrs: { title: w.title } }, [
        icon,
        el("span", { class: "tb-title", text: w.title }),
        close,
      ]);
      b.addEventListener("click", () => send({ type: "activateWindow", id: w.id }));
      // средняя кнопка мыши закрывает окно, как в обычной панели задач
      b.addEventListener("auxclick", (e) => {
        if (e.button === 1) send({ type: "closeWindow", id: w.id });
      });
      return b;
    }),
  );
}

// ---------- звук, сеть, часы ----------
let muted = false;
let volume = 0;
let dragging = false;

function renderVolume(): void {
  volIcon.innerHTML = muted || volume === 0 ? icons.muted : icons.volume;
  if (!dragging) volRange.value = String(volume);
  volLabel.textContent = muted ? "—" : String(volume);
}

volIcon.addEventListener("click", () => {
  muted = !muted;
  renderVolume();
  send({ type: "setMute", muted });
});
volRange.addEventListener("pointerdown", () => (dragging = true));
volRange.addEventListener("pointerup", () => (dragging = false));
volRange.addEventListener("input", () => {
  volume = Number(volRange.value);
  if (muted) {
    muted = false;
    send({ type: "setMute", muted });
  }
  renderVolume();
  send({ type: "setVolume", value: volume });
});

const updateClock = () => {
  const now = new Date();
  clock.innerHTML = `<b>${now.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })}</b><span>${now.toLocaleDateString("ru-RU")}</span>`;
};
updateClock();
window.setInterval(updateClock, 10_000);

// ---------- пинг и тест скорости ----------
let netBase = "Сеть";
let resultTimer: number | undefined;

function setNetText(text: string): void {
  netText.textContent = text;
}

speedBtn.addEventListener("click", () => {
  if (speedBtn.disabled) return;
  speedBtn.disabled = true;
  speedBtn.textContent = "Измеряем…";
  window.clearTimeout(resultTimer);
  send({ type: "speedTest" });
});

onMessage((msg: FromHost) => {
  switch (msg.type) {
    case "windows":
      windows = msg.items;
      renderWindows();
      break;
    case "audio":
      volume = msg.volume;
      muted = msg.muted;
      renderVolume();
      break;
    case "network": {
      netIcon.innerHTML = msg.online ? icons.network : icons.offline;
      netIcon.classList.toggle("bad", !msg.online);
      const speed = msg.speedMbps ? (msg.speedMbps >= 1000 ? `${msg.speedMbps / 1000} Гбит/с` : `${msg.speedMbps} Мбит/с`) : "";
      netBase = msg.online ? [msg.name ?? "Сеть", speed].filter(Boolean).join(" · ") : "Нет сети";
      if (!resultTimer) setNetText(netBase);
      break;
    }
    case "ping":
      pingChip.textContent = msg.ms === null ? "нет ответа" : `${msg.ms} мс`;
      pingChip.className = "tb-ping " + (msg.ms === null ? "bad" : msg.ms < 50 ? "good" : msg.ms < 120 ? "mid" : "bad");
      break;
    case "speedResult":
      speedBtn.disabled = false;
      speedBtn.textContent = "Тест скорости";
      if (!msg.ok) {
        setNetText(msg.error ?? "Тест не удался");
      } else {
        const f = (v?: number) => (v === undefined ? "—" : v >= 100 ? Math.round(v) : v);
        setNetText(`↓ ${f(msg.downMbps)}  ↑ ${f(msg.upMbps)} Мбит/с`);
      }
      resultTimer = window.setTimeout(() => {
        resultTimer = undefined;
        setNetText(netBase);
      }, 15_000);
      break;
  }
});

window.addEventListener("contextmenu", (e) => e.preventDefault());
renderVolume();
send({ type: "ready" });
