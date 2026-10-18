import "@fontsource/inter/400.css";
import "@fontsource/inter/700.css";
import "@fontsource/russo-one/400.css";
import "./styles.css";
import "./home.css";

import { onMessage, send, isMock } from "./bridge";
import { el } from "./dom";
import type { FromHost } from "./protocol";
import { buildIntro } from "./screens/intro";
import { buildLogin } from "./screens/login";
import { buildHome } from "./screens/home";

type ScreenName = "intro" | "login" | "home";

const app = document.getElementById("app")!;
const IDLE_LOGIN_MS = 60_000; // форма входа без движения: назад на заставку
const IDLE_HOME_MS = 180_000; // гость вошёл, но время не купил и ничего не делает: выходим

// Размеры интерфейса растут вместе с экраном (1920 пикселей = 16 px на единицу), поэтому на любом мониторе пропорции те же.
const fit = () => (document.documentElement.style.fontSize = `${Math.max(11, Math.min(22, window.innerWidth / 120))}px`);
fit();
window.addEventListener("resize", fit);

const backdrop = el("div", { class: "backdrop" });
const intro = buildIntro(() => show("login"), (pin) => send({ type: "adminExit", pin }));
const login = buildLogin({
  onLogin: (u, p) => send({ type: "login", username: u, password: p }),
  onRegister: (u, p, birth) => send({ type: "register", username: u, password: p, birth }),
  onBack: () => show("intro"),
});
const home = buildHome({
  onLaunch: (id) => send({ type: "launch", id }),
  onStartSession: (packageId) => send({ type: "startSession", packageId }),
  onEndSession: () => send({ type: "stopSession" }),
  onLogout: () => logout(),
  onOpenExplorer: () => send({ type: "openExplorer" }),
});

const rebootText = el("div", { class: "reboot-text", text: "" });
const reboot = el("div", { class: "reboot" }, [el("div", { class: "reboot-spinner" }), rebootText]);

app.append(backdrop, intro.root, login.root, home.root, reboot);

const screens: Record<ScreenName, { root: HTMLElement; onShow?: () => void }> = { intro, login, home };

let current: ScreenName = "intro";
let loggedIn = false; // гость вошёл (время может быть ещё не куплено)
let sessionActive = false;
let idleTimer: number | undefined;

function show(name: ScreenName): void {
  current = name;
  for (const [key, s] of Object.entries(screens)) s.root.classList.toggle("active", key === name);
  document.body.classList.toggle("on-desktop", name === "home");
  screens[name].onShow?.();
  armIdle();
}

/** Гость вышел или сеанс закончился: возвращаемся на заставку и забываем всё личное. */
function resetToIntro(): void {
  loggedIn = false;
  sessionActive = false;
  home.reset();
  show("intro");
}

function logout(): void {
  send({ type: "logout" });
  resetToIntro();
}

function armIdle(): void {
  window.clearTimeout(idleTimer);
  if (current === "login") idleTimer = window.setTimeout(() => show("intro"), IDLE_LOGIN_MS);
  else if (current === "home" && !sessionActive) idleTimer = window.setTimeout(() => logout(), IDLE_HOME_MS);
}
for (const ev of ["keydown", "mousemove", "mousedown"]) window.addEventListener(ev, armIdle, { passive: true });

onMessage((msg: FromHost) => {
  switch (msg.type) {
    case "config":
      intro.setPc(msg.pcName, msg.zone);
      home.setPc(msg.pcName, msg.zone);
      break;
    case "link":
      intro.setOffline(!msg.online, msg.reason);
      // Связь пропала, пока гость вводил логин или выбирал время: возвращаем на заставку.
      if (!msg.online && !sessionActive && (current === "login" || current === "home")) {
        if (loggedIn) logout();
        else show("intro");
      }
      break;
    case "mode":
      if (msg.mode === "session") {
        sessionActive = true;
        home.setSession(true);
        show("home");
      } else {
        sessionActive = false;
        home.setSession(false);
        if (!loggedIn) show("intro"); // заблокировано, а гость не входил: заставка
      }
      break;
    case "loginResult":
      if (msg.ok) {
        loggedIn = true;
        home.setLogin(msg.user ?? "", msg.balance ?? 0, msg.packages ?? []);
        show("home");
      } else {
        login.setError(msg.error ?? "Не удалось войти");
      }
      break;
    case "startResult":
      if (!msg.ok) home.startError(msg.error ?? "Не удалось начать сеанс");
      break;
    case "tick":
      home.tick(msg.remainingSec, msg.balance);
      break;
    case "sessionEnded":
      intro.setNotice(msg.reason);
      resetToIntro();
      break;
    case "balance":
      home.setBalance(msg.balance);
      break;
    case "notice":
      if (current === "home") home.notice(msg.text);
      else intro.setNotice(msg.text);
      break;
    case "rebooting":
      rebootText.textContent = msg.test ? "Тестовый режим: перезагрузка и очистка пропущены" : "Перезагрузка… Файлы гостя удаляются";
      reboot.classList.add("show");
      if (msg.test) window.setTimeout(() => reboot.classList.remove("show"), 2500);
      break;
    case "catalog":
      home.setCatalog(msg.items);
      break;
    case "launchResult":
      home.launchResult(msg.id, msg.ok, msg.error);
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
