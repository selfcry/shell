using System.Net.WebSockets;
using ClubShell.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://0.0.0.0:5080");

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
    Console.WriteLine($"Ключ администратора создан: {keyFile}");
}
var adminKey = File.ReadAllText(keyFile).Trim();

var club = new ClubService(db, tz);
club.SeedDefaults(builder.Configuration.GetValue("Club:SeedDemo", true));

builder.Services.AddSingleton(db);
builder.Services.AddSingleton(club);
builder.Services.AddSingleton<PcHub>();
builder.Services.AddHostedService<ExpiryWorker>();
builder.Services.AddHostedService<MaintenanceWorker>();
builder.Services.AddResponseCompression();

var app = builder.Build();
app.UseResponseCompression();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

Pc? AuthPc(HttpContext ctx)
{
    var token = ctx.Request.Headers["X-Pc-Token"].FirstOrDefault() ?? ctx.Request.Query["token"].FirstOrDefault();
    return string.IsNullOrEmpty(token) ? null : club.PcByToken(token);
}

bool IsAdmin(HttpContext ctx)
{
    var key = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault();
    return !string.IsNullOrEmpty(key) && Security.FixedEquals(key, adminKey);
}

IResult Unauthorized(string what) => Results.Json(new { ok = false, error = what }, statusCode: 401);
object Pkg(Tariff t) => new { id = t.Id, name = t.Name, minutes = t.Minutes, price = t.Price, nightOnly = t.NightOnly };

app.MapGet("/health", () => Results.Ok(new { ok = true, time = Db.Now() }));

// ======================= ПК =======================
var pc = app.MapGroup("/api/pc");

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
admin.AddEndpointFilter(async (ctx, next) => IsAdmin(ctx.HttpContext) ? await next(ctx) : Unauthorized("Нужен ключ администратора"));

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

admin.MapDelete("/pcs/{id:long}", (long id) =>
    club.DeletePc(id) ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = "ПК не найден или на нём идёт сеанс" }));

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
admin.MapPost("/guests/{id:long}/topup", (long id, TopUpReq req) =>
{
    var (r, balance) = club.TopUp(id, req.Amount, req.Note);
    return r.Ok ? Results.Ok(new { ok = true, balance }) : Results.BadRequest(new { ok = false, error = r.Error });
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

app.Run();

public partial class Program { }
