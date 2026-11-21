using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WindowIsland.Core
{
    public class AppWindow
    {
        public IntPtr Handle;
        public string Title;
        public string ExePath;
        public uint Pid;
    }

    /// <summary>
    /// Tracks the foreground app (event driven, no polling) and enumerates
    /// switchable top-level windows for the dock.
    /// </summary>
    public static class WindowTracker
    {
        static Native.WinEventDelegate _cb; // keep alive (GC)
        static IntPtr _hook;
        static readonly uint OwnPid = (uint)Environment.ProcessId;
        static readonly Dictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Last foreground window that does not belong to Window Island.</summary>
        public static IntPtr LastExternal { get; private set; }
        public static event Action<IntPtr> ForegroundChanged;

        public static void Start()
        {
            _cb = OnEvent;
            _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _cb, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            OnEvent(IntPtr.Zero, 0, Native.GetForegroundWindow(), 0, 0, 0, 0);
        }

        public static void Stop()
        {
            if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        static void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObj, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero) return;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == OwnPid) return;
            LastExternal = hwnd;
            ForegroundChanged?.Invoke(hwnd);
        }

        public static uint PidOf(IntPtr h) { Native.GetWindowThreadProcessId(h, out uint pid); return pid; }

        /// <summary>Friendly app name for the top bar (file description, cached).</summary>
        public static string AppName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "Desktop";
            string cls = Native.GetClass(hwnd);
            if (cls == "Progman" || cls == "WorkerW") return "Desktop";
            if (cls == "Shell_TrayWnd") return "Taskbar";
            string path = Native.GetProcessPath(PidOf(hwnd));
            if (path == null) return Shorten(Native.GetTitle(hwnd));
            if (path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                return Shorten(Native.GetTitle(hwnd));
            if (NameCache.TryGetValue(path, out var n)) return n;
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                n = !string.IsNullOrWhiteSpace(vi.FileDescription) ? vi.FileDescription : Path.GetFileNameWithoutExtension(path);
            }
            catch { n = Path.GetFileNameWithoutExtension(path); }
            if (path.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase)) n = "File Explorer";
            n = Shorten(n);
            NameCache[path] = n;
            return n;
        }

        static string Shorten(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 32 ? s.Substring(0, 31) + "…" : s);

        static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
            "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow"
        };

        /// <summary>Visible, switchable app windows in Z-order (top first).</summary>
        public static List<AppWindow> Enumerate()
        {
            var result = new List<AppWindow>();
            Native.EnumWindows((h, _) =>
            {
                try
                {
                    if (!Native.IsWindowVisible(h)) return true;
                    if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;
                    long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                    if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
                    if (Native.IsCloaked(h)) return true;
                    string title = Native.GetTitle(h);
                    if (string.IsNullOrWhiteSpace(title)) return true;
                    if (IgnoredClasses.Contains(Native.GetClass(h))) return true;
                    uint pid = PidOf(h);
                    if (pid == OwnPid) return true;
                    string path = Native.GetProcessPath(pid);
                    if (path != null && path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                        path = UwpChildPath(h) ?? path;
                    result.Add(new AppWindow { Handle = h, Title = title, ExePath = path, Pid = pid });
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        static string UwpChildPath(IntPtr frame)
        {
            uint framePid = PidOf(frame);
            string found = null;
            Native.EnumChildWindows(frame, (c, _) =>
            {
                uint pid = PidOf(c);
                if (pid != framePid) { found = Native.GetProcessPath(pid); return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static void Activate(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.SetForegroundWindow(h);
        }

        /// <summary>
        /// macOS-style click: focus the app, or minimize it if it is already in front.
        /// The dock never takes focus (WS_EX_NOACTIVATE), so the foreground window is still the user's app.
        /// </summary>
        public static void ToggleApp(List<AppWindow> wins)
        {
            if (wins == null || wins.Count == 0) return;
            IntPtr fg = Native.GetForegroundWindow();
            bool appInFront = wins.Any(w => w.Handle == fg) && !Native.IsIconic(fg);
            if (appInFront)
            {
                foreach (var w in wins) Native.ShowWindow(w.Handle, Native.SW_MINIMIZE);
                return;
            }
            Activate(wins[0].Handle);
        }
    }
}
