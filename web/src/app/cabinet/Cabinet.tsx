"use client"

import { useCallback, useEffect, useMemo, useState } from "react"

// Личный кабинет гостя: тот же сервер клуба, что у ПК в зале (cryvx.ru/api/guest).
// Токен входа живёт в localStorage этого браузера; на сервере хранится только его хеш.

type Loyalty = { bonus: number; spent: number; level: string | null; cashbackPct: number; nextLevel: string | null; toNext: number }
type Me = { login: string; balance: number; bonus: number; phone: string | null; birth: string | null; loyalty: Loyalty }
type Tx = { id: number; amount: number; kind: string; note: string | null; ts: number }
type Booking = { id: number; pc: string; starts: number; ends: number; status: string; note: string | null }
type Tariff = { id: string; name: string; minutes: number; price: number; zone: string; fromHour: number | null; toHour: number | null }
type FreePc = { id: number; name: string; zone: string }
type Payment = { id: string; amount: number; status: string; created: number }

const TOKEN = "mirage.guestToken"
const readToken = () => { try { return localStorage.getItem(TOKEN) } catch { return null } }
const saveToken = (t: string | null) => { try { if (t) localStorage.setItem(TOKEN, t); else localStorage.removeItem(TOKEN) } catch { /* приватный режим */ } }

