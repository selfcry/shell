using System.Net.WebSockets;
using ClubShell.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://127.0.0.1:5080"); // наружу сервер выставляет nginx; слушать все адреса — только явно через ASPNETCORE_URLS

var dataDir = builder.Configuration["Club:DataDir"] ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);

var db = new Db(Path.Combine(dataDir, "club.db"));
db.Init();

TimeZoneInfo tz;
try { tz = TimeZoneInfo.FindSystemTimeZoneById(builder.Configuration["Club:TimeZone"] ?? "Europe/Moscow"); }
catch (TimeZoneNotFoundException)
{
    try { tz = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time"); }
    catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.CreateCustomTimeZone("MSK", TimeSpan.FromHours(3), "MSK", "MSK"); }
}

// Ключ администратора: создаётся при первом запуске и лежит в data/admin-key.txt. Им подписываются запросы админ-панели.
var keyFile = Path.Combine(dataDir, "admin-key.txt");
if (!File.Exists(keyFile))
{
    File.WriteAllText(keyFile, Security.NewToken());
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); // только владелец
    Console.WriteLine($"Ключ администратора создан: {keyFile}");
}
var adminKey = File.ReadAllText(keyFile).Trim();

var club = new ClubService(db, tz);
club.SeedDefaults(builder.Configuration.GetValue("Club:SeedDemo", false)); // демо-гости только по явному Club__SeedDemo=true

var operators = new Operators(db);
var catalog = new CatalogService(db, dataDir);
builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton(club);
builder.Services.AddSingleton(tz);
builder.Services.AddSingleton<PcHub>();
builder.Services.AddSingleton<Telegram>();
builder.Services.AddSingleton<YooKassa>();
builder.Services.AddSingleton<UpdateFeed>();
builder.Services.AddHostedService<ExpiryWorker>();
builder.Services.AddHostedService<MaintenanceWorker>();
builder.Services.AddResponseCompression();

var app = builder.Build();
app.UseResponseCompression();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

Pc? AuthPc(HttpContext ctx)
{
    // только заголовок: токен в адресе попал бы в журналы nginx
    var token = ctx.Request.Headers["X-Pc-Token"].FirstOrDefault();
    return string.IsNullOrEmpty(token) ? null : club.PcByToken(token);
}

// Владелец — ключ из admin-key.txt; сотрудник — токен, выданный при входе по логину и паролю.
Actor? ActorOf(HttpContext ctx)
{
    var key = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault();
    if (string.IsNullOrEmpty(key)) return null;
    if (Security.FixedEquals(key, adminKey)) return new Actor("Владелец", "owner", null);
    return operators.ByToken(key);
}
string ClientIp(HttpContext ctx) => ctx.Request.Headers["X-Real-IP"].FirstOrDefault() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "?";

IResult Unauthorized(string what) => Results.Json(new { ok = false, error = what }, statusCode: 401);
object Pkg(Tariff t) => new { id = t.Id, name = t.Name, minutes = t.Minutes, price = t.Price, nightOnly = t.NightOnly, fromHour = t.Window?.from, toHour = t.Window?.to };

var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0";
app.MapGet("/health", () =>
{
    try { using var c = db.Open(); Db.Scalar<long>(c, "SELECT 1"); }
    catch (Exception) { return Results.Json(new { ok = false, error = "База недоступна" }, statusCode: 503); }
    return Results.Ok(new { ok = true, time = Db.Now() });
});

void Audit(HttpContext ctx, string action, string? details = null)
{
    try
    {
        using var c = db.Open();
        var who = ctx.Items["actor"] is Actor a ? a.Name : null;
        Db.Exec(c, "INSERT INTO audit(ts, ip, action, details) VALUES($t, $ip, $a, $d)",
            ("$t", Db.Now()), ("$ip", ClientIp(ctx)), ("$a", who is null ? action : $"{who}: {action}"), ("$d", details));
    }
    catch (Exception) { /* журнал не должен ломать операцию */ }
}

// ======================= ПК =======================
var pc = app.MapGroup("/api/pc");

// Каталог для оболочки: игры/приложения с путями запуска и список запрещённых программ. version меняется при любой правке.
pc.MapGet("/catalog", (HttpContext ctx) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    return Results.Ok(new
    {
        version = catalog.Version,
        items = catalog.Apps(onlyEnabled: true).Select(a => new
        {
            id = a.Id, title = a.Title, kind = a.Kind, group = a.Group, cover = a.Cover is null ? null : "/covers/" + a.Cover,
            paths = a.Paths, args = a.Args, workDir = a.WorkDir, clubAccount = a.ClubAccount, minAge = a.MinAge, pinned = a.Pinned,
        }),
        blocked = catalog.Blocked().Select(b => b.Process),
    });
});

