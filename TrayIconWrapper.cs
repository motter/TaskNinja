using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TaskNinja;

/// <summary>
/// Thin wrapper around System.Windows.Forms.NotifyIcon for the system
/// tray. Owns the lifetime of the icon and a simple context menu with
/// "Show window" and "Exit". Callers supply lambdas for the actions.
/// </summary>
public class TrayIconWrapper : IDisposable
{
    private readonly NotifyIcon _ni;

    public TrayIconWrapper(string tooltip, Action onLeftClick, Action onShow, Action onExit,
        Action? onShowDigest = null,
        Action? onShowSettings = null,
        Action? onShowWeeklyReport = null,
        Action? onCheckUpdates = null)
    {
        _ni = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = tooltip,
        };

        _ni.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) onLeftClick();
        };

        var menu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem("Show TaskNinja");
        showItem.Click += (_, _) => onShow();
        menu.Items.Add(showItem);
        if (onShowDigest is not null)
        {
            var digestItem = new ToolStripMenuItem("Show daily summary");
            digestItem.Click += (_, _) => onShowDigest();
            menu.Items.Add(digestItem);
        }
        if (onShowWeeklyReport is not null)
        {
            var reportItem = new ToolStripMenuItem("Weekly report");
            reportItem.Click += (_, _) => onShowWeeklyReport();
            menu.Items.Add(reportItem);
        }
        if (onShowSettings is not null)
        {
            menu.Items.Add(new ToolStripSeparator());
            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (_, _) => onShowSettings();
            menu.Items.Add(settingsItem);
        }
        if (onCheckUpdates is not null)
        {
            var updatesItem = new ToolStripMenuItem("Check for updates...");
            updatesItem.Click += (_, _) => onCheckUpdates();
            menu.Items.Add(updatesItem);
        }
        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => onExit();
        menu.Items.Add(exitItem);
        _ni.ContextMenuStrip = menu;
    }

    /// <summary>Update the tray tooltip — used to show overdue task count
    /// inline ("TaskNinja — 3 overdue") so the user has at-a-glance
    /// awareness even when the daily summary popup is dismissed.</summary>
    public void SetTooltip(string text)
    {
        // NotifyIcon truncates Text at 63 chars on older Windows. Keep
        // it well under that limit; longer messages get clipped silently.
        _ni.Text = text.Length > 63 ? text[..60] + "..." : text;
    }

    /// <summary>Load the bundled tasknin.ico from embedded resources, with
    /// a minimal fallback if the resource isn't present (won't happen in
    /// a published build, but lets dev/debug runs survive missing assets).</summary>
    private static Icon LoadIcon()
    {
        try
        {
            // Try the resource stream first
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("TaskNinja.Resources.tasknin.ico");
            if (stream is not null) return new Icon(stream);

            // Fall back: load from disk next to the .exe
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
            var iconPath = Path.Combine(exeDir, "tasknin.ico");
            if (File.Exists(iconPath)) return new Icon(iconPath);
        }
        catch { /* fall through */ }

        // Last resort: use a system icon so the tray entry is always visible
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        try { _ni.Visible = false; } catch { }
        try { _ni.Dispose(); } catch { }
    }
}
