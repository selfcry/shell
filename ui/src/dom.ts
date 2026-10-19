// Мини-помощник для создания элементов без фреймворка.
export function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  props: { class?: string; text?: string; html?: string; attrs?: Record<string, string> } = {},
  children: (Node | string)[] = [],
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (props.class) node.className = props.class;
  if (props.text !== undefined) node.textContent = props.text;
  if (props.html !== undefined) node.innerHTML = props.html;
  for (const [k, v] of Object.entries(props.attrs ?? {})) node.setAttribute(k, v);
  for (const c of children) node.append(c);
  return node;
}

export function formatTime(totalSec: number): string {
  const s = Math.max(0, Math.floor(totalSec));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  const mm = String(m).padStart(2, "0");
  const ss = String(sec).padStart(2, "0");
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

export function isNight(date = new Date()): boolean {
  const h = date.getHours();
  return h >= 22 || h < 8;
}

/** Окно тарифа from→to (может переходить через полночь). null — тариф круглосуточный. */
export function tariffWindow(p: { nightOnly?: boolean; fromHour?: number | null; toHour?: number | null }): [number, number] | null {
  if (p.fromHour != null && p.toHour != null) return [p.fromHour, p.toHour];
  return p.nightOnly ? [22, 8] : null;
}

export function inWindow(w: [number, number] | null, date = new Date()): boolean {
  if (!w) return true;
  const h = date.getHours();
  return w[0] < w[1] ? h >= w[0] && h < w[1] : h >= w[0] || h < w[1];
}

export const windowText = (w: [number, number]) => `${String(w[0]).padStart(2, "0")}:00 – ${String(w[1]).padStart(2, "0")}:00`;
