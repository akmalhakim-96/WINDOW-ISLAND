using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowIsland.Core;

namespace WindowIsland.Shell
{
    /// <summary>
    /// macOS-style menu bar. Registered as a Windows AppBar, so maximized windows
    /// sit below it instead of being covered.
    /// </summary>
    public partial class TopBarWindow : Window
    {
        const double BarHeight = 32;
        IntPtr _hwnd;
        int _cbMsg, _taskbarCreatedMsg;
        bool _registered;
        readonly DispatcherTimer _clock = new(DispatcherPriority.Background);
        readonly DispatcherTimer _slow = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };

        public TopBarWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            WindowTracker.ForegroundChanged += h => Dispatcher.BeginInvoke(() => AppName.Text = WindowTracker.AppName(h));
            SystemStats.Updated += () => NetText.Text = "↓ " + SystemStats.Speed(SystemStats.DownBps);

            _clock.Tick += (_, __) => UpdateClock();
            UpdateClock();
            _clock.Start();
            _slow.Tick += (_, __) => UpdateStatus();
            UpdateStatus();
            _slow.Start();
            NetworkChange.NetworkAvailabilityChanged += (_, __) => Dispatcher.BeginInvoke(UpdateStatus);
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(_hwnd, noActivate: true);
            Native.EnableBlur(_hwnd, Config.Current.TopBarBlur);
            HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
            _taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
            Register();
        }

        double Scale => VisualTreeHelper.GetDpi(this).DpiScaleY;

        void Register()
        {
            _cbMsg = Native.RegisterWindowMessage("WindowIslandAppBar");
            var abd = NewData();
            abd.uCallbackMessage = _cbMsg;
            Native.SHAppBarMessage(Native.ABM_NEW, ref abd);
            _registered = true;
            SetPosition();
        }

        Native.APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = _hwnd };

        void SetPosition()
        {
            if (!_registered) return;
            var scr = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            int h = (int)Math.Round(BarHeight * Scale);
            var abd = NewData();
            abd.uEdge = Native.ABE_TOP;
            abd.rc = new Native.RECT { Left = scr.Left, Top = scr.Top, Right = scr.Right, Bottom = scr.Top + h };
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
            abd.rc.Bottom = abd.rc.Top + h;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, abd.rc.Left, abd.rc.Top, abd.rc.Width, abd.rc.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _cbMsg && _cbMsg != 0)
            {
                switch (wParam.ToInt32())
                {
                    case Native.ABN_POSCHANGED: SetPosition(); break;
                    case Native.ABN_FULLSCREENAPP:
                        if (lParam != IntPtr.Zero) Native.ShowWindow(_hwnd, Native.SW_HIDE);
                        else { Native.ShowWindow(_hwnd, Native.SW_SHOWNA); SetPosition(); }
                        break;
                }
            }
            else if (msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
            {
                // Explorer restarted: our AppBar registration is gone, redo it.
                _registered = false;
                Register();
                App.Instance.OnTaskbarCreated();
            }
            else if (msg == 0x007E /*WM_DISPLAYCHANGE*/ || msg == 0x02E0 /*WM_DPICHANGED*/)
            {
                Dispatcher.BeginInvoke(SetPosition, DispatcherPriority.Background);
            }
            return IntPtr.Zero;
        }

        public void CloseBar()
        {
            if (_registered)
            {
                var abd = NewData();
                Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
                _registered = false;
            }
            Close();
        }

        void UpdateClock()
        {
            var now = DateTime.Now;
            string time = Config.Current.Use24h ? now.ToString("HH:mm") : now.ToString("h:mm tt");
            ClockText.Text = now.ToString("ddd d MMM") + "   " + time;
            _clock.Interval = TimeSpan.FromSeconds(60 - now.Second + 0.05);
        }

        void UpdateStatus()
        {
            WifiIcon.Text = NetworkInterface.GetIsNetworkAvailable() ? "" : "";
            if (Native.GetSystemPowerStatus(out var ps) && ps.BatteryFlag != 128 && ps.BatteryLifePercent <= 100)
            {
                BatteryBtn.Visibility = Visibility.Visible;
                BatteryText.Text = ps.BatteryLifePercent + "%";
                BatteryIcon.Text = ps.ACLineStatus == 1 ? "" : "";
            }
            else BatteryBtn.Visibility = Visibility.Collapsed;
        }

        void Logo_Click(object sender, RoutedEventArgs e)
        {
            var m = new ContextMenu { PlacementTarget = LogoBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            void Add(string text, Action a) { var i = new MenuItem { Header = text }; i.Click += (_, __) => a(); m.Items.Add(i); }
            Add("Tentang Window Island", () => App.Instance.ShowSettings());
            Add("Tetapan Window Island…", () => App.Instance.ShowSettings());
            Add("Tetapan Windows…", () => SystemControl.Launch("ms-settings:"));
            m.Items.Add(new Separator());
            Add("Tidur", SystemControl.Sleep);
            Add("Mula semula…", () => { if (Confirm("Mula semula komputer sekarang?")) SystemControl.Restart(); });
            Add("Matikan…", () => { if (Confirm("Matikan komputer sekarang?")) SystemControl.Shutdown(); });
            m.Items.Add(new Separator());
            Add("Kunci skrin", SystemControl.Lock);
            Add("Log keluar…", () => { if (Confirm("Log keluar sekarang? Kerja yang belum disimpan akan hilang.")) SystemControl.SignOut(); });
            m.IsOpen = true;
        }

        static bool Confirm(string q) => MessageBox.Show(q, "Window Island", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        void Apps_Click(object s, RoutedEventArgs e) => SystemControl.OpenStart();
        void Files_Click(object s, RoutedEventArgs e) => SystemControl.Launch("explorer.exe");
        void WinSettings_Click(object s, RoutedEventArgs e) => SystemControl.Launch("ms-settings:");
        void Quick_Click(object s, RoutedEventArgs e) => SystemControl.OpenQuickSettings();
        void Clock_Click(object s, RoutedEventArgs e) => SystemControl.OpenNotifications();
    }
}
