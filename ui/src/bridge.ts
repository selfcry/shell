import type { FromHost, ToHost } from "./protocol";
import { MockHost } from "./mock";

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
const role = location.pathname.includes("taskbar") ? "taskbar" : location.pathname.includes("taskmgr") ? "taskmgr" : location.pathname.includes("admin") ? "admin" : location.pathname.includes("explorer") ? "explorer" : "shell";
const mock = webview ? null : new MockHost(emit, role);

if (webview) {
  webview.addEventListener("message", (e) => emit(e.data));
}

export const isMock = mock !== null;

export function send(msg: ToHost): void {
  if (webview) webview.postMessage(JSON.stringify(msg));
  else mock!.handle(msg);
}

export function onMessage(l: Listener): void {
  listeners.push(l);
}
