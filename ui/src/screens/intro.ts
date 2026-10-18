import { el } from "../dom";

export interface IntroScreen {
  root: HTMLElement;
  setPc(name: string, zone: string): void;
  setNotice(text: string): void;
  setOffline(offline: boolean, reason?: string): void;
  adminResult(ok: boolean): void;
}

// Заставка: ролик по кругу (public/media/intro.mp4, необязателен), «Нажмите ПРОБЕЛ» → форма входа.
export function buildIntro(onSpace: () => void, onAdminPin: (pin: string) => void): IntroScreen {
  const video = el("video", { attrs: { src: "./media/intro.mp4", autoplay: "", loop: "", muted: "", playsinline: "" } });
  video.muted = true;
  video.addEventListener("error", () => video.remove()); // нет файла — остаётся анимированный фон
  document.querySelector(".backdrop")?.prepend(video);

  const logoImg = el("img", { attrs: { src: "./media/logo.webp", alt: "" } });
  logoImg.addEventListener("error", () => (logoImg.hidden = true));

  const pcPill = el("span", { class: "pill", text: "…" });
  const notice = el("div", { class: "tagline", text: "" });
  const offlineBanner = el("div", { class: "offline-banner" });
  offlineBanner.hidden = true;
  let offline = false;

  const root = el("div", { class: "screen" }, [
    offlineBanner,
    el("div", { class: "logo" }, [
      logoImg,
      el("span", { class: "logo-word brand-gradient", text: "МИРАЖ" }),
      el("span", { class: "logo-pro", text: "PRO" }),
    ]),
    el("div", { class: "tagline", text: "Компьютерные клубы в Пятигорске" }),
    notice,
    el("div", { class: "hint", html: "Нажмите <kbd>ПРОБЕЛ</kbd>, чтобы начать" }),
    el("div", { class: "pc-info" }, [pcPill, el("span", { class: "pill", text: "24/7" })]),
  ]);

  let adminOpen = false;
  window.addEventListener("keydown", (e) => {
    if (!root.classList.contains("active")) return;
    if (e.code === "Space") {
      e.preventDefault();
      if (offline) return; // без связи с сервером войти нельзя
      notice.textContent = "";
      onSpace();
    } else if (e.ctrlKey && e.shiftKey && e.code === "F12" && !adminOpen) {
      adminOpen = true;
      const pin = window.prompt("Админ-PIN");
      adminOpen = false;
      if (pin) onAdminPin(pin);
    }
  });

  return {
    root,
    setPc: (name, zone) => (pcPill.textContent = `${name} · ${zone}`),
    setNotice: (t) => (notice.textContent = t),
    setOffline(isOffline, reason) {
      offline = isOffline;
      offlineBanner.hidden = !isOffline;
      offlineBanner.textContent = `${reason ?? "Нет связи с сервером"} — вход временно недоступен`;
      root.classList.toggle("is-offline", isOffline);
    },
    adminResult: (ok) => {
      if (!ok) notice.textContent = "Неверный PIN";
    },
  };
}
