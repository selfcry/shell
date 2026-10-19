import type { FromHost, ToHost } from "./protocol";
import { MockHost } from "./mock";
import { LiveAdminHost } from "./adminLive";

type Listener = (msg: FromHost) => void;

interface WebView2 {
  postMessage(message: string): void;
  addEventListener(type: "message", handler: (e: { data: FromHost }) => void): void;
}

const webview: WebView2 | undefined = (window as unknown as { chrome?: { webview?: WebView2 } }).chrome?.webview;

const listeners: Listener[] = [];
const emit = (msg: FromHost) => listeners.forEach((l) => l(msg));

// В WebView2 говорим с настоящим хостом, в браузере (npm run dev) — с макетом.
// Страница панели задач (taskbar.html) — отдельное окно со своим каналом, поэтому у неё свой макет.
const role = location.pathname.includes("taskbar") ? "taskbar" : location.pathname.includes("taskmgr") ? "taskmgr" : location.pathname.includes("admin") ? "admin" : location.pathname.includes("explorer") ? "explorer" : location.pathname.includes("switcher") ? "switcher" : "shell";
// Админ-панель, открытая с сервера клуба (или в разработке с ?live), работает с настоящим сервером, а не с макетом.
const liveAdmin = role === "admin" && (!/^(localhost|127\.0\.0\.1)$/.test(location.hostname) || location.search.includes("live"));
const mock = webview ? null : liveAdmin ? new LiveAdminHost(emit) : new MockHost(emit, role);

if (webview) {
  webview.addEventListener("message", (e) => emit(e.data));
}

export const isMock = mock !== null && !liveAdmin;
/** Админка открыта в обычном браузере и работает с настоящим сервером. */
export const isLiveAdmin = !webview && liveAdmin;

export function send(msg: ToHost): void {
  if (webview) webview.postMessage(JSON.stringify(msg));
  else mock!.handle(msg);
}

export function onMessage(l: Listener): void {
  listeners.push(l);
}
