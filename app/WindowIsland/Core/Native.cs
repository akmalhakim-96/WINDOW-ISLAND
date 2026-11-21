using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowIsland.Core
{
    /// <summary>All Win32 interop in one place.</summary>
    internal static class Native
    {
        // ---------- AppBar ----------
        public const int ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
        public const int ABE_TOP = 1;
        public const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;
        public const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string msg);

        // ---------- Windows ----------
        public const int GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
        public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
        public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_MINIMIZE = 6, SW_SHOWNA = 8, SW_RESTORE = 9;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_BOTTOM = new IntPtr(1);
        public const int GW_OWNER = 4;
        public const int WM_CLOSE = 0x0010;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, int flags);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern bool ExitWindowsEx(uint flags, uint reason);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr l);

        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        public const int DWMWA_CLOAKED = 14;

        public static string GetTitle(IntPtr h)
        {
            int len = GetWindowTextLength(h);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetClass(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool IsCloaked(IntPtr h)
        {
            try { return DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int c, 4) == 0 && c != 0; }
            catch { return false; }
        }

        public static void MakeToolWindow(IntPtr h, bool noActivate = false)
        {
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            ex |= WS_EX_TOOLWINDOW;
            ex &= ~WS_EX_APPWINDOW;
            if (noActivate) ex |= WS_EX_NOACTIVATE;
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        }

        // ---------- WinEvent (foreground tracking) ----------
        public const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
        public delegate void WinEventDelegate(IntPtr hook, uint ev, IntPtr hwnd, int idObj, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventDelegate cb, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

        // ---------- Process ----------
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

        public static string GetProcessPath(uint pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        // ---------- System info ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        [StructLayout(LayoutKind.Sequential)] public struct FILETIME { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr h);

        // ---------- Keyboard (open Windows flyouts) ----------
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        public const byte VK_LWIN = 0x5B; public const uint KEYEVENTF_KEYUP = 2;
        public static void SendWinCombo(byte key)
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            if (key != 0) { keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // ---------- Shell icons ----------
        public const uint SHGFI_SYSICONINDEX = 0x4000;
        public const int SHIL_EXTRALARGE = 2, SHIL_JUMBO = 4;
        public const int ILD_TRANSPARENT = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHSTOCKICONINFO
        {
            public uint cbSize; public IntPtr hIcon; public int iSysImageIndex; public int iIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
        }
        public const uint SHGSI_SYSICONINDEX = 0x4000;
        public const int SIID_RECYCLER = 31, SIID_RECYCLERFULL = 32;
        [DllImport("shell32.dll")] public static extern int SHGetStockIconInfo(int siid, uint flags, ref SHSTOCKICONINFO info);

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IImageList
        {
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
        }
        [DllImport("shell32.dll", EntryPoint = "#727")] public static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

        // ---------- Blur behind (top bar) ----------
        [StructLayout(LayoutKind.Sequential)] public struct ACCENTPOLICY { public int AccentState, AccentFlags, GradientColor, AnimationId; }
        [StructLayout(LayoutKind.Sequential)] public struct WINCOMPATTRDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr h, ref WINCOMPATTRDATA data);

        public static void EnableBlur(IntPtr h, bool on)
        {
            try
            {
                var accent = new ACCENTPOLICY { AccentState = on ? 3 : 0 }; // 3 = ACCENT_ENABLE_BLURBEHIND
                int size = Marshal.SizeOf(accent);
                IntPtr p = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(accent, p, false);
                    var data = new WINCOMPATTRDATA { Attribute = 19, Data = p, SizeOfData = size }; // 19 = WCA_ACCENT_POLICY
                    SetWindowCompositionAttribute(h, ref data);
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { /* older builds: ignore */ }
        }
    }
}
