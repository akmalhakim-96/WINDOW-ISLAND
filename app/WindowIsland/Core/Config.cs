using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace WindowIsland.Core
{
    public class DockItem
    {
        public string Name { get; set; }
        public string Path { get; set; }       // exe, lnk, folder, or uri (ms-settings:, shell:...)
        public string Args { get; set; }
        public string Glyph { get; set; }      // fallback Segoe icon glyph
    }

    public class WidgetState
    {
        public bool Visible { get; set; } = true;
        public double X { get; set; } = double.NaN;
        public double Y { get; set; } = double.NaN;
        /// <summary>True once the user dragged it; otherwise the layout engine places it.</summary>
        public bool Manual { get; set; }
    }

    public class ReminderItem
    {
        public string Text { get; set; }
        public bool Done { get; set; }
        public DateTime? Due { get; set; }
        public bool Alerted { get; set; }
    }

    public class AppConfig
    {
        /// <summary>"NotchDock" | "BarDock" | "NotchOnly" | "DockOnly"</summary>
        public string Layout { get; set; } = "NotchDock";
        [JsonIgnore] public bool ShowTopBar => Layout is "NotchDock" or "BarDock" or "NotchOnly";
        [JsonIgnore] public bool FullBar => Layout == "BarDock";
        [JsonIgnore] public bool ShowDock => Layout is "NotchDock" or "BarDock" or "DockOnly";
        public bool ShowNotifications { get; set; } = true;
        public bool ShowDockStats { get; set; } = true;
        public bool PrayerAlerts { get; set; } = true;
        public string Theme { get; set; } = "Malam";
        public bool ShowWidgets { get; set; } = true;
        public bool DockSmartHide { get; set; } = true;
        public bool HideTaskbar { get; set; } = false;
        public bool StartWithWindows { get; set; } = true;
        public bool Use24h { get; set; } = false;
        public bool TopBarBlur { get; set; } = true;
        public bool LightWidgets { get; set; } = true;
        public bool CheckUpdates { get; set; } = true;
        public double DockIconSize { get; set; } = 48;

        public string City { get; set; } = "Kuala Lumpur";
        public double Lat { get; set; } = 3.139;
        public double Lon { get; set; } = 101.6869;

        public List<DockItem> DockItems { get; set; }
        public Dictionary<string, WidgetState> Widgets { get; set; } = new();
        public string Notes { get; set; } = "“Dan Dia bersama kamu di mana saja kamu berada.”\n(QS. Al-Hadid: 4)";
        public List<ReminderItem> Reminders { get; set; }

        // Taskbar state that must be restored even after a crash
        public bool TaskbarWasHiddenByUs { get; set; }
        public int TaskbarOriginalState { get; set; } = -1;
        public bool FirstRunDone { get; set; }
    }

    public static class Config
    {
        public static readonly string Dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowIsland");
        static readonly string FilePath = System.IO.Path.Combine(Dir, "config.json");
        static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

        public static AppConfig Current { get; private set; } = new AppConfig();
        public static event Action Changed;

        static DispatcherTimer _saveTimer;

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath))
                    Current = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts) ?? new AppConfig();
            }
            catch (Exception ex)
            {
                Log.Error("Config rosak, guna lalai", ex);
                try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
                Current = new AppConfig();
            }
            Current.DockItems ??= DefaultDock();
            Current.Reminders ??= new List<ReminderItem>
            {
                new() { Text = "Cuba Window Island" },
                new() { Text = "Susun widget kegemaran" },
            };
            Current.Widgets ??= new();
        }

        public static List<DockItem> DefaultDock()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sys = Environment.SystemDirectory;
            var list = new List<DockItem>
            {
                new() { Name = "File Explorer", Path = System.IO.Path.Combine(win, "explorer.exe") },
            };
            foreach (var edge in new[] {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe" })
                if (File.Exists(edge)) { list.Add(new() { Name = "Microsoft Edge", Path = edge }); break; }
            foreach (var chrome in new[] {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe") })
                if (File.Exists(chrome)) { list.Add(new() { Name = "Google Chrome", Path = chrome }); break; }
            list.Add(new() { Name = "Notepad", Path = System.IO.Path.Combine(sys, "notepad.exe") });
            list.Add(new() { Name = "Kalkulator", Path = "calc.exe", Glyph = "\uE8EF" });
            list.Add(new() { Name = "Tetapan", Path = "ms-settings:", Glyph = "\uE713" });
            return list;
        }

        /// <summary>Debounced save (500 ms) so typing in notes doesn't hammer the disk.</summary>
        public static void Save(bool notify = true)
        {
            if (notify) Changed?.Invoke();
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _saveTimer.Tick += (_, __) => { _saveTimer.Stop(); SaveNow(); };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public static void SaveNow()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Opts));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex) { Log.Error("Gagal simpan config", ex); }
        }

        public static WidgetState Widget(string id)
        {
            if (!Current.Widgets.TryGetValue(id, out var s)) Current.Widgets[id] = s = new WidgetState();
            return s;
        }
    }
}
