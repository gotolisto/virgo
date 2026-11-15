using System.Drawing;
using System.Windows.Forms;
using TrayTranslator.Core.Platform;

namespace TrayTranslator.App.Platform;

/// <summary>Notification-area icon and menu (WinForms NotifyIcon, which runs fine on the WPF message loop).</summary>
internal sealed class WindowsTrayIcon : ITrayIcon
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly Icon _image;
    private Action? _notificationClick;

    public WindowsTrayIcon(Action onDoubleClick)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))
            ?? throw new InvalidOperationException("Icon resource missing.");
        using (var stream = resource.Stream)
        {
            _image = new Icon(stream, SystemInformation.SmallIconSize);
        }

        _icon = new NotifyIcon { Icon = _image, ContextMenuStrip = _menu, Visible = false };
        _menu.Opening += (_, _) => MenuOpening?.Invoke(this, EventArgs.Empty);
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                onDoubleClick();
            }
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            var action = _notificationClick;
            _notificationClick = null;
            action?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _notificationClick = null;
    }

    public event EventHandler? MenuOpening;

    public void Show() => _icon.Visible = true;

    public void SetTooltip(string text)
    {
        // NotifyIcon tooltips are limited to 127 characters.
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _menu.SuspendLayout();
        _menu.Items.Clear();
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                _menu.Items.Add(new ToolStripSeparator());
                continue;
            }

            var menuItem = new ToolStripMenuItem(item.Text) { Enabled = item.IsEnabled, Checked = item.IsChecked };
            if (item.OnClick is { } onClick)
            {
                menuItem.Click += (_, _) => onClick();
            }

            _menu.Items.Add(menuItem);
        }

        _menu.ResumeLayout();
    }

    public void ShowNotification(string title, string message, Action? onClick = null)
    {
        _notificationClick = onClick;
        _icon.ShowBalloonTip(10_000, title, message, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }
}
