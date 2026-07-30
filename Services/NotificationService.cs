using System;
using System.Windows.Threading;
using TaskNinja.Models;

namespace TaskNinja.Services;

/// <summary>
/// Decides when to surface the daily summary popup to the user. Owned
/// by MainWindow; ticks once a minute. Cheap.
///
/// Decision rules (in order):
///   1. If NotificationsEnabled is false → never show.
///   2. If there's a pending "remind me later" timestamp and now >= that
///      timestamp → show, clear the pending stamp, mark today shown.
///   3. If today's date is different from LastDigestShownDate AND the
///      current time-of-day is >= DailyDigestTime → show, mark today
///      shown, clear any pending remind-later.
///   4. Otherwise → no-op.
///
/// "Show today" tracking is by calendar date (YYYY-MM-DD), so the popup
/// reappears each day automatically. The user can also force-show via
/// the tray menu without affecting the LastDigestShownDate (since
/// they're choosing to look).
/// </summary>
public class NotificationService
{
    private readonly AppSettings _settings;
    private readonly Action _showDigest;
    private readonly Action _persistSettings;
    private DispatcherTimer? _timer;

    public NotificationService(AppSettings settings, Action showDigest, Action persistSettings)
    {
        _settings = settings;
        _showDigest = showDigest;
        _persistSettings = persistSettings;
    }

    public void Start()
    {
        // Tick every minute. We could tick every 10 seconds for tighter
        // timing but the digest is a daily event — minute granularity is
        // plenty. A faster tick burns more battery on laptops for no
        // user-visible benefit.
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1),
        };
        _timer.Tick += (_, _) => Evaluate();
        _timer.Start();

        // Also evaluate immediately on start, in case the user launched
        // the app AFTER the daily digest time and hasn't seen it yet.
        // Defer one tick so the main window is fully shown first; popping
        // a child window before the parent has rendered looks janky.
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(Evaluate),
            DispatcherPriority.ApplicationIdle);
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>Mark the digest as shown today (call from the host when
    /// the user dismisses the popup with "Got it for today"). Persists
    /// to settings.json so a subsequent app restart respects it.</summary>
    public void MarkShownToday()
    {
        _settings.LastDigestShownDate = DateTime.Today.ToString("yyyy-MM-dd");
        _settings.DigestRemindAt = null;
        _persistSettings();
    }

    /// <summary>Schedule a re-pop at the given time (user clicked
    /// "Remind me in 2h"). Persists immediately so the reminder
    /// survives an app restart.
    ///
    /// ALSO marks the digest as "shown today" — without this, the
    /// time-of-day check in <see cref="Evaluate"/> would re-pop the
    /// digest within ~60 seconds of dismissal (since the day's
    /// digest time has already passed). The DigestRemindAt timestamp
    /// is the authoritative trigger for the next pop.</summary>
    public void RemindAt(DateTime when)
    {
        _settings.DigestRemindAt = when;
        _settings.LastDigestShownDate = DateTime.Today.ToString("yyyy-MM-dd");
        _persistSettings();
    }

    /// <summary>Check whether to show the digest right now. Public so
    /// tests / debug paths can poke it; the timer normally calls it.</summary>
    public void Evaluate()
    {
        if (!_settings.NotificationsEnabled) return;

        var now = DateTime.Now;

        // (2) Remind-later override — fires regardless of time-of-day check.
        // Clear the trigger BEFORE showing so subsequent ticks while
        // the popup is open don't re-trigger it (the popup guards
        // against duplicate windows, but cleaner not to spam Show()).
        if (_settings.DigestRemindAt is { } remindAt && now >= remindAt)
        {
            _settings.DigestRemindAt = null;
            _persistSettings();
            _showDigest();
            return;
        }

        // (3) First-of-day check
        var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        if (_settings.LastDigestShownDate == todayStr) return;  // already shown today

        if (!TryParseDigestTime(_settings.DailyDigestTime, out var digestTime)) return;
        if (now.TimeOfDay < digestTime) return;  // not time yet

        _showDigest();
    }

    private static bool TryParseDigestTime(string s, out TimeSpan ts)
    {
        if (TimeSpan.TryParse(s, out ts)) return true;
        // Accept "8:00" without leading zero
        if (DateTime.TryParse("2000-01-01 " + s, out var d))
        {
            ts = d.TimeOfDay;
            return true;
        }
        ts = TimeSpan.Zero;
        return false;
    }
}
