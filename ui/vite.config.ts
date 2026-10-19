import { defineConfig } from "vite";
import { resolve } from "node:path";

// base "./" — интерфейс открывается из WebView2 по виртуальному хосту.
// Две страницы: index.html (окно блокировки и рабочий стол) и taskbar.html (панель задач — отдельное окно Windows).
export default defineConfig({
  base: "./",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    rollupOptions: {
      input: {
        main: resolve(__dirname, "index.html"),
        taskbar: resolve(__dirname, "taskbar.html"),
        taskmgr: resolve(__dirname, "taskmgr.html"),
        admin: resolve(__dirname, "admin.html"),
        switcher: resolve(__dirname, "switcher.html"),
        explorer: resolve(__dirname, "explorer.html"),
      },
    },
  },
  // В разработке админка с ?live ходит к настоящему серверу через прокси (тот же адрес для страницы и API).
  server: {
    port: 5173,
    proxy: {
      "/api/admin": { target: "https://cryvx.ru", changeOrigin: true },
      "/covers": { target: "https://cryvx.ru", changeOrigin: true },
    },
  },
});