pc.MapPost("/login", (HttpContext ctx, LoginReq req) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    club.Touch(p.Id);
    var (ok, error, guest, tariffs) = club.Login(p, req.Username, req.Password);
    return ok
        ? Results.Ok(new { ok = true, user = guest!.Login, balance = guest.Balance, bonus = club.BonusOf(guest.Id), loyalty = club.Loyalty(guest.Id), packages = tariffs!.Select(Pkg) })
        : Results.Ok(new { ok = false, error });
});

pc.MapPost("/register", (HttpContext ctx, RegisterReq req) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    club.Touch(p.Id);
    var (ok, error, guest, tariffs) = club.Register(p, req.Username, req.Password, req.Birth);
    return ok
        ? Results.Ok(new { ok = true, user = guest!.Login, balance = guest.Balance, packages = tariffs!.Select(Pkg) })
        : Results.Ok(new { ok = false, error });
});

// Тест скорости до сервера клуба: скачивание (до 25 МБ) и отдача (до 4 МБ). Не чаще раза в 10 секунд с одного ПК.
var speedLast = new System.Collections.Concurrent.ConcurrentDictionary<long, long>();
var speedBlock = new byte[64 * 1024];
Random.Shared.NextBytes(speedBlock); // случайные данные: не сжимаются по дороге и не завышают скорость

var speedSlots = new SemaphoreSlim(3); // одновременно не больше 3 замеров на весь клуб: канал VDS общий

bool SpeedAllowed(long pcId, string kind)
{
    var key = pcId * 2 + (kind == "down" ? 0 : 1);
    var now = Db.Now();
    while (true)
    {
        var last = speedLast.GetOrAdd(key, 0);
        if (now - last < 10) return false;
        if (speedLast.TryUpdate(key, now, last)) return true; // атомарно: из параллельных запросов пройдёт один
    }
}

pc.MapGet("/speedtest/down", async (HttpContext ctx, int? bytes) =>
{
    var p = AuthPc(ctx); if (p is null) { ctx.Response.StatusCode = 401; return; }
    if (!SpeedAllowed(p.Id, "down")) { ctx.Response.StatusCode = 429; return; }
    if (!await speedSlots.WaitAsync(0)) { ctx.Response.StatusCode = 429; return; }
    try {
    var total = Math.Clamp(bytes ?? 10_000_000, 1, 25_000_000);
    ctx.Response.ContentType = "application/octet-stream";
    ctx.Response.ContentLength = total;
    var left = total;
    while (left > 0 && !ctx.RequestAborted.IsCancellationRequested)
    {
        var n = Math.Min(left, speedBlock.Length);
        await ctx.Response.Body.WriteAsync(speedBlock.AsMemory(0, n), ctx.RequestAborted);
        left -= n;
    }
    } finally { speedSlots.Release(); }
});

pc.MapPost("/speedtest/up", async (HttpContext ctx) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    if (!SpeedAllowed(p.Id, "up")) return Results.StatusCode(429);
    if (!await speedSlots.WaitAsync(0)) return Results.StatusCode(429);
    try
    {
        var limit = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 5_000_000;
        var buf = new byte[64 * 1024];
        long total = 0;
        int r;
        while ((r = await ctx.Request.Body.ReadAsync(buf, ctx.RequestAborted)) > 0) total += r;
        return Results.Ok(new { ok = true, bytes = total });
    }
    finally { speedSlots.Release(); }
});

pc.MapPost("/session/start", (HttpContext ctx, StartReq req) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    var (ok, error, balance, minutes, bonus) = club.StartSessionEx(p, req.Username, req.PackageId);
    return Results.Ok(new { ok, error, balance, minutes, bonus });
});

pc.MapPost("/session/stop", (HttpContext ctx, StopReq req) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    var s = club.EndSession(p.Id, "Гость завершил сеанс");
    club.Logout(p.Id); // после завершения купить время снова можно только после нового входа
    return Results.Ok(new { ok = true, ended = s is not null });
});

// «Позвать администратора»: в админке звук и подсветка ПК, в Telegram — сообщение.
pc.MapPost("/call", (HttpContext ctx, CallReq req, Telegram tg) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    if (!club.CallAdmin(p, req.Username)) return Results.Ok(new { ok = true, repeated = true });
    tg.Notify($"🔔 {p.Name}: гость зовёт администратора{(string.IsNullOrWhiteSpace(req.Username) ? "" : $" ({req.Username})")}");
    return Results.Ok(new { ok = true });
});

