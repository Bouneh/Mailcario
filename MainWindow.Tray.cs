using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;
using DrawingIcon = System.Drawing.Icon;

namespace Mailcario;

public partial class MainWindow
{
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _trayOpenItem;
    private Forms.ToolStripMenuItem? _trayQuitItem;
    private DrawingIcon? _trayBaseIcon;
    private DrawingIcon? _trayUnreadIcon;
    private bool _hasMailAttention;
    private bool _exitRequested;

    private void InitializeTrayEvents()
    {
        Loaded += (_, _) => EnsureTrayIcon();
        Closing += (_, e) =>
        {
            if (_exitRequested || _trayIcon is null) return;
            e.Cancel = true;
            Hide();
        };
        Closed += (_, _) => DisposeTrayIcon();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null) return;
        _trayBaseIcon = LoadApplicationIcon();
        _trayUnreadIcon = CreateUnreadTrayIcon();
        _trayOpenItem = new Forms.ToolStripMenuItem();
        _trayQuitItem = new Forms.ToolStripMenuItem();
        _trayOpenItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(OpenFromMailNotice));
        _trayQuitItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(QuitMailcario));
        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.AddRange([_trayOpenItem, _trayQuitItem]);
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _hasMailAttention ? _trayUnreadIcon : _trayBaseIcon,
            Text = "Mailcario",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                Dispatcher.BeginInvoke(new Action(OpenFromMailNotice));
        };
        _trayIcon.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(new Action(OpenFromMailNotice));
        UpdateTrayLanguage();
    }

    private void UpdateTrayLanguage()
    {
        if (_trayOpenItem is not null) _trayOpenItem.Text = Localizer.T("openMailcarioTray");
        if (_trayQuitItem is not null) _trayQuitItem.Text = Localizer.T("quitMailcarioTray");
        if (_trayIcon is not null)
            _trayIcon.Text = _hasMailAttention ? "Mailcario · " + Localizer.T("newMailNotification") : "Mailcario";
    }

    private void SetMailAttention(bool active)
    {
        _hasMailAttention = active;
        if (_trayIcon is not null)
            _trayIcon.Icon = active ? _trayUnreadIcon : _trayBaseIcon;
        UpdateTrayLanguage();
    }

    private void ShowNewMailBalloon(string account, string detail)
    {
        if (_trayIcon is null) return;
        string title = Localizer.T("newMailNotification") + " · " + account;
        _trayIcon.ShowBalloonTip(8000, title.Length > 63 ? title[..63] : title,
            detail.Length > 230 ? detail[..227] + "…" : detail, Forms.ToolTipIcon.None);
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayBaseIcon?.Dispose();
        _trayBaseIcon = null;
        _trayUnreadIcon?.Dispose();
        _trayUnreadIcon = null;
    }

    internal void QuitMailcario()
    {
        _exitRequested = true;
        Application.Current.Shutdown();
    }

    private static DrawingIcon LoadApplicationIcon()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (executable is not null && DrawingIcon.ExtractAssociatedIcon(executable) is DrawingIcon icon)
                return icon;
        }
        catch (Exception) { /* The notification icon should not prevent Mailcario from starting. */ }
        return (DrawingIcon)SystemIcons.Application.Clone();
    }

    private static DrawingIcon CreateUnreadTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(24, 119, 128));
            using var outline = new Pen(Color.White, 2.1f);
            graphics.FillEllipse(background, 1, 1, 30, 30);
            graphics.DrawRectangle(outline, 6, 10, 20, 13);
            graphics.DrawLine(outline, 6, 10, 16, 17);
            graphics.DrawLine(outline, 26, 10, 16, 17);
        }
        nint handle = bitmap.GetHicon();
        try
        {
            using DrawingIcon icon = DrawingIcon.FromHandle(handle);
            return (DrawingIcon)icon.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint handle);
}
