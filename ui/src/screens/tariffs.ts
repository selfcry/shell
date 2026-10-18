import { el, isNight } from "../dom";
import type { TariffPackage } from "../protocol";

export interface TariffsScreen {
  root: HTMLElement;
  setData(user: string, balance: number, packages: TariffPackage[]): void;
  setError(text: string): void;
}

export function buildTariffs(onPick: (packageId: string) => void, onCancel: () => void): TariffsScreen {
  const title = el("h2", { class: "brand-gradient", text: "Выберите тариф" });
  const bal = el("b", { text: "0 ₽" });
  const grid = el("div", { class: "packages" });
  const error = el("div", { class: "error" });
  const cancel = el("button", { class: "btn ghost", text: "Выйти" });
  cancel.addEventListener("click", onCancel);

  const root = el("div", { class: "screen" }, [
    el("div", { class: "card wide" }, [
      title,
      el("div", { class: "balance" }, [el("span", { class: "sub", text: "Баланс" }), bal]),
      grid, error, cancel,
    ]),
  ]);

  return {
    root,
    setData(user, balance, packages) {
      title.textContent = user ? `Привет, ${user}` : "Выберите тариф";
      bal.textContent = `${balance} ₽`;
      error.textContent = "";
      grid.replaceChildren(
        ...packages.map((p) => {
          const locked = (p.nightOnly && !isNight()) || p.price > balance;
          const note = p.nightOnly ? "22:00 – 08:00" : p.price > balance ? "не хватает средств" : "";
          const btn = el("button", { class: "pkg", attrs: locked ? { disabled: "" } : {} }, [
            el("div", { class: "name", text: p.name }),
            el("div", { class: "price brand-gradient", text: `${p.price} ₽` }),
            el("div", { class: "note", text: note }),
          ]);
          btn.addEventListener("click", () => onPick(p.id));
          return btn;
        }),
      );
    },
    setError: (t) => (error.textContent = t),
  };
}
