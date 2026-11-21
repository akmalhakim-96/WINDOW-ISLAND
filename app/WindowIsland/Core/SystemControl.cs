using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace WindowIsland.Core
{
    /// <summary>Taskbar hiding, autostart, power actions — all reversible.</summary>
    public static class SystemControl
    {
        // ---------- Taskbar ----------
        public static void ApplyTaskbar(bool hide)
        {
            try
            {
                var cfg = Config.Current;
                IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero) return;
                var abd = new Native.APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.APPBARDATA>(), hWnd = tray };

                if (hide)
                {
                    if (!cfg.TaskbarWasHiddenByUs)
                    {
                        cfg.TaskbarOriginalState = (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd);
                        cfg.TaskbarWasHiddenByUs = true;
                        Config.SaveNow();
                    }
                    // auto-hide releases the reserved work area, then we hide the window itself
                    abd.lParam = new IntPtr(Native.ABS_AUTOHIDE);
                    Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
                    SetTrayVisible(false);
                }
                else if (cfg.TaskbarWasHiddenByUs)
                {
                    int orig = cfg.TaskbarOriginalState < 0 ? Native.ABS_ALWAYSONTOP : cfg.TaskbarOriginalState;
                    abd.lParam = new IntPtr(orig);
                    Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
                    SetTrayVisible(true);
                    cfg.TaskbarWasHiddenByUs = false;
                    Config.SaveNow();
                }
            }
            catch (Exception ex) { Log.Error("Taskbar", ex); }
        }

        static void SetTrayVisible(bool visible)
        {
            int cmd = visible ? Native.SW_SHOWNA : Native.SW_HIDE;
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero) Native.ShowWindow(tray, cmd);
            IntPtr sec = IntPtr.Zero;
            while ((sec = Native.FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                Native.ShowWindow(sec, cmd);
        }

        // ---------- Autostart ----------
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void ApplyAutostart(bool on)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return;
                if (on) key.SetValue("WindowIsland", $"\"{Environment.ProcessPath}\" --startup");
                else key.DeleteValue("WindowIsland", false);
            }
            catch (Exception ex) { Log.Error("Autostart", ex); }
        }

        // ---------- Launch ----------
        public static void Launch(string path, string args = null)
        {
            try
            {
                if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo(path, args ?? "") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Launch " + path, ex);
                System.Windows.MessageBox.Show("Tidak dapat membuka:\n" + path, "Window Island");
            }
        }

        public static void OpenQuickSettings() => Native.SendWinCombo(0x41);   // Win+A
        public static void OpenNotifications() => Native.SendWinCombo(0x4E);   // Win+N
        public static void OpenStart() => Native.SendWinCombo(0);              // Win

        public static void Sleep() => System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false);
        public static void Restart() => Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
        public static void Shutdown() => Process.Start(new ProcessStartInfo("shutdown", "/s /hybrid /t 0") { CreateNoWindow = true, UseShellExecute = false });
        public static void Lock() => Native.LockWorkStation();
        public static void SignOut() => Native.ExitWindowsEx(0, 0);

        /// <summary>Gives back memory after startup (shell apps sit idle most of the time).</summary>
        public static void TrimMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Optimized, false, true);
                Native.EmptyWorkingSet(Native.GetCurrentProcess());
            }
            catch { }
        }
    }
}
