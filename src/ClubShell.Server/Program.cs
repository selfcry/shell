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

var catalog = new CatalogService(db, dataDir);
builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton(club);
builder.Services.AddSingleton(tz);
builder.Services.AddSingleton<PcHub>();
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

bool IsAdmin(HttpContext ctx)
{
    var key = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault();
    return !string.IsNullOrEmpty(key) && Security.FixedEquals(key, adminKey);
}

IResult Unauthorized(string what) => Results.Json(new { ok = false, error = what }, statusCode: 401);
object Pkg(Tariff t) => new { id = t.Id, name = t.Name, minutes = t.Minutes, price = t.Price, nightOnly = t.NightOnly };

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
        Db.Exec(c, "INSERT INTO audit(ts, ip, action, details) VALUES($t, $ip, $a, $d)",
            ("$t", Db.Now()), ("$ip", ctx.Request.Headers["X-Real-IP"].FirstOrDefault() ?? ctx.Connection.RemoteIpAddress?.ToString()), ("$a", action), ("$d", details));
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
        ? Results.Ok(new { ok = true, user = guest!.Login, balance = guest.Balance, packages = tariffs!.Select(Pkg) })
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
    var (ok, error, balance, minutes) = club.StartSession(p, req.Username, req.PackageId);
    return Results.Ok(new { ok, error, balance, minutes });
});

pc.MapPost("/session/stop", (HttpContext ctx, StopReq req) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    var s = club.EndSession(p.Id, "Гость завершил сеанс");
    club.Logout(p.Id); // после завершения купить время снова можно только после нового входа
    return Results.Ok(new { ok = true, ended = s is not null });
});

pc.MapPost("/heartbeat", (HttpContext ctx, HeartbeatReq req) =>
{
    var p = AuthPc(ctx); if (p is null) return Unauthorized("ПК не опознан");
    club.Touch(p.Id);
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

// ======================= админ =======================
var admin = app.MapGroup("/api/admin");
admin.AddEndpointFilter(async (ctx, next) =>
{
    var http = ctx.HttpContext;
    if (!IsAdmin(http)) return Unauthorized("Нужен ключ администратора");
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

admin.MapGet("/pcs", (PcHub hub) =>
{
    var now = Db.Now();
    var active = club.Sessions(activeOnly: true).ToDictionary(s => s.PcId);
    return Results.Ok(club.Pcs().Select(p => new
    {
        id = p.Id, name = p.Name, zone = p.Zone,
        online = hub.IsConnected(p.Id) || now - p.LastSeen < 20,
        lastSeen = p.LastSeen,
        session = active.TryGetValue(p.Id, out var s) ? new { id = s.Id, guest = s.Guest, tariff = s.TariffId, started = s.Started, remainingSec = Math.Max(0, s.Ends - now) } : null,
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

admin.MapGet("/guests", (string? q) => Results.Ok(club.Guests(q).Select(g => new { id = g.Id, login = g.Login, balance = g.Balance, birth = g.Birth, banned = g.Banned, created = g.Created })));
admin.MapPost("/guests", (NewGuestReq req) =>
{
    var r = club.CreateGuest(req.Login, req.Password, req.Balance ?? 0, req.Birth);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapPut("/guests/{id:long}", (long id, UpdateGuestReq req) =>
{
    var r = club.UpdateGuest(id, req.Password, req.Banned, req.Birth);
    return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
});
admin.MapPost("/guests/{id:long}/topup", async (long id, TopUpReq req, PcHub hub, HttpContext http) =>
{
    var (r, balance) = club.TopUp(id, req.Amount, req.Note, req.RequestId);
    if (!r.Ok) return Results.BadRequest(new { ok = false, error = r.Error });
    http.Items["audited"] = true;
    if (r.Error == "duplicate") return Results.Ok(new { ok = true, balance }); // повтор того же запроса: ничего не делаем
    Audit(http, req.Amount > 0 ? "Пополнение" : "Списание", $"гость #{id}: {req.Amount:+#;-#} ₽, баланс {balance} ₽{(string.IsNullOrWhiteSpace(req.Note) ? "" : " · " + req.Note)}");
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
app.MapGet("/covers/{name}", (string name, HttpContext ctx) =>
{
    var path = catalog.CoverPath(name);
    if (path is null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    return Results.File(path, name.EndsWith(".png") ? "image/png" : name.EndsWith(".webp") ? "image/webp" : "image/jpeg");
});

app.Run();

public partial class Program { }
