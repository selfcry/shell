import type { FromHost, ToHost } from "./protocol";

// Админ-панель в обычном браузере: те же запросы, но настоящие, прямо к серверу клуба (тот же адрес, откуда открыта страница).
// Ключ хранится в этом браузере; так же, как в приложении администратора, страница видит только ответы.
const KEY = "clubshell.adminKey";
const read = (): string => { try { return localStorage.getItem(KEY) ?? ""; } catch { return ""; } };

export class LiveAdminHost {
  constructor(private emit: (m: FromHost) => void) {}

  handle(msg: ToHost): void {
    if (msg.type === "ready") {
      this.emit({ type: "settings", serverUrl: location.origin, hasKey: read() !== "" });
    } else if (msg.type === "saveSettings") {
      try { localStorage.setItem(KEY, msg.key); } catch { /* приватный режим */ }
      this.emit({ type: "settings", serverUrl: location.origin, hasKey: true });
    } else if (msg.type === "api") {
      void this.call(msg);
    }
  }

  private async call(msg: Extract<ToHost, { type: "api" }>): Promise<void> {
    const done = (status: number, body: unknown) => this.emit({ type: "apiResult", id: msg.id, status, body: typeof body === "string" ? body : JSON.stringify(body) });
    if (!msg.path.startsWith("/api/admin/") || msg.path.includes("..")) return done(400, { ok: false, error: "Недопустимый запрос" });
    try {
      const headers: Record<string, string> = { "X-Admin-Key": read() };
      let method: string = msg.method;
      let body: BodyInit | undefined;
      if (msg.method === "UPLOAD") {
        const file = await pickImage();
        if (!file) return done(200, { ok: false, error: "Файл не выбран" });
        if (file.size > 2_000_000) return done(200, { ok: false, error: "Файл больше 2 МБ" });
        method = "POST";
        headers["Content-Type"] = file.type || "image/jpeg";
        body = file;
      } else if (msg.body !== undefined && msg.body !== null) {
        headers["Content-Type"] = "application/json";
        body = JSON.stringify(msg.body);
      }
      const r = await fetch(msg.path, { method, headers, body });
      done(r.status, await r.text());
    } catch {
      done(0, { ok: false, error: "Сервер недоступен" });
    }
  }
}

function pickImage(): Promise<File | null> {
  return new Promise((resolve) => {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = "image/png,image/jpeg,image/webp";
    input.onchange = () => resolve(input.files?.[0] ?? null);
    input.oncancel = () => resolve(null);
    input.click();
  });
}
