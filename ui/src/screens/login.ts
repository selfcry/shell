import { el } from "../dom";

export interface LoginScreen {
  root: HTMLElement;
  setError(text: string): void;
  onShow(): void;
}

export function buildLogin(onSubmit: (user: string, pass: string) => void, onBack: () => void): LoginScreen {
  const user = el("input", { attrs: { type: "text", autocomplete: "off", placeholder: "Логин или телефон" } });
  const pass = el("input", { attrs: { type: "password", autocomplete: "off", placeholder: "Пароль" } });
  const error = el("div", { class: "error" });
  const submit = el("button", { class: "btn", text: "Войти" });
  const back = el("button", { class: "btn ghost", text: "Назад" });

  const root = el("div", { class: "screen" }, [
    el("div", { class: "card" }, [
      el("h2", { class: "brand-gradient", text: "Вход" }),
      el("div", { class: "sub", text: "Введите данные гостя клуба" }),
      el("label", { text: "Логин" }), user,
      el("label", { text: "Пароль" }), pass,
      error, submit, back,
    ]),
  ]);

  const go = () => {
    if (!user.value || !pass.value) return (error.textContent = "Заполните оба поля");
    error.textContent = "";
    submit.disabled = true;
    onSubmit(user.value.trim(), pass.value);
  };
  submit.addEventListener("click", go);
  back.addEventListener("click", onBack);
  root.addEventListener("keydown", (e) => {
    if (e.key === "Enter") go();
    if (e.key === "Escape") onBack();
  });

  return {
    root,
    setError: (t) => {
      error.textContent = t;
      submit.disabled = false;
      pass.value = "";
      pass.focus();
    },
    onShow: () => {
      user.value = "";
      pass.value = "";
      error.textContent = "";
      submit.disabled = false;
      setTimeout(() => user.focus(), 50);
    },
  };
}
