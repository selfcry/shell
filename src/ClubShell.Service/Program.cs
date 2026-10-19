using ClubShell.Core;
using ClubShell.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "ClubShellService");

var shellConfig = ShellConfig.Load();                  // создаёт config.json и admin-pin.txt при первом запуске
builder.Services.AddSingleton(shellConfig);
// Адрес и токен заданы в config.json — работаем с настоящим сервером, иначе заглушка с тестовыми гостями.
builder.Services.AddSingleton<IClubApi>(_ => !string.IsNullOrWhiteSpace(shellConfig.ServerUrl) && !string.IsNullOrWhiteSpace(shellConfig.PcToken)
    ? new HttpClubApi(shellConfig.ServerUrl, shellConfig.PcToken)
    : new UnconfiguredClubApi()); // без токена ПК вход закрыт: тестовых гостей на боевом ПК быть не должно
builder.Services.AddSingleton<CommandBus>();
builder.Services.AddSingleton<LinkState>();

builder.Services.AddHostedService<CleanupWorker>();   // сначала чистка профиля гостя, потом страж
builder.Services.AddHostedService<GuardianWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();
builder.Services.AddHostedService<ServerLinkWorker>();
builder.Services.AddHostedService<ControlPipeServer>();
builder.Build().Run();
