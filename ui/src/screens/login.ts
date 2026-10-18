import { el } from "../dom";

export interface LoginScreen {
  root: HTMLElement;
  setError(text: string): void;
  onShow(): void;
}

export interface LoginHandlers {
  onLogin(user: string, pass: string): void;
  onRegister(user: string, pass: string, birth: string): void;
  onBack(): void;
}

// Вход и регистрация гостя на одном экране. Новый гость регистрируется прямо за клубным ПК.
export function buildLogin(h: LoginHandlers): LoginScreen {
  let mode: "login" | "register" = "login";

  const title = el("h2", { class: "brand-gradient", text: "Вход" });
  const sub = el("div", { class: "sub" });
  const user = el("input", { attrs: { type: "text", autocomplete: "off", placeholder: "Логин" } });
  const pass = el("input", { attrs: { type: "password", autocomplete: "off", placeholder: "Пароль" } });
  const pass2 = el("input", { attrs: { type: "password", autocomplete: "off", placeholder: "Повторите пароль" } });
  const birth = el("input", { attrs: { type: "date", placeholder: "Дата рождения" } });
  const agree = el("input", { attrs: { type: "checkbox" } });
  const error = el("div", { class: "error" });
  const submit = el("button", { class: "btn" });
  const switchBtn = el("button", { class: "btn ghost" });
  const back = el("button", { class: "btn ghost", text: "Назад" });

  const pass2Label = el("label", { text: "Повторите пароль" });
  const birthLabel = el("label", { text: "Дата рождения" });
  const agreeRow = el("label", { class: "agree" }, [agree, el("span", { text: "Согласен с правилами клуба и обработкой данных" })]);
  const registerOnly = [pass2Label, pass2, birthLabel, birth, agreeRow];

  const root = el("div", { class: "screen" }, [
    el("div", { class: "card" }, [title, sub, el("label", { text: "Логин" }), user, el("label", { text: "Пароль" }), pass, ...registerOnly, error, submit, switchBtn, back]),
  ]);

  function setMode(next: "login" | "register"): void {
    mode = next;
    const reg = mode === "register";
    title.textContent = reg ? "Регистрация" : "Вход";
    sub.textContent = reg ? "Создайте аккаунт гостя клуба" : "Введите данные гостя клуба";
    submit.textContent = reg ? "Зарегистрироваться" : "Войти";
    switchBtn.textContent = reg ? "Уже есть аккаунт? Войти" : "Нет аккаунта? Зарегистрироваться";
    for (const n of registerOnly) n.hidden = !reg;
    pass.placeholder = reg ? "Пароль (от 6 символов)" : "Пароль";
    error.textContent = "";
    submit.disabled = false;
  }

  const go = () => {
    const u = user.value.trim();
    if (mode === "login") {
      if (!u || !pass.value) return (error.textContent = "Заполните оба поля");
      error.textContent = "";
      submit.disabled = true;
      return h.onLogin(u, pass.value);
    }
    if (u.length < 3) return (error.textContent = "Логин — не короче 3 символов");
    if (pass.value.length < 6) return (error.textContent = "Пароль — не короче 6 символов");
    if (pass.value !== pass2.value) return (error.textContent = "Пароли не совпадают");
    if (!birth.value) return (error.textContent = "Укажите дату рождения");
    if (!agree.checked) return (error.textContent = "Нужно согласие с правилами клуба");
    error.textContent = "";
    submit.disabled = true;
    h.onRegister(u, pass.value, birth.value);
  };

  submit.addEventListener("click", go);
  switchBtn.addEventListener("click", () => setMode(mode === "login" ? "register" : "login"));
  back.addEventListener("click", h.onBack);
  root.addEventListener("keydown", (e) => {
    if (e.key === "Enter") go();
    if (e.key === "Escape") h.onBack();
  });

  return {
    root,
    setError: (t) => {
      error.textContent = t;
      submit.disabled = false;
      pass.value = "";
      pass2.value = "";
      pass.focus();
    },
    onShow: () => {
      user.value = pass.value = pass2.value = birth.value = "";
      agree.checked = false;
      setMode("login");
      setTimeout(() => user.focus(), 50);
    },
  };
}