pc.MapPost("/promo", (HttpContext ctx, PromoReq req) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    var (r, bonus) = club.RedeemPromo(p, req.Username, req.Code);
    return Results.Ok(new { ok = r.Ok, error = r.Error, bonus });
});

// Бар: меню и заказ с ПК (оплата с баланса гостя, выдаёт администратор).
pc.MapGet("/shop", (HttpContext ctx) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    return Results.Ok(club.Products(onlyActive: true).Where(x => x.Stock > 0).Select(x => new { id = x.Id, name = x.Name, category = x.Category, price = x.Price, stock = x.Stock }));
});
pc.MapPost("/order", (HttpContext ctx, PcOrderReq req, Telegram tg) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    var (r, order) = club.Sell((req.Items ?? new()).Select(i => (i.ProductId, i.Qty)).ToList(), "balance", req.Username, p);
    if (r.Ok) tg.Notify($"🍔 {p.Name}: заказ #{order!.Id} на {order.Total} ₽ — {string.Join(", ", order.Lines.Select(l => $"{l.Name} ×{l.Qty}"))}");
    return Results.Ok(new { ok = r.Ok, error = r.Error, orderId = order?.Id, total = order?.Total, balance = r.Ok ? club.BalanceOfLogin(req.Username) : (int?)null });
});

pc.MapGet("/update", async (HttpContext ctx, string? version, UpdateFeed feed) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    var m = await feed.ForPcAsync(p, version ?? "0.0.0");
    return m is null ? Results.NoContent() : Results.Ok(m);
});

pc.MapPost("/heartbeat", (HttpContext ctx, HeartbeatReq req, UpdateFeed feed) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    club.Touch(p.Id);
    feed.SeenVersion(p.Id, req.Version);
    var s = club.ActiveSessionOfPc(p.Id);
    // Сервер сообщает остаток времени: клиент сверяет свой таймер с серверным.
    return Results.Ok(new { ok = true, time = Db.Now(), session = s is null ? null : new { id = s.Id, user = s.Guest, remainingSec = Math.Max(0, s.Ends - Db.Now()) } });
});

app.Map("/ws/pc", async (HttpContext ctx, PcHub hub) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
    var p = AuthPc(ctx);
    if (p is null) { ctx.Response.StatusCode = 401; return; }
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    club.Touch(p.Id);
    await hub.RunAsync(p.Id, socket, () => club.Touch(p.Id), ctx.RequestAborted);
});

// ======================= кабинет гостя (cryvx.ru) =======================
var guestApi = app.MapGroup("/api/guest");
CabinetGuest? GuestOf(HttpContext ctx) => club.GuestByToken(ctx.Request.Headers["X-Guest-Token"].FirstOrDefault());
object GuestDto(CabinetGuest g) => new { login = g.Login, balance = g.Balance, bonus = g.Bonus, phone = g.Phone, birth = g.Birth, loyalty = club.Loyalty(g.Id) };

