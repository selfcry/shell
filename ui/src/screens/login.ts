import { el } from "../dom";

export interface LoginScreen {
  root: HTMLElement;
  setError(text: string): void;
  /** Ответ на «отправить код»: при успехе экран переходит к вводу кода. */
  codeSent(ok: boolean, error: string | undefined, resendSec: number, hint?: string): void;
  onShow(): void;
}

export interface LoginHandlers {
  onLogin(user: string, pass: string): void;
  onRegisterStart(user: string, pass: string, birth: string, phone: string): void;
  onRegisterConfirm(phone: string, code: string): void;
  onBack(): void;
}

type Mode = "login" | "register" | "code";

// Вход и регистрация гостя. Регистрация в два шага: данные и телефон → код из SMS.
export function buildLogin(h: LoginHandlers): LoginScreen {
  let mode: Mode = "login";
  let resendTimer: number | undefined;

  const title = el("h2", { class: "brand-gradient", text: "Вход" });
  const sub = el("div", { class: "sub" });
  const user = el("input", { attrs: { type: "text", autocomplete: "off", placeholder: "Логин" } });
  const pass = el("input", { attrs: { type: "password", autocomplete: "off", placeholder: "Пароль" } });
  const pass2 = el("input", { attrs: { type: "password", autocomplete: "off", placeholder: "Повторите пароль" } });
  const phone = el("input", { attrs: { type: "tel", autocomplete: "off", placeholder: "+7 900 000-00-00" } });
  const birth = el("input", { attrs: { type: "date" } });
  const agree = el("input", { attrs: { type: "checkbox" } });
  const code = el("input", { class: "code", attrs: { type: "text", inputmode: "numeric", maxlength: "6", autocomplete: "off", placeholder: "••••••" } });
  const hint = el("div", { class: "hint" });
  const error = el("div", { class: "error" });
  const submit = el("button", { class: "btn" });
  const resend = el("button", { class: "btn ghost" });
  const switchBtn = el("button", { class: "btn ghost" });
  const back = el("button", { class: "btn ghost", text: "Назад" });

  const lbl = (t: string) => el("label", { text: t });
  const loginParts = [lbl("Логин"), user, lbl("Пароль"), pass];
  const regParts = [lbl("Повторите пароль"), pass2, lbl("Телефон"), phone, lbl("Дата рождения"), birth, el("label", { class: "agree" }, [agree, el("span", { text: "Согласен с правилами клуба и обработкой данных" })])];
  const codeParts = [lbl("Код из SMS"), code, hint];

  const root = el("div", { class: "screen" }, [el("div", { class: "card" }, [title, sub, ...loginParts, ...regParts, ...codeParts, error, submit, resend, switchBtn, back])]);

  function stopResendTimer(): void {
    window.clearInterval(resendTimer);
    resendTimer = undefined;
  }

  function startResendTimer(sec: number): void {
    stopResendTimer();
    let left = sec;
    const tick = () => {
      resend.disabled = left > 0;
      resend.textContent = left > 0 ? `Отправить код снова (${left} с)` : "Отправить код снова";
      left -= 1;
      if (left < -1) stopResendTimer();
    };
    tick();
    resendTimer = window.setInterval(tick, 1000);
  }

  function setMode(next: Mode): void {
    mode = next;
    const set = (nodes: HTMLElement[], visible: boolean) => nodes.forEach((n) => (n.hidden = !visible));
    set(loginParts, mode !== "code");
    set(regParts, mode === "register");
    set(codeParts, mode === "code");
    resend.hidden = mode !== "code";
    switchBtn.hidden = mode === "code";
    title.textContent = { login: "Вход", register: "Регистрация", code: "Подтверждение" }[mode];
    sub.textContent = { login: "Введите данные гостя клуба", register: "Создайте аккаунт гостя клуба", code: `Мы отправили код на ${phone.value.trim()}` }[mode];
    submit.textContent = { login: "Войти", register: "Получить код по SMS", code: "Подтвердить" }[mode];
    switchBtn.textContent = mode === "register" ? "Уже есть аккаунт? Войти" : "Нет аккаунта? Зарегистрироваться";
    pass.placeholder = mode === "register" ? "Пароль (от 6 символов)" : "Пароль";
    error.textContent = "";
    submit.disabled = false;
    if (mode !== "code") stopResendTimer();
  }

  const go = () => {
    const u = user.value.trim();
    error.textContent = "";
    if (mode === "login") {
      if (!u || !pass.value) return (error.textContent = "Заполните оба поля");
      submit.disabled = true;
      return h.onLogin(u, pass.value);
    }
    if (mode === "register") {
      if (u.length < 3) return (error.textContent = "Логин — не короче 3 символов");
      if (pass.value.length < 6) return (error.textContent = "Пароль — не короче 6 символов");
      if (pass.value !== pass2.value) return (error.textContent = "Пароли не совпадают");
      if (phone.value.replace(/\D/g, "").length < 10) return (error.textContent = "Введите номер телефона");
      if (!birth.value) return (error.textContent = "Укажите дату рождения");
      if (!agree.checked) return (error.textContent = "Нужно согласие с правилами клуба");
      submit.disabled = true;
      return h.onRegisterStart(u, pass.value, birth.value, phone.value.trim());
    }
    if (code.value.replace(/\D/g, "").length !== 6) return (error.textContent = "Введите 6 цифр из SMS");
    submit.disabled = true;
    h.onRegisterConfirm(phone.value.trim(), code.value.trim());
  };

  submit.addEventListener("click", go);
  switchBtn.addEventListener("click", () => setMode(mode === "login" ? "register" : "login"));
  resend.addEventListener("click", () => {
    resend.disabled = true;
    error.textContent = "";
    h.onRegisterStart(user.value.trim(), pass.value, birth.value, phone.value.trim()); // пароль остаётся в форме до подтверждения
  });
  back.addEventListener("click", () => (mode === "code" ? setMode("register") : h.onBack()));
  root.addEventListener("keydown", (e) => {
    if (e.key === "Enter") go();
    if (e.key === "Escape") (mode === "code" ? setMode("register") : h.onBack());
  });

  return {
    root,
    setError: (t) => {
      error.textContent = t;
      submit.disabled = false;
      if (mode === "code") {
        code.value = "";
        code.focus();
      } else {
        pass.value = "";
        pass2.value = "";
        pass.focus();
      }
    },
    codeSent(ok, err, resendSec, hintText) {
      submit.disabled = false;
      if (!ok) {
        error.textContent = err ?? "Не удалось отправить код";
        if (mode === "code") startResendTimer(Math.max(resendSec, 0));
        return;
      }
      if (mode !== "code") setMode("code");
      hint.textContent = hintText ?? "";
      code.value = "";
      startResendTimer(resendSec);
      setTimeout(() => code.focus(), 50);
    },
    onShow: () => {
      user.value = pass.value = pass2.value = phone.value = birth.value = code.value = "";
      agree.checked = false;
      hint.textContent = "";
      setMode("login");
      setTimeout(() => user.focus(), 50);
    },
  };
}
