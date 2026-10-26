using System;
using Microsoft.Win32;

namespace TaskNinja.Services;

/// <summary>
/// Manages the "launch on Windows startup" registry entry under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run. No admin
/// rights required. Mirrors the ClipNinja implementation.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskNinja";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) is not null;
        }
        catch { return false; }
    }

    public static void Enable(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key is null) return;
            // --hidden flag so we start in the tray (no window flash)
            key.SetValue(ValueName, $"\"{exePath}\" --hidden");
        }
        catch (Exception ex)
        {
            Trace.Log("startup", $"Enable failed: {ex.Message}");
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Trace.Log("startup", $"Disable failed: {ex.Message}");
        }
    }
}
