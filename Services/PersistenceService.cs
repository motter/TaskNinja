using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TaskNinja.Models;

namespace TaskNinja.Services;

/// <summary>
/// Saves and loads TaskNinja state to %AppData%\TaskNinja\.
///
/// Layout:
///   • settings.json      preferences
///   • tasks.json         all tasks across all buckets
///   • buckets.json       bucket definitions (id, name, order)
///   • people.json        autocomplete list of person names
///   • attachments\       PNG files referenced by TaskItem.Attachments
///
/// The save path is debounced + async (driven from MainViewModel) so
/// frequent edits don't thrash the disk.
/// </summary>
public class PersistenceService
{
    public string AppDir { get; }
    public string SettingsPath { get; }
    public string TasksPath { get; }
    public string BucketsPath { get; }
    public string PeoplePath { get; }
    public string AttachmentsDir { get; }

    public PersistenceService()
    {
        AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskNinja");
        SettingsPath = Path.Combine(AppDir, "settings.json");
        TasksPath = Path.Combine(AppDir, "tasks.json");
        BucketsPath = Path.Combine(AppDir, "buckets.json");
        PeoplePath = Path.Combine(AppDir, "people.json");
        AttachmentsDir = Path.Combine(AppDir, "attachments");
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(AttachmentsDir);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    // ── Settings ────────────────────────────────────────────────────

    public AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"settings load failed: {ex.Message}");
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings s)
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOpts));
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"settings save failed: {ex.Message}");
        }
    }

    // ── Buckets ─────────────────────────────────────────────────────

    public List<Bucket> LoadBuckets()
    {
        try
        {
            if (File.Exists(BucketsPath))
            {
                var json = File.ReadAllText(BucketsPath);
                var list = JsonSerializer.Deserialize<List<Bucket>>(json) ?? new();
                // Ensure the default bucket always exists with the stable Id.
                if (!list.Any(b => b.Id == Bucket.DefaultBucketId))
                {
                    list.Insert(0, new Bucket
                    {
                        Id = Bucket.DefaultBucketId,
                        Name = "Tasks",
                        SortOrder = 0,
                    });
                }
                return list;
            }
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"buckets load failed: {ex.Message}");
        }
        // Fresh install
        return new List<Bucket>
        {
            new() { Id = Bucket.DefaultBucketId, Name = "Tasks", SortOrder = 0 },
        };
    }

    public void SaveBuckets(IEnumerable<Bucket> buckets)
    {
        try
        {
            var list = buckets.ToList();
            File.WriteAllText(BucketsPath, JsonSerializer.Serialize(list, JsonOpts));
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"buckets save failed: {ex.Message}");
        }
    }

    // ── Tasks ───────────────────────────────────────────────────────

    public List<TaskItem> LoadTasks()
    {
        try
        {
            if (!File.Exists(TasksPath)) return new();
            var json = File.ReadAllText(TasksPath);
            var list = JsonSerializer.Deserialize<List<TaskItem>>(json) ?? new();
            // Migrate any tasks with empty BucketId to the default bucket.
            foreach (var t in list)
                if (string.IsNullOrEmpty(t.BucketId)) t.BucketId = Bucket.DefaultBucketId;
            return list;
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"tasks load failed: {ex.Message}");
            return new();
        }
    }

    public void SaveTasks(IEnumerable<TaskItem> tasks)
    {
        try
        {
            var list = tasks.ToList();
            var json = JsonSerializer.Serialize(list, JsonOpts);
            // Atomic write via temp file to avoid corruption on power loss.
            var tmp = TasksPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(TasksPath)) File.Delete(TasksPath);
            File.Move(tmp, TasksPath);
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"tasks save failed: {ex.Message}");
        }
    }

    // ── People (autocomplete) ───────────────────────────────────────

    public List<string> LoadPeople()
    {
        try
        {
            if (!File.Exists(PeoplePath)) return new();
            var json = File.ReadAllText(PeoplePath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"people load failed: {ex.Message}");
            return new();
        }
    }

    public void SavePeople(IEnumerable<string> people)
    {
        try
        {
            var distinct = people
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            File.WriteAllText(PeoplePath, JsonSerializer.Serialize(distinct, JsonOpts));
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"people save failed: {ex.Message}");
        }
    }

    // ── Attachments ─────────────────────────────────────────────────

    /// <summary>Save a bitmap to attachments\ and return the filename
    /// (caller stores this on the TaskItem). Filename is a fresh GUID
    /// so collisions are impossible.</summary>
    public string SaveAttachment(System.Windows.Media.Imaging.BitmapSource bmp)
    {
        var fileName = $"img_{Guid.NewGuid():N}.png";
        var path = Path.Combine(AttachmentsDir, fileName);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create);
        encoder.Save(fs);
        return fileName;
    }

    public System.Windows.Media.Imaging.BitmapImage? LoadAttachment(string fileName)
    {
        try
        {
            var path = Path.Combine(AttachmentsDir, fileName);
            if (!File.Exists(path)) return null;
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"attachment load failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Delete an attachment file. Safe if file doesn't exist.</summary>
    public void DeleteAttachment(string fileName)
    {
        try
        {
            var path = Path.Combine(AttachmentsDir, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"attachment delete failed: {ex.Message}");
        }
    }
}
