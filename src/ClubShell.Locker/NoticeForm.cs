using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ClubShell.Locker;

/// <summary>
/// Уведомление поверх всех окон, в том числе полноэкранной игры: «осталось 5 минут», сообщение администратора.
/// Обычное окно WinForms без WebView: появляется мгновенно и не отнимает фокус у игры.
/// </summary>
internal sealed class NoticeForm : Form
{
    private static NoticeForm? _current;
    private readonly System.Windows.Forms.Timer _close = new();

    private NoticeForm(string title, string text, bool important, int seconds)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(14, 14, 14);
        Size = new Size(460, 118);
        Padding = new Padding(22, 16, 22, 16);
        Cursor = Cursors.Hand;

        var titleLabel = new Label
        {
            Text = title, AutoSize = false, Dock = DockStyle.Top, Height = 34,
            ForeColor = important ? Color.FromArgb(255, 90, 0) : Color.White,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold), BackColor = Color.Transparent,
        };
        var textLabel = new Label
        {
            Text = text, AutoSize = false, Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(200, 200, 200), Font = new Font("Segoe UI", 11f), BackColor = Color.Transparent,
        };
        Controls.Add(textLabel);
        Controls.Add(titleLabel);
        foreach (Control c in new Control[] { this, titleLabel, textLabel }) c.Click += (_, _) => Close();

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(area.Right - Width - 24, area.Top + 24);

        _close.Interval = seconds * 1000;
        _close.Tick += (_, _) => Close();
        _close.Start();
    }

    /// <summary>Показать уведомление (предыдущее закрывается). Вызывать в потоке интерфейса.</summary>
    public static void Show(string title, string text, bool important = false, int seconds = 8)
    {
        _current?.Close();
        _current = new NoticeForm(title, text, important, seconds);
        _current.FormClosed += (s, _) => { if (ReferenceEquals(_current, s)) _current = null; };
        _current.Show();
        SetWindowPos(_current.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    // окно не забирает фокус: игра продолжает получать ввод
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(new LinearGradientBrush(ClientRectangle, Color.FromArgb(255, 90, 0), Color.FromArgb(245, 255, 0), 0f), 2f);
        e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _close.Dispose();
        base.Dispose(disposing);
    }

    private const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
