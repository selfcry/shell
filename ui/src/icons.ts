// Простые линейные иконки (24×24), цвет берётся из currentColor.
const svg = (body: string) =>
  `<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${body}</svg>`;

export const icons = {
  gamepad: svg('<path d="M6 11h4M8 9v4"/><circle cx="15.5" cy="10.5" r=".6"/><circle cx="17.5" cy="12.5" r=".6"/><path d="M6.5 6h11a4.5 4.5 0 0 1 4.4 5.4l-.9 4.3a2.6 2.6 0 0 1-4.4 1.2L14.5 15h-5l-2.1 1.9A2.6 2.6 0 0 1 3 15.7l-.9-4.3A4.5 4.5 0 0 1 6.5 6Z"/>'),
  grid: svg('<rect x="4" y="4" width="6.5" height="6.5" rx="1.5"/><rect x="13.5" y="4" width="6.5" height="6.5" rx="1.5"/><rect x="4" y="13.5" width="6.5" height="6.5" rx="1.5"/><rect x="13.5" y="13.5" width="6.5" height="6.5" rx="1.5"/>'),
  search: svg('<circle cx="11" cy="11" r="6.5"/><path d="m20 20-3.8-3.8"/>'),
  volume: svg('<path d="M4 9.5v5h3.5L12 19V5L7.5 9.5H4Z"/><path d="M15.5 9a4 4 0 0 1 0 6M18 6.5a7.5 7.5 0 0 1 0 11"/>'),
  muted: svg('<path d="M4 9.5v5h3.5L12 19V5L7.5 9.5H4Z"/><path d="m16 9.5 5 5M21 9.5l-5 5"/>'),
  network: svg('<path d="M5 12.5a10 10 0 0 1 14 0M8.2 15.7a5.5 5.5 0 0 1 7.6 0"/><circle cx="12" cy="19" r="1"/><path d="M2 9.3a14.5 14.5 0 0 1 20 0"/>'),
  offline: svg('<path d="M5 12.5a10 10 0 0 1 4-2.5M8.2 15.7a5.5 5.5 0 0 1 3-1.4"/><circle cx="12" cy="19" r="1"/><path d="m3 3 18 18"/>'),
  power: svg('<path d="M12 3v8"/><path d="M7 6.2a8 8 0 1 0 10 0"/>'),
  user: svg('<circle cx="12" cy="8.5" r="3.5"/><path d="M5 20a7 7 0 0 1 14 0"/>'),
  lock: svg('<rect x="5" y="10.5" width="14" height="9.5" rx="2"/><path d="M8 10.5V8a4 4 0 0 1 8 0v2.5"/>'),
  clock: svg('<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/>'),
  folder: svg('<path d="M3.5 7.5A2 2 0 0 1 5.5 5.5h3.6l2 2h7.4a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2v-9Z"/>'),
  heart: svg('<path d="M12 20s-7-4.4-7-10a4 4 0 0 1 7-2.6A4 4 0 0 1 19 10c0 5.6-7 10-7 10Z"/>'),
  heartFill: '<svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"><path d="M12 20s-7-4.4-7-10a4 4 0 0 1 7-2.6A4 4 0 0 1 19 10c0 5.6-7 10-7 10Z"/></svg>',
  close: svg('<path d="m6 6 12 12M18 6 6 18"/>'),
  play: '<svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor"><path d="M8 5.5v13l11-6.5-11-6.5Z"/></svg>',
  club: svg('<path d="M12 3 4 7v5c0 4.5 3.4 8 8 9 4.6-1 8-4.5 8-9V7l-8-4Z"/>'),
};
