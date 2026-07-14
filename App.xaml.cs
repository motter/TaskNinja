using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace TaskNinja;

public partial class App : Application
{
    /// <summary>
    /// Single source of truth for the app version. The csproj is bumped
    /// separately for the assembly version; this is what appears in the
    /// title bar, status messages, and About dialogs. Keep them in sync
    /// when releasing.
    /// </summary>
    public const string DisplayVersion = "1.2.0";

    private static Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private MainWindow? _mainWindow;

    /// <summary>P/Invoke to shell32 — sets the AppUserModelID for the
    /// current process. Windows uses this ID to group windows in the
    /// taskbar and to associate pinned shortcuts with running apps.</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appID);

    /// <summary>True if --hidden was on the command line (auto-launch
    /// on Windows startup uses this).</summary>
    public bool StartHidden { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Set an explicit AppUserModelID for this process. Without this,
        // Windows treats the pinned shortcut and the running app as TWO
        // separate taskbar entries (because the shortcut's implicit ID
        // doesn't match the process's implicit ID). Setting a stable,
        // documented ID at startup lets Windows group them correctly,
        // so clicking the pinned icon brings the running window forward
        // instead of stacking a second taskbar slot beside it.
        //
        // The ID is the company.app.subcomponent.version dotted format
        // recommended by Microsoft. Keep this string STABLE across
        // releases — changing it breaks existing pins. Bump only the
        // patch/minor portion if a major identity change is intended.
        try
        {
            SetCurrentProcessExplicitAppUserModelID("Anthropic.TaskNinja");
        }
        catch (Exception ex)
        {
            // Non-fatal — just means the taskbar may show two icons
            // when the user has a pinned shortcut. Worth logging.
            System.Diagnostics.Trace.WriteLine(
                $"[startup] SetCurrentProcessExplicitAppUserModelID failed: {ex.Message}");
        }

        // Parse --hidden BEFORE we touch anything else.
        foreach (var arg in e.Args)
        {
            if (string.Equals(arg, "--hidden", StringComparison.OrdinalIgnoreCase))
                StartHidden = true;
        }

        // Single-instance guard. We want EXACTLY ONE TaskNinja running at
        // a time, so the tray icon and global hotkey have a single owner.
        // The dance:
        //   1. Try to acquire the named mutex. If we own it, we ARE the
        //      primary instance — proceed to create the window.
        //   2. If we don't own it, another instance IS already running.
        //      Signal it via a named event to show its window, then exit.
        //
        // Robustness fixes layered on top of the basic dance:
        //   • Wrap the whole single-instance block in a defensive try/catch.
        //     A failure here should let the app launch anyway (preferring
        //     two instances to zero — the user can kill the duplicate from
        //     the tray).
        //   • Handle AbandonedMutexException: if a previous TaskNinja
        //     process crashed while holding the mutex, .NET signals
        //     ownership but throws AbandonedMutexException on the next
        //     WaitOne. We treat the mutex as ours in that case.
        //   • Use try-Mutex.OpenExisting for the secondary-instance check
        //     instead of relying purely on the createdNew flag, which has
        //     edge cases on some Windows builds where the OS reports
        //     createdNew=false even with no other process holding it.
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: false, "TaskNinja.SingleInstance");
            try
            {
                _ownsMutex = _singleInstanceMutex.WaitOne(TimeSpan.FromMilliseconds(50), false);
            }
            catch (AbandonedMutexException)
            {
                // Previous instance died holding the mutex. .NET hands us
                // ownership but flags it; for us, that's fine — we own it.
                _ownsMutex = true;
            }

            if (!_ownsMutex)
            {
                // Another process holds the mutex. Try to signal it via
                // the named event. There are three outcomes:
                //
                //   1. Signaling SUCCEEDS  → there's a healthy primary
                //      instance; we've told it to show its window. Exit.
                //
                //   2. Signaling FAILS because the event doesn't exist
                //      (WaitHandleCannotBeOpenedException) → the "owning"
                //      process is a zombie (it acquired the mutex but
                //      died/crashed before creating the event, likely from
                //      a previous version's startup bug). The mutex lock
                //      is effectively stale. Override and launch anyway —
                //      DO NOT exit, because that would lock the user out
                //      indefinitely.
                //
                //   3. Signaling fails for some other reason → also
                //      override and launch. Better two TaskNinjas than
                //      zero.
                bool signalled = false;
                try
                {
                    using var ev = EventWaitHandle.OpenExisting("TaskNinja.ShowWindow");
                    ev.Set();
                    signalled = true;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // Event doesn't exist → zombie lock. Proceed.
                    System.Diagnostics.Trace.WriteLine(
                        "[startup] mutex held but show-event missing — treating as stale lock, proceeding");
                    _ownsMutex = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[startup] failed to signal existing instance: {ex.Message} — proceeding anyway");
                    _ownsMutex = true;
                }

                if (signalled)
                {
                    // Healthy primary instance handed off to. Exit cleanly.
                    Shutdown();
                    return;
                }
                // Otherwise fall through and launch as primary.
            }
        }
        catch (Exception ex)
        {
            // Single-instance logic failed for some unforeseen reason
            // (rare ACL issues, weird security contexts, etc). Log and
            // continue — better to launch a possible second instance
            // than to silently refuse to launch at all.
            System.Diagnostics.Trace.WriteLine($"[startup] single-instance check failed: {ex.Message}");
            _ownsMutex = false;
            _singleInstanceMutex = null;
        }

        base.OnStartup(e);

        _mainWindow = new MainWindow();

        // The "another instance asked us to show" channel. Wrap in try/catch
        // for the same reason as above — failure here is non-fatal.
        try
        {
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "TaskNinja.ShowWindow");
            ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) =>
            {
                _mainWindow?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _mainWindow?.ShowWindowFromTray();
                }));
            }, null, -1, executeOnlyOnce: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[startup] show-event registration failed: {ex.Message}");
        }

        if (StartHidden)
        {
            // Mirror the ClipNinja v2.4.3 fix: minimize + ShowInTaskbar=false
            // during the brief Show()/Hide() needed to materialize the HWND,
            // then RESTORE both so the next reveal puts a taskbar button up.
            _mainWindow.WindowState = WindowState.Minimized;
            _mainWindow.ShowInTaskbar = false;
            _mainWindow.Show();
            _mainWindow.Hide();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.ShowInTaskbar = true;
        }
        else
        {
            _mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainWindow?.OnAppExiting();
        try
        {
            if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        }
        catch { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
