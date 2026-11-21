using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace WindowIsland.Core
{
    /// <summary>CPU, RAM and network speed sampled once per second (only while someone listens).</summary>
    public static class SystemStats
    {
        public static double Cpu { get; private set; }
        public static double Ram { get; private set; }
        public static double DownBps { get; private set; }
        public static double UpBps { get; private set; }
        public static event Action Updated;

        static DispatcherTimer _t;
        static ulong _idle, _kernel, _user;
        static long _rx, _tx;
        static DateTime _last;

        public static void Start()
        {
            if (_t != null) return;
            _t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
            _t.Tick += (_, __) => Sample();
            Sample();
            _t.Start();
        }

        static void Sample()
        {
            try
            {
                if (Native.GetSystemTimes(out var i, out var k, out var u))
                {
                    ulong di = i.Value - _idle, dk = k.Value - _kernel, du = u.Value - _user;
                    if (_kernel != 0 && dk + du > 0) Cpu = Math.Clamp(100.0 * (dk + du - di) / (dk + du), 0, 100);
                    _idle = i.Value; _kernel = k.Value; _user = u.Value;
                }
                var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
                if (Native.GlobalMemoryStatusEx(ref m)) Ram = m.dwMemoryLoad;

                long rx = 0, tx = 0;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var s = ni.GetIPStatistics();
                    rx += s.BytesReceived; tx += s.BytesSent;
                }
                var now = DateTime.UtcNow;
                double secs = (now - _last).TotalSeconds;
                if (_rx != 0 && secs > 0)
                {
                    DownBps = Math.Max(0, (rx - _rx) / secs);
                    UpBps = Math.Max(0, (tx - _tx) / secs);
                }
                _rx = rx; _tx = tx; _last = now;
                Updated?.Invoke();
            }
            catch (Exception ex) { Log.Error("SystemStats", ex); }
        }

        public static string Speed(double bps)
        {
            if (bps >= 1024 * 1024) return $"{bps / 1024 / 1024:0.0} MB/s";
            if (bps >= 1024) return $"{bps / 1024:0} KB/s";
            return $"{bps:0} B/s";
        }
    }

    /// <summary>Detects full-screen apps/games so our UI gets out of the way.</summary>
    public static class FullscreenWatcher
    {
        public static bool IsFullscreen { get; private set; }
        public static event Action<bool> Changed;
        static DispatcherTimer _t;

        public static void Start()
        {
            _t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
            _t.Tick += (_, __) => Check();
            _t.Start();
        }

        static void Check()
        {
            bool fs = false;
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != IntPtr.Zero && WindowTracker.PidOf(fg) != (uint)Environment.ProcessId)
                {
                    string cls = Native.GetClass(fg);
                    if (cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd" && !Native.IsZoomed(fg) && Native.GetWindowRect(fg, out var r))
                    {
                        var scr = System.Windows.Forms.Screen.FromHandle(fg).Bounds;
                        fs = r.Left <= scr.Left && r.Top <= scr.Top && r.Right >= scr.Right && r.Bottom >= scr.Bottom;
                    }
                }
            }
            catch { }
            if (fs != IsFullscreen) { IsFullscreen = fs; Changed?.Invoke(fs); }
        }
    }

    public class IslandAlert
    {
        public string App, Title, Body, Glyph;
        public System.Windows.Media.ImageSource Icon;
        public double Seconds = 5;
    }

    /// <summary>
    /// Mirrors Windows notifications (WhatsApp, Discord, ...) into the island.
    /// Requires the user to allow "notification access"; silently disabled otherwise.
    /// </summary>
    public static class NotificationService
    {
        public static event Action<IslandAlert> Received;
        public static bool Available { get; private set; }
        static readonly HashSet<uint> Seen = new();
        static DispatcherTimer _t;
        static UserNotificationListener _listener;

        public static async Task StartAsync()
        {
            try
            {
                _listener = UserNotificationListener.Current;
                var status = await _listener.RequestAccessAsync();
                if (status != UserNotificationListenerAccessStatus.Allowed) { Log.Info("Akses notifikasi: " + status); return; }
                foreach (var n in await _listener.GetNotificationsAsync(NotificationKinds.Toast)) Seen.Add(n.Id);
                Available = true;
                _t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
                _t.Tick += async (_, __) => await Poll();
                _t.Start();
            }
            catch (Exception ex) { Log.Info("Notifikasi tidak disokong: " + ex.Message); }
        }

        static async Task Poll()
        {
            if (!Config.Current.ShowNotifications) return;
            try
            {
                var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                foreach (var n in list)
                {
                    if (!Seen.Add(n.Id)) continue;
                    var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                    var texts = binding?.GetTextElements()?.Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();
                    Received?.Invoke(new IslandAlert
                    {
                        App = n.AppInfo?.DisplayInfo?.DisplayName ?? "Notifikasi",
                        Title = texts.ElementAtOrDefault(0) ?? "",
                        Body = string.Join(" ", texts.Skip(1)),
                        Glyph = "",
                    });
                }
                if (Seen.Count > 500) { Seen.Clear(); foreach (var n in list) Seen.Add(n.Id); }
            }
            catch (Exception ex) { Log.Error("Poll notifikasi", ex); }
        }
    }

    /// <summary>Fires prayer-time alerts (works offline).</summary>
    public static class PrayerAlerts
    {
        public static event Action<IslandAlert> Due;
        static DispatcherTimer _t;
        static string _lastFired;

        public static void Start()
        {
            _t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
            _t.Tick += (_, __) => Check();
            _t.Start();
        }

        static void Check()
        {
            if (!Config.Current.PrayerAlerts) return;
            var now = DateTime.Now;
            foreach (var p in PrayerTimes.For(now, Config.Current.Lat, Config.Current.Lon))
            {
                if (p.Name == "Syuruk") continue;
                string key = p.Name + p.Time.ToString("yyyyMMddHHmm");
                if (now >= p.Time && now < p.Time.AddMinutes(1) && _lastFired != key)
                {
                    _lastFired = key;
                    Due?.Invoke(new IslandAlert { App = "Waktu Solat", Title = "Masuk waktu " + p.Name, Body = Config.Current.City + " · " + p.Time.ToString("h:mm tt"), Glyph = "", Seconds = 12 });
                }
            }
        }
    }
}