guestApi.MapPost("/register", (WebRegisterReq req, HttpContext ctx) =>
{
    var (token, error) = club.RegisterWeb(ClientIp(ctx), req.Login, req.Password, req.Birth, req.Phone);
    return token is null ? Results.Ok(new { ok = false, error }) : Results.Ok(new { ok = true, token });
});
guestApi.MapPost("/login", (WebLoginReq req, HttpContext ctx) =>
{
    var (token, error) = club.LoginWeb(ClientIp(ctx), req.Login, req.Password);
    return token is null ? Results.Ok(new { ok = false, error }) : Results.Ok(new { ok = true, token });
});
guestApi.MapPost("/logout", (HttpContext ctx) =>
{
    if (ctx.Request.Headers["X-Guest-Token"].FirstOrDefault() is { } t) club.LogoutGuest(t);
    return Results.Ok(new { ok = true });
});
guestApi.MapGet("/me", (HttpContext ctx) => GuestOf(ctx) is { } g ? Results.Ok(GuestDto(g)) : Unauthorized("Войдите"));
guestApi.MapGet("/history", (HttpContext ctx) => GuestOf(ctx) is { } g ? Results.Ok(club.Transactions(g.Id).Take(50)) : Unauthorized("Войдите"));
guestApi.MapPost("/password", (WebPasswordReq req, HttpContext ctx) =>
{
    if (GuestOf(ctx) is not { } g) return Unauthorized("Войдите");
    var r = club.ChangePasswordWeb(g.Id, req.OldPassword, req.NewPassword);
    return Results.Ok(new { ok = r.Ok, error = r.Error });
});
guestApi.MapPost("/promo", (WebPromoReq req, HttpContext ctx) =>
{
    if (GuestOf(ctx) is not { } g) return Unauthorized("Войдите");
    var (r, bonus) = club.RedeemPromoWeb(g.Id, req.Code);
    return Results.Ok(new { ok = r.Ok, error = r.Error, bonus });
});
// тарифы и зоны клуба (для страницы брони и цен) — без входа
guestApi.MapGet("/club", () => Results.Ok(new
{
    zones = club.Zones(),
    tariffs = club.Tariffs(null).Select(t => new { id = t.Id, name = t.Name, minutes = t.Minutes, price = t.Price, zone = t.Zone, fromHour = t.Window?.from, toHour = t.Window?.to }),
}));
guestApi.MapGet("/free", (string? zone, long starts, int minutes) => Results.Ok(club.FreePcs(zone, starts, Math.Clamp(minutes, 60, 720))));
guestApi.MapGet("/bookings", (HttpContext ctx) => GuestOf(ctx) is { } g ? Results.Ok(club.GuestBookings(g.Id)) : Unauthorized("Войдите"));
guestApi.MapPost("/bookings", (WebBookingReq req, HttpContext ctx) =>
{
    if (GuestOf(ctx) is not { } g) return Unauthorized("Войдите");
    var (r, b) = club.BookWeb(g, req.PcId, req.Starts, req.Minutes);
    return Results.Ok(new { ok = r.Ok, error = r.Error, booking = b });
});
guestApi.MapDelete("/bookings/{id:long}", (long id, HttpContext ctx) =>
    GuestOf(ctx) is { } g ? Results.Ok(new { ok = club.CancelGuestBooking(g.Id, id) }) : Unauthorized("Войдите"));
// онлайн-пополнение
guestApi.MapGet("/pay/available", (YooKassa yk) => Results.Ok(new { available = yk.Configured }));
guestApi.MapPost("/pay", async (WebPayReq req, HttpContext ctx, YooKassa yk) =>
{
    if (GuestOf(ctx) is not { } g) return Unauthorized("Войдите");
    var origin = $"https://{ctx.Request.Host}";
    var (url, error) = await yk.CreateAsync(g, req.Amount, origin + "/cabinet?paid=1");
    return Results.Ok(new { ok = url is not null, url, error });
});
guestApi.MapGet("/payments", async (HttpContext ctx, YooKassa yk) =>
{
    if (GuestOf(ctx) is not { } g) return Unauthorized("Войдите");
    var list = club.GuestPayments(g.Id);
    foreach (var p in list.Where(p => p.Status == "pending" && Db.Now() - p.Created < 86400).Take(3)) await yk.SyncAsync(p.Id); // вернулся с оплаты — проверяем
    return Results.Ok(club.GuestPayments(g.Id));
});
// уведомление ЮKassa: тело не проверяем, а сами спрашиваем статус платежа по его id
app.MapPost("/api/guest/pay/notify", async (HttpContext ctx, YooKassa yk) =>
{
    try
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
        if (doc.RootElement.TryGetProperty("object", out var o) && o.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 and < 64 } pid)
            await yk.SyncAsync(pid);
    }
    catch (Exception) { /* мусор */ }
    return Results.Ok();
});

// ======================= админ =======================
// Вход сотрудника (до проверки ключа: у него ещё нет токена).
app.MapPost("/api/admin/login", async (OperatorLoginReq req, HttpContext ctx) =>
{
    var (token, actor, error) = await operators.LoginAsync(req.Login, req.Password, ClientIp(ctx));
    if (token is null) return Results.Json(new { ok = false, error }, statusCode: 401);
    ctx.Items["actor"] = actor;
    Audit(ctx, "Вход в админку");
    return Results.Ok(new { ok = true, token, name = actor!.Name, role = actor.Role });
});

