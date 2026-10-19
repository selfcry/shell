using ClubShell.Core;
using ClubShell.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "ClubShellService");

var shellConfig = ShellConfig.Load();                  // создаёт config.json и admin-pin.txt при первом запуске
builder.Services.AddSingleton(shellConfig);
// Адрес и токен заданы в config.json — работаем с настоящим сервером. Токен ПК остаётся в службе: окно ходит к серверу через неё.
builder.Services.AddSingleton<IClubApi>(_ => !string.IsNullOrWhiteSpace(shellConfig.ServerUrl) && !string.IsNullOrWhiteSpace(shellConfig.PcToken)
    ? new HttpClubApi(shellConfig.ServerUrl, shellConfig.PcToken)
    : new UnconfiguredClubApi()); // без токена ПК вход закрыт: тестовых гостей на боевом ПК быть не должно
builder.Services.AddSingleton<CommandBus>();
builder.Services.AddSingleton<LinkState>();
builder.Services.AddSingleton<LockerTracker>();

// Порядок важен: StartAsync вызываются по очереди. Канал создаётся первым (до долгой чистки профиля),
// чтобы его имя не успел занять процесс гостя; затем чистка профиля, и только потом страж запускает окно.
builder.Services.AddHostedService<ControlPipeServer>();
builder.Services.AddHostedService<CleanupWorker>();
builder.Services.AddHostedService<GuardianWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();
builder.Services.AddHostedService<ServerLinkWorker>();
builder.Services.AddHostedService<UpdateWorker>();    // автообновление клиента (вне сеанса, с проверкой подписи)
builder.Services.AddHostedService<SessionEnforcer>();

var host = builder.Build();
LockerMutex.Create(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ClubShell.Service.LockerMutex"));
host.Run();
