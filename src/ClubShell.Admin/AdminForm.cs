using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClubShell.Admin;

/// <summary>
/// Окно администратора. Страница ui/admin.html не знает ключа: она шлёт сообщение {type:"api", method, path, body},
/// хост добавляет ключ и делает запрос к серверу (только пути /api/admin/…), ответ возвращает как {type:"apiResult"}.
/// </summary>
internal sealed class AdminForm : Form
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly Settings _settings = Settings.Load();
    private HttpClient? _http;

    public AdminForm()
    {
        Text = "Мираж PRO — администратор";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1360, 840);
        MinimumSize = new Size(1000, 640);
        BackColor = Color.Black;
        Controls.Add(_web);
        Load += async (_, _) => await InitWebAsync();
    }

    private async Task InitWebAsync()
    {
        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClubShell.Admin", "webview2");
        var env = await CoreWebView2Environment.CreateAsync(null, userData);
        await _web.EnsureCoreWebView2Async(env);

        var s = _web.CoreWebView2.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = Debugger.IsAttached;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = Debugger.IsAttached;

        _web.CoreWebView2.WebMessageReceived += OnWebMessage;
        _web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;

        var dev = Environment.GetEnvironmentVariable("CLUBSHELL_DEV_URL");
        if (!string.IsNullOrEmpty(dev)) _web.CoreWebView2.Navigate(dev.TrimEnd('/') + "/admin.html");
        else
        {
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Deny);
            _web.CoreWebView2.Navigate("https://app.local/admin.html");
        }
    }

    private void Rebuild()
    {
        _http?.Dispose();
        _http = null;
        if (Uri.TryCreate(_settings.ServerUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && _settings.GetKey() is { } key)
        {
            _http = new HttpClient { BaseAddress = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/"), Timeout = TimeSpan.FromSeconds(10) };
            _http.DefaultRequestHeaders.Add("X-Admin-Key", key);
        }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    Rebuild();
                    Post(new { type = "settings", serverUrl = _settings.ServerUrl, hasKey = _settings.HasKey });
                    break;

                case "saveSettings":
                    _settings.ServerUrl = (root.GetProperty("serverUrl").GetString() ?? "").Trim();
                    var key = root.GetProperty("key").GetString();
                    if (!string.IsNullOrWhiteSpace(key)) _settings.SetKey(key.Trim());
                    _settings.Save();
                    Rebuild();
                    Post(new { type = "settings", serverUrl = _settings.ServerUrl, hasKey = _settings.HasKey });
                    break;

                case "api":
                    await HandleApiAsync(root);
                    break;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[ClubShell.Admin] bridge error: {ex}"); }
    }

    private async Task HandleApiAsync(JsonElement root)
    {
        var id = root.GetProperty("id").GetInt32();
        var method = root.GetProperty("method").GetString() ?? "GET";
        var path = root.GetProperty("path").GetString() ?? "";

        // Пускаем только админские пути сервера и известные методы: страница не может обратиться ни к чему другому.
        if (!path.StartsWith("/api/admin/", StringComparison.Ordinal) || path.Contains("..") ||
            method is not ("GET" or "POST" or "PUT" or "DELETE"))
        {
            Post(new { type = "apiResult", id, status = 400, body = "{\"ok\":false,\"error\":\"Недопустимый запрос\"}" });
            return;
        }
        if (_http is null)
        {
            Post(new { type = "apiResult", id, status = 0, body = "{\"ok\":false,\"error\":\"Не задан адрес сервера или ключ\"}" });
            return;
        }

        try
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), path.TrimStart('/'));
            if (root.TryGetProperty("body", out var body) && body.ValueKind != JsonValueKind.Null)
                req.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req);
            Post(new { type = "apiResult", id, status = (int)resp.StatusCode, body = await resp.Content.ReadAsStringAsync() });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Post(new { type = "apiResult", id, status = 0, body = "{\"ok\":false,\"error\":\"Сервер недоступен\"}" });
        }
    }

    private void Post(object message) =>
        _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));

    protected override void Dispose(bool disposing)
    {
        if (disposing) _http?.Dispose();
        base.Dispose(disposing);
    }
}