var admin = app.MapGroup("/api/admin");
admin.AddEndpointFilter(async (ctx, next) =>
{
    var http = ctx.HttpContext;
    var actor = ActorOf(http);
    if (actor is null) return Unauthorized("Нужен ключ администратора или вход сотрудника");
    if (!actor.IsOwner && Operators.OwnerOnly(http.Request.Method, http.Request.Path)) return Results.Json(new { ok = false, error = "Доступно только владельцу" }, statusCode: 403);
    http.Items["actor"] = actor;
    var result = await next(ctx);
    if (!HttpMethods.IsGet(http.Request.Method) && !http.Items.ContainsKey("audited")) Audit(http, $"{http.Request.Method} {http.Request.Path}"); // кто и когда что менял
    return result;
});
admin.MapGet("/audit", () =>
{
    using var c = db.Open();
    return Results.Ok(Db.Query<object>(c, "SELECT id, ts, ip, action, details FROM audit ORDER BY id DESC LIMIT 300",
        r => new { id = r.GetInt64(0), ts = r.GetInt64(1), ip = r.IsDBNull(2) ? null : r.GetString(2), action = r.GetString(3), details = r.IsDBNull(4) ? null : r.GetString(4) }));
});
admin.MapGet("/version", () => Results.Ok(new { version }));
admin.MapGet("/me", (HttpContext ctx) => ctx.Items["actor"] is Actor a ? Results.Ok(new { name = a.Name, role = a.Role }) : Results.Unauthorized());
admin.MapPost("/logout", (HttpContext ctx) =>
{
    if (ctx.Request.Headers["X-Admin-Key"].FirstOrDefault() is { } t) operators.Logout(t);
    return Results.Ok(new { ok = true });
});
admin.MapGet("/operators", () => Results.Ok(operators.List()));
admin.MapPost("/operators", (OperatorReq req) =>
{
    var r = operators.Create(req.Login, req.Name ?? "", req.Password, req.Role);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapPut("/operators/{id:long}", (long id, OperatorUpdateReq req) =>
{
    var r = operators.Update(id, req.Password, req.Active, req.Role, req.Name);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});

// ---- каталог игр и приложений ----
object AppDto(AppEntry a) => new
{
    id = a.Id, title = a.Title, kind = a.Kind, group = a.Group, cover = a.Cover is null ? null : "/covers/" + a.Cover,
    paths = a.Paths, args = a.Args, workDir = a.WorkDir, clubAccount = a.ClubAccount, minAge = a.MinAge, enabled = a.Enabled, pinned = a.Pinned, sort = a.Sort,
};
admin.MapGet("/apps", () => Results.Ok(catalog.Apps(onlyEnabled: false).Select(AppDto)));
admin.MapPut("/apps", (AppReq req) =>
{
    var r = catalog.Upsert(new AppEntry(req.Id ?? "", req.Title ?? "", req.Kind ?? "game", req.Group, null, req.Paths ?? new(), req.Args, req.WorkDir,
        req.ClubAccount ?? false, req.MinAge, req.Enabled ?? true, req.Pinned ?? false, req.Sort ?? 0));
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapDelete("/apps/{id}", (string id) => catalog.Delete(id) ? Results.Ok(new { ok = true }) : Results.NotFound(new { ok = false, error = "Не найдено" }));
admin.MapPost("/apps/{id}/cover", async (string id, HttpContext ctx) =>
{
    var limit = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 2_500_000;
    using var ms = new MemoryStream();
    await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
    var r = catalog.SetCover(id, ms.ToArray());
    return r.Ok ? Results.Ok(new { ok = true, cover = "/covers/" + r.Error }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapDelete("/apps/{id}/cover", (string id) => catalog.ClearCover(id) ? Results.Ok(new { ok = true }) : Results.NotFound(new { ok = false }));

// ---- запрещённые программы ----
admin.MapGet("/blocked", () => Results.Ok(catalog.Blocked()));
admin.MapPost("/blocked", (BlockedReq req) =>
{
    var r = catalog.AddBlocked(req.Process ?? "", req.Note);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapDelete("/blocked/{id:long}", (long id) => catalog.RemoveBlocked(id) ? Results.Ok(new { ok = true }) : Results.NotFound(new { ok = false }));

admin.MapGet("/stats", () => Results.Ok(club.Stats()));

admin.MapGet("/pcs", (PcHub hub, UpdateFeed feed) =>
{
    var now = Db.Now();
    var active = club.Sessions(activeOnly: true).ToDictionary(s => s.PcId);
    var bookings = club.NextBookings();
    var calls = club.Calls().ToDictionary(x => x.PcId);
    return Results.Ok(club.Pcs().Select(p => new
    {
        id = p.Id, name = p.Name, zone = p.Zone,
        online = hub.IsConnected(p.Id) || now - p.LastSeen < 20,
        lastSeen = p.LastSeen,
        session = active.TryGetValue(p.Id, out var s) ? new { id = s.Id, guest = s.Guest, tariff = s.TariffId, started = s.Started, remainingSec = Math.Max(0, s.Ends - now) } : null,
        booking = bookings.TryGetValue(p.Id, out var b) ? new { id = b.Id, guest = b.Guest, starts = b.Starts, ends = b.Ends } : null,
        call = calls.TryGetValue(p.Id, out var call) ? new { ts = call.Ts, guest = call.Guest } : null,
        version = feed.VersionOf(p.Id),
    }));
});

admin.MapPost("/pcs", (NewPcReq req) =>
{
    var (p, token, error) = club.CreatePc(req.Name, req.Zone);
    // Токен показывается один раз: в базе только его хеш.
    return p is null ? Results.BadRequest(new { ok = false, error }) : Results.Ok(new { ok = true, id = p.Id, name = p.Name, zone = p.Zone, token });
});

admin.MapDelete("/pcs/{id:long}", async (long id, PcHub hub) =>
{
    if (!club.DeletePc(id)) return Results.BadRequest(new { ok = false, error = "ПК не найден или на нём идёт сеанс" });
    await hub.DisconnectAsync(id);
    return Results.Ok(new { ok = true });
});

admin.MapPost("/pcs/{id:long}/token", async (long id, PcHub hub) =>
{
    var token = club.ResetPcToken(id);
    if (token is null) return Results.NotFound(new { ok = false, error = "ПК не найден" });
    await hub.DisconnectAsync(id);
    return Results.Ok(new { ok = true, token });
});

admin.MapPost("/pcs/{id:long}/command", async (long id, CommandReq req, PcHub hub) =>
{
    var allowed = new[] { "lock", "reboot", "shutdown", "message", "endSession" };
    if (!allowed.Contains(req.Name)) return Results.BadRequest(new { ok = false, error = "Неизвестная команда" });
    if (req.Name == "endSession") club.EndSession(id, "Завершено администратором");
    var delivered = await hub.SendAsync(id, new { type = "command", name = req.Name, text = req.Text });
    return Results.Ok(new { ok = true, delivered });
});

admin.MapGet("/guests", (string? q) =>
{
    var extra = club.GuestExtras();
    return Results.Ok(club.Guests(q).Select(g => new
    {
        id = g.Id, login = g.Login, balance = g.Balance, birth = g.Birth, banned = g.Banned, created = g.Created,
        bonus = extra.GetValueOrDefault(g.Id).bonus, phone = extra.GetValueOrDefault(g.Id).phone,
    }));
});
admin.MapGet("/guests/{id:long}/loyalty", (long id) => Results.Ok(club.Loyalty(id)));
admin.MapPost("/guests/{id:long}/bonus", (long id, BonusReq req) =>
{
    var (r, bonus) = club.AdjustBonus(id, req.Amount, req.Note);
    return r.Ok ? Results.Ok(new { ok = true, bonus }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapPost("/guests", (NewGuestReq req) =>
{
    var r = club.CreateGuest(req.Login, req.Password, req.Balance ?? 0, req.Birth);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapPut("/guests/{id:long}", (long id, UpdateGuestReq req) =>
{
    var r = club.UpdateGuest(id, req.Password, req.Banned, req.Birth);
    if (r.Ok && req.Phone is not null) r = club.SetPhone(id, req.Phone);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapPost("/guests/{id:long}/topup", async (long id, TopUpReq req, PcHub hub, HttpContext http) =>
{
    var (r, balance) = club.TopUp(id, req.Amount, req.Note, req.RequestId, req.Method);
    if (!r.Ok) return Results.BadRequest(new { ok = false, error = r.Error });
    http.Items["audited"] = true;
    if (r.Error == "duplicate") return Results.Ok(new { ok = true, balance }); // повтор того же запроса: ничего не делаем
    var how = req.Method switch { "cash" => " наличными", "card" => " картой", "sbp" => " по СБП", _ => "" };
    Audit(http, req.Amount > 0 ? "Пополнение" : "Списание", $"гость #{id}: {req.Amount:+#;-#} ₽{how}, баланс {balance} ₽{(string.IsNullOrWhiteSpace(req.Note) ? "" : " · " + req.Note)}");
    // Гость уже сидит за ПК: сразу присылаем ему новый баланс, чтобы не перезаходить.
    if (club.BalanceTarget(id) is { } t) await hub.SendAsync(t.pcId, new { type = "command", name = "balance", user = t.login, balance = t.balance });
    return Results.Ok(new { ok = true, balance });
});
admin.MapGet("/guests/{id:long}/transactions", (long id) => Results.Ok(club.Transactions(id)));

admin.MapGet("/tariffs", () => Results.Ok(club.Tariffs(null, onlyActive: false)));
admin.MapPut("/tariffs", (TariffReq req) =>
{
    var r = club.UpsertTariff(req);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapDelete("/tariffs/{id}", (string id) => club.DeleteTariff(id) ? Results.Ok(new { ok = true }) : Results.NotFound(new { ok = false }));

admin.MapGet("/sessions", (bool? active) => Results.Ok(club.Sessions(active ?? false)));
admin.MapPost("/sessions/{id:long}/stop", async (long id, PcHub hub) =>
{
    var s = club.EndSessionById(id, "Завершено администратором");
    if (s is null) return Results.NotFound(new { ok = false, error = "Сеанс не найден или уже завершён" });
    await hub.SendAsync(s.PcId, new { type = "command", name = "endSession", text = "Сеанс завершён администратором" });
    return Results.Ok(new { ok = true });
});
admin.MapPost("/sessions/{id:long}/add-time", async (long id, AddTimeReq req, PcHub hub) =>
{
    var s = club.AddTime(id, req.Minutes);
    if (s is null) return Results.BadRequest(new { ok = false, error = "Нельзя изменить время этого сеанса" });
    await hub.SendAsync(s.PcId, new { type = "command", name = "sync", remainingSec = Math.Max(0, s.Ends - Db.Now()) });
    return Results.Ok(new { ok = true, remainingSec = Math.Max(0, s.Ends - Db.Now()) });
});

// Обложки игр: имена файлов содержат случайную часть и не меняются, поэтому кэшируются надолго.
// ---- смены и касса ----
admin.MapGet("/shift", () =>
{
    var sh = club.CurrentShift();
    return Results.Ok(new { shift = sh, report = sh is null ? null : club.ShiftReportOf(sh.Id) });
});
admin.MapPost("/shift/open", (ShiftOpenReq req) =>
{
    var (r, sh) = club.OpenShift(req.Cashier, req.Cash);
    return r.Ok ? Results.Ok(new { ok = true, shift = sh }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapPost("/shift/close", (ShiftCloseReq req, Telegram tg) =>
{
    var (r, report) = club.CloseShift(req.Cash, req.Note);
    if (!r.Ok) return Results.BadRequest(new { ok = false, error = r.Error });
    tg.Notify(club.ShiftReportText(report!));
    return Results.Ok(new { ok = true, report });
});
admin.MapGet("/shifts", () => Results.Ok(club.Shifts()));
admin.MapGet("/shifts/{id:long}", (long id) => club.ShiftReportOf(id) is { } r ? Results.Ok(r) : Results.NotFound(new { ok = false }));

// ---- бар и склад ----
admin.MapGet("/products", () => Results.Ok(club.Products(onlyActive: false)));
admin.MapPut("/products", (ProductReq req) =>
{
    var r = club.UpsertProduct(req.Id, req.Name, req.Category, req.Price, req.Stock, req.Active ?? true, req.Sort ?? 0);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapPost("/products/{id:long}/stock", (long id, StockReq req) =>
{
    var r = club.AddStock(id, req.Qty);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapPost("/sales", async (SaleReq req, PcHub hub) =>
{
    var (r, order) = club.Sell((req.Items ?? new()).Select(i => (i.ProductId, i.Qty)).ToList(), req.Method, req.Login, null);
    if (!r.Ok) return Results.BadRequest(new { ok = false, error = r.Error });
    // продажа с баланса гостя, который сидит за ПК: присылаем новый баланс
    if (req.Method == "balance" && order!.GuestId is { } gid && club.BalanceTarget(gid) is { } t)
        await hub.SendAsync(t.pcId, new { type = "command", name = "balance", user = t.login, balance = t.balance });
    return Results.Ok(new { ok = true, order });
});
admin.MapGet("/orders", (bool? all) => Results.Ok(club.Orders(pendingOnly: all != true)));
admin.MapPost("/orders/{id:long}/done", (long id) => club.FinishOrder(id, cancel: false) is { Ok: true } ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = "Заказ не найден или уже закрыт" }));
admin.MapPost("/orders/{id:long}/cancel", async (long id, PcHub hub) =>
{
    var o = club.Orders(pendingOnly: true).FirstOrDefault(x => x.Id == id);
    var r = club.FinishOrder(id, cancel: true);
    if (!r.Ok) return Results.BadRequest(new { ok = false, error = r.Error });
    if (o?.GuestId is { } gid && club.BalanceTarget(gid) is { } t)
        await hub.SendAsync(t.pcId, new { type = "command", name = "balance", user = t.login, balance = t.balance });
    return Results.Ok(new { ok = true });
});

// ---- аналитика ----
admin.MapGet("/analytics", (int? days) => Results.Ok(club.Analytics(days ?? 7)));

// ---- лояльность ----
admin.MapGet("/levels", () => Results.Ok(club.Levels()));
admin.MapPut("/levels", (List<LevelReq> req) =>
{
    var r = club.SetLevels(req.Select(l => (l.Name, l.MinSpent, l.CashbackPct)).ToList());
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapGet("/promos", () => Results.Ok(club.Promos()));
admin.MapPut("/promos", (PromoUpsertReq req) =>
{
    var r = club.UpsertPromo(req.Code, req.Bonus, req.MaxUses ?? 0, req.Expires, req.Active ?? true);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});

// ---- бронирование ----
admin.MapGet("/bookings", (bool? all) => Results.Ok(club.Bookings(upcomingOnly: all != true)));
admin.MapPost("/bookings", (BookingReq req) =>
{
    var (r, b) = club.CreateBooking(req.PcId, req.Login, req.Starts, req.Minutes, req.Note);
    return r.Ok ? Results.Ok(new { ok = true, booking = b }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapDelete("/bookings/{id:long}", (long id) => club.CancelBooking(id) ? Results.Ok(new { ok = true }) : Results.NotFound(new { ok = false, error = "Бронь не найдена или уже не активна" }));

// ---- вызовы администратора ----
admin.MapGet("/calls", () => Results.Ok(club.Calls()));
admin.MapPost("/calls/{pcId:long}/ack", (long pcId) => Results.Ok(new { ok = club.AckCall(pcId) }));

// ---- настройки: Telegram ----
admin.MapGet("/settings/updates", async (UpdateFeed feed, bool? refresh) =>
{
    var (m, error) = await feed.LatestAsync(force: refresh == true);
    return Results.Ok(new { enabled = feed.Enabled, mode = feed.TestOnly ? "test" : "all", ring = feed.Ring, url = feed.Url, latest = m?.Version, error });
});
admin.MapPut("/settings/updates", (UpdateSettingsReq req) =>
{
    if (req.Enabled is not null) club.SetSetting("upd_enabled", req.Enabled.Value ? "1" : null);
    if (req.Mode is "test" or "all") club.SetSetting("upd_mode", req.Mode);
    if (req.Ring is not null) club.SetSetting("upd_ring", req.Ring.Trim());
    if (req.Url is not null) club.SetSetting("upd_url", req.Url.Trim().StartsWith("https://") ? req.Url.Trim() : null);
    return Results.Ok(new { ok = true });
});
admin.MapGet("/settings/yookassa", (YooKassa yk) => Results.Ok(new { configured = yk.Configured, shopId = club.Setting("yk_shop"), receipt = club.Setting("yk_receipt") == "1", vat = club.Setting("yk_vat") }));
admin.MapPut("/settings/yookassa", (YooKassaReq req) =>
{
    if (req.ShopId is not null) club.SetSetting("yk_shop", req.ShopId.Trim());
    if (!string.IsNullOrWhiteSpace(req.Secret)) club.SetSetting("yk_secret", req.Secret.Trim());
    if (req.Receipt is not null) club.SetSetting("yk_receipt", req.Receipt.Value ? "1" : null);
    if (req.Vat is not null) club.SetSetting("yk_vat", req.Vat.Value.ToString());
    return Results.Ok(new { ok = true });
});
admin.MapGet("/settings/telegram", (Telegram tg) => Results.Ok(new { configured = tg.Configured, chat = club.Setting("tg_chat") }));
admin.MapPut("/settings/telegram", (TelegramReq req) =>
{
    if (req.Token is not null) club.SetSetting("tg_token", req.Token.Trim());
    if (req.Chat is not null) club.SetSetting("tg_chat", req.Chat.Trim());
    return Results.Ok(new { ok = true });
});
admin.MapPost("/settings/telegram/test", async (Telegram tg) =>
{
    var (ok, error) = await tg.SendAsync("✅ Мираж PRO: уведомления подключены");
    return Results.Ok(new { ok, error });
});

app.MapGet("/covers/{name}", (string name, HttpContext ctx) =>
{
    var path = catalog.CoverPath(name);
    if (path is null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    return Results.File(path, name.EndsWith(".png") ? "image/png" : name.EndsWith(".webp") ? "image/webp" : "image/jpeg");
});

app.Run();

public partial class Program { }
