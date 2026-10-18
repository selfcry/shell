import "@fontsource/inter/400.css";
import "@fontsource/inter/700.css";
import "@fontsource/russo-one/400.css";
import "./styles.css";

import { onMessage, send, isMock } from "./bridge";
import { el } from "./dom";
import type { FromHost } from "./protocol";
import { buildIntro } from "./screens/intro";
import { buildLogin } from "./screens/login";
import { buildTariffs } from "./screens/tariffs";
import { buildDesktop } from "./screens/desktop";

type ScreenName = "intro" | "login" | "tariffs" | "desktop";

const app = document.getElementById("app")!;
const IDLE_MS = 60_000; // вернуться на заставку, если гость ничего не делает

const backdrop = el("div", { class: "backdrop" });
const intro = buildIntro(() => show("login"), (pin) => send({ type: "adminExit", pin }));
const login = buildLogin((u, p) => send({ type: "login", username: u, password: p }), () => show("intro"));
const tariffs = buildTariffs((id) => send({ type: "startSession", packageId: id }), () => show("intro"));
const desktop = buildDesktop({
  onLaunch: (id) => send({ type: "launch", id }),
  onStop: () => send({ type: "stopSession" }),
});

const rebootText = el("div", { class: "reboot-text", text: "" });
const reboot = el("div", { class: "reboot" }, [el("div", { class: "reboot-spinner" }), rebootText]);

app.append(backdrop, intro.root, login.root, tariffs.root, desktop.root, reboot);

const screens: Record<ScreenName, { root: HTMLElement; onShow?: () => void }> = {
  intro, login, tariffs, desktop,
};

let current: ScreenName = "intro";
let idleTimer: number | undefined;

function show(name: ScreenName): void {
  current = name;
  for (const [key, s] of Object.entries(screens)) s.root.classList.toggle("active", key === name);
  document.body.classList.toggle("on-desktop", name === "desktop");
  screens[name].onShow?.();
  armIdle();
}

function armIdle(): void {
  window.clearTimeout(idleTimer);
  if (current === "login" || current === "tariffs") {
    idleTimer = window.setTimeout(() => show("intro"), IDLE_MS);
  }
}
for (const ev of ["keydown", "mousemove", "mousedown"]) window.addEventListener(ev, armIdle, { passive: true });

onMessage((msg: FromHost) => {
  switch (msg.type) {
    case "config":
      intro.setPc(msg.pcName, msg.zone);
      desktop.setPc(msg.pcName, msg.zone);
      break;
    case "link":
      intro.setOffline(!msg.online, msg.reason);
      // Связь пропала, пока гость вводил логин или выбирал тариф: возвращаем на заставку.
      if (!msg.online && (current === "login" || current === "tariffs")) show("intro");
      break;
    case "mode":
      if (msg.mode === "session") show("desktop");
      else if (current === "desktop") show("intro");
      break;
    case "loginResult":
      if (msg.ok) {
        desktop.setUser(msg.user ?? "");
        tariffs.setData(msg.user ?? "", msg.balance ?? 0, msg.packages ?? []);
        show("tariffs");
      } else {
        login.setError(msg.error ?? "Не удалось войти");
      }
      break;
    case "startResult":
      if (!msg.ok) tariffs.setError(msg.error ?? "Не удалось начать сессию");
      break;
    case "tick":
      desktop.tick(msg.remainingSec, msg.balance);
      break;
    case "sessionEnded":
      intro.setNotice(msg.reason);
      break;
    case "notice":
      if (current === "desktop") desktop.notice(msg.text);
      else intro.setNotice(msg.text);
      break;
    case "rebooting":
      rebootText.textContent = msg.test ? "Тестовый режим: перезагрузка и очистка пропущены" : "Перезагрузка… Файлы гостя удаляются";
      reboot.classList.add("show");
      if (msg.test) window.setTimeout(() => reboot.classList.remove("show"), 2500);
      break;
    case "catalog":
      desktop.setCatalog(msg.items);
      break;
    case "launchResult":
      desktop.launchResult(msg.id, msg.ok, msg.error);
      break;
    case "adminExitResult":
      intro.adminResult(msg.ok);
      break;
  }
});

// Запрещаем контекстное меню — это киоск, а не сайт.
window.addEventListener("contextmenu", (e) => e.preventDefault());

show("intro");
send({ type: "ready" });
if (isMock) console.info("ClubShell UI: макет хоста (логин demo / demo, админ-PIN 0000)");