namespace WindowIsland.Core
{
    /// <summary>Camera / microphone in use (privacy dots in the island). Reads Windows' own usage registry.</summary>
    public static class PrivacyMonitor
    {
        public static bool Mic { get; private set; }
        public static bool Camera { get; private set; }
        public static event Action Changed;
        static DispatcherTimer _t;

        public static void Start()
        {
            _t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            _t.Tick += (_, __) =>
            {
                bool m = InUse("microphone"), c = InUse("webcam");
                if (m != Mic || c != Camera) { Mic = m; Camera = c; Changed?.Invoke(); }
            };
            _t.Start();
        }

        static bool InUse(string cap)
        {
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + cap);
                return root != null && Scan(root, 0);
            }
            catch { return false; }
        }

        static bool Scan(RegistryKey key, int depth)
        {
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                if (sub == null) continue;
                if (sub.GetValue("LastUsedTimeStart") is long start && start > 0 && sub.GetValue("LastUsedTimeStop") is long stop && stop == 0)
                    return true;
                if (depth == 0 && name == "NonPackaged" && Scan(sub, 1)) return true;
            }
            return false;
        }
    }

    /// <summary>Timed to-dos: "Mesyuarat @15:30" pops up in the island at 3:30 PM.</summary>
    public static class ReminderAlerts
    {
        public static event Action<IslandAlert> Due;
        static DispatcherTimer _t;

        public static void Start()
        {
            _t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
            _t.Tick += (_, __) =>
            {
                var now = DateTime.Now;
                foreach (var r in Config.Current.Reminders)
                {
                    if (r.Done || r.Alerted || r.Due == null || now < r.Due.Value) continue;
                    r.Alerted = true;
                    Config.Save(false);
                    Due?.Invoke(new IslandAlert { App = "Peringatan", Title = r.Text, Body = r.Due.Value.ToString("h:mm tt"), Glyph = "\uE787", Seconds = 15 });
                }
            };
            _t.Start();
        }

        static readonly System.Text.RegularExpressions.Regex TimeRx =
            new(@"@\s*(\d{1,2})(?:[:.](\d{2}))?\s*(am|pm|pg|ptg|mlm)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Parses "teks @15:30" / "teks @3pm". Returns cleaned text and the due time (today or tomorrow).</summary>
        public static (string Text, DateTime? Due) Parse(string input)
        {
            var m = TimeRx.Match(input ?? "");
            if (!m.Success) return (input?.Trim(), null);
            int h = int.Parse(m.Groups[1].Value), min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            string ap = m.Groups[3].Value.ToLowerInvariant();
            if ((ap == "pm" || ap == "ptg" || ap == "mlm") && h < 12) h += 12;
            if ((ap == "am" || ap == "pg") && h == 12) h = 0;
            if (h > 23 || min > 59) return (input.Trim(), null);
            var due = DateTime.Today.AddHours(h).AddMinutes(min);
            if (due <= DateTime.Now) due = due.AddDays(1);
            string text = TimeRx.Replace(input, "").Trim();
            return (text.Length == 0 ? "Peringatan" : text, due);
        }
    }
}