async function api<T>(method: string, path: string, body?: unknown): Promise<{ status: number; data: T }> {
  const headers: Record<string, string> = { "Content-Type": "application/json" }
  const t = readToken()
  if (t) headers["X-Guest-Token"] = t
  try {
    const r = await fetch(`/api/guest${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(15000) })
    const data = (await r.json().catch(() => ({}))) as T
    return { status: r.status, data }
  } catch {
    return { status: 0, data: { ok: false, error: "Нет связи с сервером клуба" } as T }
  }
}

const rub = (n: number) => `${n.toLocaleString("ru-RU")} ₽`
const when = (ts: number) => new Date(ts * 1000).toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" })
const KIND: Record<string, string> = {
  topup: "Пополнение", charge: "Списание", session: "Время", bonus_spent: "Оплачено бонусами", cashback: "Кешбэк",
  promo: "Промокод", bonus_admin: "Бонусы от клуба", shop: "Бар",
}
const BONUS_KINDS = new Set(["bonus_spent", "cashback", "promo", "bonus_admin"])

const panel = "liquid-glass-panel relative rounded-3xl p-6 sm:p-8"
const input = "w-full rounded-xl border border-white/10 bg-white/5 px-4 py-3 text-base text-white outline-none transition focus:border-[#ff6b00] placeholder:text-white/30"
const btn = "rounded-xl px-5 py-3 font-semibold transition disabled:opacity-40"
const primary = `${btn} gradient-bg text-black`
const ghost = `${btn} border border-white/10 text-white/80 hover:border-white/30`

export function Cabinet() {
  const [me, setMe] = useState<Me | null>(null)
  const [ready, setReady] = useState(false)

  const load = useCallback(async () => {
    if (!readToken()) { setMe(null); setReady(true); return }
    const r = await api<Me>("GET", "/me")
    if (r.status === 200) setMe(r.data)
    else { if (r.status === 401) saveToken(null); setMe(null) }
    setReady(true)
  }, [])

  useEffect(() => { void load() }, [load])

  if (!ready) return <div className="py-32 text-center text-white/40">Загрузка…</div>
  return me ? <Dashboard me={me} reload={load} onLogout={() => { void api("POST", "/logout"); saveToken(null); setMe(null) }} /> : <Auth onDone={load} />
}

// ---------------- вход и регистрация ----------------
function Auth({ onDone }: { onDone: () => void }) {
  const [mode, setMode] = useState<"login" | "register">("login")
  const [login, setLogin] = useState("")
  const [password, setPassword] = useState("")
  const [birth, setBirth] = useState("")
  const [phone, setPhone] = useState("")
  const [error, setError] = useState("")
  const [busy, setBusy] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError("")
    setBusy(true)
    const r = mode === "login"
      ? await api<{ ok: boolean; error?: string; token?: string }>("POST", "/login", { login, password })
      : await api<{ ok: boolean; error?: string; token?: string }>("POST", "/register", { login, password, birth, phone: phone || null })
    setBusy(false)
    if (!r.data.ok || !r.data.token) return setError(r.data.error ?? "Не получилось")
    saveToken(r.data.token)
    onDone()
  }

  return (
    <div className="mx-auto max-w-md">
      <div className={panel}>
        <div className="liquid-glass-shine" />
        <div className="relative z-10">
          <h1 className="mb-2 text-3xl font-bold"><span className="gradient-text">Личный кабинет</span></h1>
          <p className="mb-6 text-white/50">Тот же логин и пароль, что на компьютерах клуба.</p>
          <div className="mb-6 flex gap-1 rounded-2xl bg-white/5 p-1">
            {(["login", "register"] as const).map((m) => (
              <button key={m} type="button" onClick={() => { setMode(m); setError("") }}
                className={`flex-1 rounded-xl py-2.5 text-sm font-semibold transition ${mode === m ? "bg-white/10 text-white" : "text-white/50"}`}>
                {m === "login" ? "Вход" : "Регистрация"}
              </button>
            ))}
          </div>
          <form onSubmit={submit} className="flex flex-col gap-3">
            <input className={input} placeholder="Логин" autoComplete="username" value={login} onChange={(e) => setLogin(e.target.value)} />
            <input className={input} placeholder="Пароль" type="password" autoComplete={mode === "login" ? "current-password" : "new-password"} value={password} onChange={(e) => setPassword(e.target.value)} />
            {mode === "register" && (
              <>
                <label className="text-sm text-white/50">Дата рождения
                  <input className={`${input} mt-1`} type="date" value={birth} onChange={(e) => setBirth(e.target.value)} />
                </label>
                <input className={input} placeholder="Телефон (необязательно)" type="tel" autoComplete="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
                <p className="text-xs text-white/40">Логин: 3–24 символа (буквы, цифры, точка, дефис). Пароль — от 6 символов.</p>
              </>
            )}
            {error && <div className="rounded-xl bg-red-500/10 px-4 py-3 text-sm text-red-300">{error}</div>}
            <button className={`${primary} mt-2`} disabled={busy || !login || !password}>{busy ? "…" : mode === "login" ? "Войти" : "Создать аккаунт"}</button>
          </form>
        </div>
      </div>
    </div>
  )
}

// ---------------- кабинет ----------------
function Dashboard({ me, reload, onLogout }: { me: Me; reload: () => void; onLogout: () => void }) {
  const [tab, setTab] = useState<"main" | "book" | "history" | "settings">("main")
  const [toast, setToast] = useState<{ text: string; ok: boolean } | null>(null)
  const say = (text: string, ok = true) => { setToast({ text, ok }); window.setTimeout(() => setToast(null), 3500) }

  useEffect(() => {
    // вернулись со страницы оплаты: сервер сам проверит платёж в ЮKassa
    if (new URLSearchParams(location.search).get("paid") === "1") {
      void api<Payment[]>("GET", "/payments").then((r) => {
        const last = Array.isArray(r.data) ? r.data[0] : null
        if (last?.status === "succeeded") say(`Баланс пополнен на ${rub(last.amount)}`)
        else if (last?.status === "pending") say("Платёж обрабатывается, баланс обновится через минуту")
        reload()
        history.replaceState(null, "", "/cabinet")
      })
    }
  }, [reload])

  const tabs = [["main", "Баланс"], ["book", "Бронь ПК"], ["history", "История"], ["settings", "Профиль"]] as const
  return (
    <div className="mx-auto max-w-4xl">
      <div className="mb-6 flex flex-wrap items-center justify-between gap-4">
        <div>
          <div className="text-sm text-white/40">Личный кабинет</div>
          <h1 className="text-3xl font-bold"><span className="gradient-text">{me.login}</span></h1>
        </div>
        <div className="flex w-full gap-1 overflow-x-auto rounded-2xl bg-white/5 p-1 sm:w-auto">
          {tabs.map(([id, title]) => (
            <button key={id} onClick={() => setTab(id)} className={`flex-1 whitespace-nowrap rounded-xl px-3 py-2 text-sm font-semibold transition sm:flex-none sm:px-4 ${tab === id ? "bg-white/10 text-white" : "text-white/50 hover:text-white/80"}`}>{title}</button>
          ))}
        </div>
      </div>
      {tab === "main" && <Main me={me} reload={reload} say={say} />}
      {tab === "book" && <Book say={say} />}
      {tab === "history" && <History />}
      {tab === "settings" && <Settings me={me} say={say} onLogout={onLogout} />}
      {toast && (
        <div className={`fixed bottom-6 left-1/2 z-50 -translate-x-1/2 rounded-2xl border px-5 py-3 text-sm shadow-2xl backdrop-blur ${toast.ok ? "border-[#ff6b00]/50 bg-black/80" : "border-red-500/50 bg-black/80 text-red-300"}`}>{toast.text}</div>
      )}
    </div>
  )
}

function Main({ me, reload, say }: { me: Me; reload: () => void; say: (t: string, ok?: boolean) => void }) {
  const [amount, setAmount] = useState(500)
  const [online, setOnline] = useState<boolean | null>(null)
  const [code, setCode] = useState("")
  const [busy, setBusy] = useState(false)
  const l = me.loyalty
  const progress = l.nextLevel ? Math.min(100, Math.round((l.spent / (l.spent + l.toNext)) * 100)) : 100

  useEffect(() => { void api<{ available: boolean }>("GET", "/pay/available").then((r) => setOnline(!!r.data.available)) }, [])

  const pay = async () => {
    setBusy(true)
    const r = await api<{ ok: boolean; url?: string; error?: string }>("POST", "/pay", { amount })
    setBusy(false)
    if (r.data.ok && r.data.url) location.href = r.data.url
    else say(r.data.error ?? "Не получилось", false)
  }
  const promo = async () => {
    if (!code.trim()) return
    const r = await api<{ ok: boolean; error?: string; bonus?: number }>("POST", "/promo", { code: code.trim() })
    if (r.data.ok) { setCode(""); say("Промокод принят: бонусы начислены"); reload() } else say(r.data.error ?? "Промокод не принят", false)
  }

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <section className={panel}>
        <div className="liquid-glass-shine" />
        <div className="relative z-10">
          <div className="text-sm uppercase tracking-wider text-white/40">Баланс</div>
          <div className="mt-1 text-5xl font-bold">{rub(me.balance)}</div>
          <div className="mt-2 text-lg font-semibold text-[#f5ff00]">{me.bonus > 0 ? `+ ${me.bonus} бонусов` : "Бонусов пока нет"}</div>
          <p className="mt-2 text-sm text-white/40">Бонусы тратятся на время первыми: 1 бонус = 1 ₽.</p>
        </div>
      </section>

      <section className={panel}>
        <div className="liquid-glass-shine" />
        <div className="relative z-10">
          <div className="text-sm uppercase tracking-wider text-white/40">Уровень</div>
          <div className="mt-1 text-3xl font-bold"><span className="gradient-text">{l.level ?? "—"}</span></div>
          <div className="mt-1 text-white/60">Кешбэк бонусами {l.cashbackPct}% с каждой покупки времени</div>
          <div className="mt-4 h-2 overflow-hidden rounded-full bg-white/10"><div className="h-full gradient-bg" style={{ width: `${progress}%` }} /></div>
          <div className="mt-2 text-sm text-white/40">{l.nextLevel ? `До «${l.nextLevel}» осталось потратить ${rub(l.toNext)}` : "Максимальный уровень"}</div>
        </div>
      </section>

      <section className={panel}>
        <div className="liquid-glass-shine" />
        <div className="relative z-10">
          <h2 className="mb-3 text-xl font-bold">Пополнить баланс</h2>
          {online === false ? (
            <p className="text-white/60">Онлайн-пополнение скоро появится. Сейчас баланс пополняется на стойке администратора: наличными, картой или по СБП — назовите свой логин <b className="text-white">{me.login}</b>.</p>
          ) : (
            <>
              <div className="mb-3 flex flex-wrap gap-2">
                {[300, 500, 1000, 2000].map((v) => (
                  <button key={v} onClick={() => setAmount(v)} className={`rounded-xl border px-4 py-2 text-sm font-semibold ${amount === v ? "border-[#ff6b00] text-white" : "border-white/10 text-white/60"}`}>{rub(v)}</button>
                ))}
              </div>
              <input className={input} type="number" min={100} max={15000} value={amount} onChange={(e) => setAmount(Number(e.target.value) || 0)} />
              <button className={`${primary} mt-3 w-full`} disabled={busy || amount < 100 || online === null} onClick={pay}>Оплатить {rub(amount)}</button>
              <p className="mt-2 text-xs text-white/40">Оплата картой или СБП через ЮKassa. Деньги зачисляются сразу после оплаты.</p>
            </>
          )}
        </div>
      </section>

      <section className={panel}>
        <div className="liquid-glass-shine" />
        <div className="relative z-10">
          <h2 className="mb-3 text-xl font-bold">Промокод</h2>
          <div className="flex gap-2">
            <input className={`${input} uppercase`} placeholder="Например, WELCOME" value={code} onChange={(e) => setCode(e.target.value)} onKeyDown={(e) => e.key === "Enter" && void promo()} />
            <button className={ghost} onClick={promo}>OK</button>
          </div>
          <p className="mt-2 text-xs text-white/40">Промокод даёт бонусы. Каждый код — один раз на гостя.</p>
        </div>
      </section>
    </div>
  )
}

function Book({ say }: { say: (t: string, ok?: boolean) => void }) {
  const [zones, setZones] = useState<string[]>([])
  const [tariffs, setTariffs] = useState<Tariff[]>([])
  const [zone, setZone] = useState("")
  const tomorrowish = useMemo(() => { const d = new Date(Date.now() + 2 * 3600 * 1000); d.setMinutes(0, 0, 0); return d }, [])
  const pad = (n: number) => String(n).padStart(2, "0")
  const [date, setDate] = useState(`${tomorrowish.getFullYear()}-${pad(tomorrowish.getMonth() + 1)}-${pad(tomorrowish.getDate())}`)
  const [time, setTime] = useState(`${pad(tomorrowish.getHours())}:00`)
  const [hours, setHours] = useState(2)
  const [free, setFree] = useState<FreePc[] | null>(null)
  const [mine, setMine] = useState<Booking[]>([])

  const starts = Math.floor(new Date(`${date}T${time}`).getTime() / 1000)
  const loadMine = useCallback(async () => { const r = await api<Booking[]>("GET", "/bookings"); setMine(Array.isArray(r.data) ? r.data : []) }, [])

  useEffect(() => {
    void api<{ zones: string[]; tariffs: Tariff[] }>("GET", "/club").then((r) => {
      setZones(r.data.zones ?? [])
      setTariffs(r.data.tariffs ?? [])
      setZone((r.data.zones ?? [])[0] ?? "")
    })
    void loadMine()
  }, [loadMine])

  useEffect(() => {
    setFree(null)
    if (!zone || !starts) return
    const t = window.setTimeout(() => void api<FreePc[]>("GET", `/free?zone=${encodeURIComponent(zone)}&starts=${starts}&minutes=${hours * 60}`).then((r) => setFree(Array.isArray(r.data) ? r.data : [])), 250)
    return () => window.clearTimeout(t)
  }, [zone, starts, hours])

  const book = async (pc: FreePc) => {
    const r = await api<{ ok: boolean; error?: string }>("POST", "/bookings", { pcId: pc.id, starts, minutes: hours * 60 })
    if (r.data.ok) { say(`Забронирован ${pc.name}: ${when(starts)}`); void loadMine(); setFree(null) } else say(r.data.error ?? "Не получилось", false)
  }
  const cancel = async (b: Booking) => {
    const r = await api<{ ok: boolean }>("DELETE", `/bookings/${b.id}`)
    if (r.data.ok) { say("Бронь отменена"); void loadMine() } else say("Не удалось отменить", false)
  }
  const active = mine.filter((b) => b.status === "active" && b.ends * 1000 > Date.now())
  const zoneTariffs = tariffs.filter((t) => t.zone === zone)

  return (
    <div className="grid gap-4">
      {active.length > 0 && (
        <section className={panel}>
          <div className="liquid-glass-shine" />
          <div className="relative z-10">
            <h2 className="mb-3 text-xl font-bold">Ваша бронь</h2>
            {active.map((b) => (
              <div key={b.id} className="flex flex-wrap items-center justify-between gap-3 rounded-2xl bg-white/5 px-4 py-3">
                <div><b>{b.pc}</b> · {when(b.starts)} — {new Date(b.ends * 1000).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })}</div>
                <button className={ghost} onClick={() => void cancel(b)}>Отменить</button>
              </div>
            ))}
            <p className="mt-3 text-sm text-white/40">За 15 минут до начала компьютер держится за вами. Если опоздаете больше чем на 30 минут — бронь снимется. Оплата времени — как обычно, на компьютере после входа.</p>
          </div>
        </section>
      )}
      <section className={panel}>
        <div className="liquid-glass-shine" />
        <div className="relative z-10">
          <h2 className="mb-4 text-xl font-bold">Забронировать компьютер</h2>
          <div className="grid gap-3 sm:grid-cols-4">
            <label className="text-sm text-white/50">Зона
              <select className={`${input} mt-1`} value={zone} onChange={(e) => setZone(e.target.value)}>{zones.map((z) => <option key={z} value={z} className="bg-black">{z}</option>)}</select>
            </label>
            <label className="text-sm text-white/50">Дата<input className={`${input} mt-1`} type="date" value={date} onChange={(e) => setDate(e.target.value)} /></label>
            <label className="text-sm text-white/50">Время<input className={`${input} mt-1`} type="time" step={1800} value={time} onChange={(e) => setTime(e.target.value)} /></label>
            <label className="text-sm text-white/50">Часов
              <select className={`${input} mt-1`} value={hours} onChange={(e) => setHours(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 8, 10, 12].map((h) => <option key={h} value={h} className="bg-black">{h}</option>)}</select>
            </label>
          </div>
          {zoneTariffs.length > 0 && (
            <p className="mt-3 text-sm text-white/40">Тарифы зоны: {zoneTariffs.map((t) => `${t.name} — ${rub(t.price)}${t.fromHour != null ? ` (${pad(t.fromHour)}:00–${pad(t.toHour ?? 0)}:00)` : ""}`).join(" · ")}</p>
          )}
          <div className="mt-5">
            {free === null ? <div className="text-white/40">Ищем свободные компьютеры…</div>
              : free.length === 0 ? <div className="text-white/60">На это время в зоне {zone} свободных компьютеров нет — выберите другое время.</div>
              : (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                  {free.map((pc) => (
                    <button key={pc.id} onClick={() => void book(pc)} disabled={active.length > 0}
                      className="rounded-2xl border border-white/10 bg-white/5 px-4 py-4 text-left transition hover:border-[#ff6b00] disabled:opacity-40">
                      <div className="text-lg font-bold">{pc.name}</div>
                      <div className="text-sm text-white/40">свободен</div>
                    </button>
                  ))}
                </div>
              )}
            {active.length > 0 && free && free.length > 0 && <p className="mt-3 text-sm text-white/40">Одна бронь за раз: отмените текущую, чтобы выбрать другую.</p>}
          </div>
        </div>
      </section>
    </div>
  )
}

function History() {
  const [tx, setTx] = useState<Tx[] | null>(null)
  useEffect(() => { void api<Tx[]>("GET", "/history").then((r) => setTx(Array.isArray(r.data) ? r.data : [])) }, [])
  return (
    <section className={panel}>
      <div className="liquid-glass-shine" />
      <div className="relative z-10">
        <h2 className="mb-4 text-xl font-bold">История операций</h2>
        {tx === null ? <div className="text-white/40">Загрузка…</div> : tx.length === 0 ? <div className="text-white/50">Операций пока нет</div> : (
          <div className="flex flex-col divide-y divide-white/5">
            {tx.map((t) => (
              <div key={t.id} className="flex items-center justify-between gap-4 py-3">
                <div>
                  <div className="font-semibold">{KIND[t.kind] ?? t.kind}</div>
                  <div className="text-sm text-white/40">{when(t.ts)}{t.note ? ` · ${t.note}` : ""}</div>
                </div>
                <div className={`whitespace-nowrap font-bold ${t.amount > 0 ? "text-[#5be38b]" : "text-white/70"}`}>
                  {t.amount > 0 ? "+" : ""}{BONUS_KINDS.has(t.kind) ? `${t.amount} б.` : rub(t.amount)}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </section>
  )
}

function Settings({ me, say, onLogout }: { me: Me; say: (t: string, ok?: boolean) => void; onLogout: () => void }) {
  const [oldPass, setOld] = useState("")
  const [newPass, setNew] = useState("")
  const change = async () => {
    const r = await api<{ ok: boolean; error?: string }>("POST", "/password", { oldPassword: oldPass, newPassword: newPass })
    if (r.data.ok) { say("Пароль изменён. Войдите заново"); onLogout() } else say(r.data.error ?? "Не получилось", false)
  }
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <section className={panel}>
        <div className="liquid-glass-shine" />
        <div className="relative z-10">
          <h2 className="mb-3 text-xl font-bold">Профиль</h2>
          <div className="flex flex-col gap-1 text-white/70">
            <div>Логин: <b className="text-white">{me.login}</b></div>
            <div>Телефон: {me.phone ?? "не указан"}</div>
            <div>Дата рождения: {me.birth ? new Date(me.birth).toLocaleDateString("ru-RU") : "не указана"}</div>
          </div>
          <button className={`${ghost} mt-5`} onClick={onLogout}>Выйти</button>
        </div>
      </section>
      <section className={panel}>
        <div className="liquid-glass-shine" />
        <div className="relative z-10">
          <h2 className="mb-3 text-xl font-bold">Сменить пароль</h2>
          <div className="flex flex-col gap-3">
            <input className={input} type="password" placeholder="Текущий пароль" autoComplete="current-password" value={oldPass} onChange={(e) => setOld(e.target.value)} />
            <input className={input} type="password" placeholder="Новый пароль (от 6 символов)" autoComplete="new-password" value={newPass} onChange={(e) => setNew(e.target.value)} />
            <button className={primary} disabled={!oldPass || newPass.length < 6} onClick={change}>Сменить</button>
          </div>
          <p className="mt-2 text-xs text-white/40">Пароль один и для кабинета, и для компьютеров клуба.</p>
        </div>
      </section>
    </div>
  )
}
