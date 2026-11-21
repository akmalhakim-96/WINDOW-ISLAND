using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using WindowIsland.Core;
using WindowIsland.Widgets;
using Path = System.IO.Path;

namespace WindowIsland.Shell
{
    /// <summary>One tile in the dock.</summary>
    class DockTile
    {
        public string Key;               // exe path (lower) or special id
        public DockItem Pinned;          // null for running-only apps
        public string ExePath;
        public List<AppWindow> Windows = new();
        public FrameworkElement View;    // the square that scales
        public Ellipse Dot;
        public double Scale = 1, Target = 1;
        public Action Click;
        public Func<ContextMenu> Menu;
    }

    public partial class DockWindow : Window
    {
        IntPtr _hwnd;
        readonly List<DockTile> _tiles = new();
        readonly DispatcherTimer _refresh = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1200) };
        readonly DispatcherTimer _autoHide = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
        bool _animating, _hidden, _mouseInside;
        string _signature = "";
        TextBlock _statCpu, _statRam, _statNet, _calMonth, _calDay;

        double Size => Math.Clamp(Config.Current.DockIconSize, 32, 80);
        const double MaxMag = 1.55;

        public DockWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, __) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(_hwnd, noActivate: true);
                Position();
            };
            DpiChanged += (_, __) => Position();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, __) => Dispatcher.BeginInvoke(Position);

            _refresh.Tick += (_, __) => RefreshRunning();
            _refresh.Start();
            _autoHide.Tick += (_, __) => CheckAutoHide();
            _autoHide.Start();
            SystemStats.Updated += UpdateStats;
            FullscreenWatcher.Changed += fs => Visibility = fs ? Visibility.Hidden : Visibility.Visible;
        }

        // ---------------- layout ----------------
        void SizePill()
        {
            PillBg.Height = Size + 7 + 5 + 7;
        }

        void Position()
        {
            SizePill();
            if (_hwnd == IntPtr.Zero) return;
            var scr = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Height = Size * MaxMag + 40;
            Width = scr.Width / s;
            int w = scr.Width, h = (int)(Height * s);
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, scr.Left, scr.Bottom - h, w, h, Native.SWP_NOACTIVATE);
        }

        public void Rebuild()
        {
            _signature = "";
            Position();
            RefreshRunning();
        }

        /// <summary>Rebuilds tiles only when the set of apps changes; otherwise only updates dots.</summary>
        void RefreshRunning()
        {
            List<AppWindow> wins;
            try { wins = WindowTracker.Enumerate(); } catch { return; }
            var byExe = wins.Where(w => w.ExePath != null)
                            .GroupBy(w => w.ExePath.ToLowerInvariant())
                            .ToDictionary(g => g.Key, g => g.ToList());

            var pinned = Config.Current.DockItems;
            var pinnedKeys = new HashSet<string>(pinned.Select(p => ResolveExe(p.Path)?.ToLowerInvariant()).Where(k => k != null));
            var extra = byExe.Keys.Where(k => !pinnedKeys.Contains(k)).ToList();

            string sig = string.Join("|", pinned.Select(p => p.Path)) + "#" + string.Join("|", extra) + "#" + Size + Config.Current.ShowDockStats;
            if (sig != _signature)
            {
                _signature = sig;
                Build(pinned, extra, byExe);
            }
            foreach (var t in _tiles)
            {
                if (t.Key == null) continue;
                t.Windows = byExe.TryGetValue(t.Key, out var l) ? l : new List<AppWindow>();
                if (t.Dot != null) t.Dot.Opacity = t.Windows.Count > 0 ? 1 : 0;
            }
            if (_calDay != null && _calDay.Text != DateTime.Now.Day.ToString()) UpdateCalendarTile();
        }

        static string ResolveExe(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(path)) return Path.GetFullPath(path);
                string sys = Path.Combine(Environment.SystemDirectory, path);
                if (File.Exists(sys)) return sys;
                string win = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
                if (File.Exists(win)) return win;
            }
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return ShortcutTarget(path);
            return null;
        }

        static readonly Dictionary<string, string> LnkCache = new(StringComparer.OrdinalIgnoreCase);

        static string ShortcutTarget(string lnk)
        {
            if (LnkCache.TryGetValue(lnk, out var cached)) return cached;
            return LnkCache[lnk] = ShortcutTargetUncached(lnk);
        }

        static string ShortcutTargetUncached(string lnk)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic sh = Activator.CreateInstance(t);
                dynamic sc = sh.CreateShortcut(lnk);
                string target = sc.TargetPath;
                return string.IsNullOrEmpty(target) ? null : target;
            }
            catch { return null; }
        }

        void Build(List<DockItem> pinned, List<string> extra, Dictionary<string, List<AppWindow>> byExe)
        {
            Items.Children.Clear();
            _tiles.Clear();

            // Start / apps button
            AddTile(new DockTile { Click = SystemControl.OpenStart }, GlyphTile("", "#FF6B6B7B"), "Semua apl");
            AddSeparator();

            foreach (var item in pinned)
            {
                string exe = ResolveExe(item.Path);
                var tile = new DockTile { Pinned = item, ExePath = exe, Key = exe?.ToLowerInvariant() };
                tile.Click = () => OnAppClick(tile);
                tile.Menu = () => AppMenu(tile);
                var icon = IconCache.Get(item.Path) ?? (exe != null ? IconCache.Get(exe) : null);
                AddTile(tile, icon != null ? ImageTile(icon) : GlyphTile(item.Glyph ?? "", "#FF3B82F6"), item.Name ?? Path.GetFileNameWithoutExtension(item.Path), withDot: true);
            }

            if (extra.Count > 0)
            {
                AddSeparator();
                foreach (var key in extra)
                {
                    var w = byExe[key];
                    var tile = new DockTile { ExePath = w[0].ExePath, Key = key };
                    tile.Click = () => OnAppClick(tile);
                    tile.Menu = () => AppMenu(tile);
                    var icon = IconCache.Get(w[0].ExePath);
                    AddTile(tile, icon != null ? ImageTile(icon) : GlyphTile("", "#FF64748B"), WindowTracker.AppName(w[0].Handle), withDot: true);
                }
            }

            AddSeparator();
            AddTile(new DockTile { Click = () => WidgetManager.Toggle("today") }, CalendarTile(), "Hari ini");
            if (Config.Current.ShowDockStats)
                AddTile(new DockTile { Click = () => SystemControl.Launch("taskmgr.exe") }, StatsTile(), "Pengurus Tugas");
            var bin = IconCache.RecycleBin(false);
            AddTile(new DockTile { Click = () => SystemControl.Launch("shell:RecycleBinFolder") }, bin != null ? ImageTile(bin) : GlyphTile("", "#FF64748B"), "Tong Kitar Semula");
            UpdateStats();
        }

        // ---------------- tile factories ----------------
        void AddTile(DockTile tile, FrameworkElement content, string tooltip, bool withDot = false)
        {
            var host = new Border { Width = content.Width, Height = Size, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tooltip, RenderTransformOrigin = new Point(0.5, 1) };
            host.RenderTransform = new TranslateTransform();
            ToolTipService.SetInitialShowDelay(host, 150);
            ToolTipService.SetPlacement(host, System.Windows.Controls.Primitives.PlacementMode.Top);
            content.Width = double.NaN; content.Height = double.NaN;
            host.Child = content;
            tile.View = host;
            host.Tag = content.Tag; // base width ratio

            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 3, 0) };
            col.Children.Add(host);
            var dot = new Ellipse { Width = 4, Height = 4, Fill = (Brush)FindResource("WidgetFg"), Margin = new Thickness(0, 3, 0, 0), Opacity = 0, HorizontalAlignment = HorizontalAlignment.Center };
            if (withDot) tile.Dot = dot; else dot.Visibility = Visibility.Hidden;
            col.Children.Add(dot);

            host.MouseLeftButtonUp += (_, e) => { Bounce(host); tile.Click?.Invoke(); e.Handled = true; };
            host.MouseRightButtonUp += (_, e) =>
            {
                var m = tile.Menu?.Invoke();
                if (m != null) { m.PlacementTarget = host; m.Placement = System.Windows.Controls.Primitives.PlacementMode.Top; m.IsOpen = true; }
                e.Handled = true;
            };
            host.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle && tile.ExePath != null) { Bounce(host); SystemControl.Launch(tile.ExePath); } };
            Items.Children.Add(col);
            _tiles.Add(tile);
        }

        void AddSeparator() =>
            Items.Children.Add(new Border { Width = 1, Height = Size * 0.7, Background = (Brush)FindResource("DockBorder"), Margin = new Thickness(6, 0, 6, Size * 0.15 + 7), VerticalAlignment = VerticalAlignment.Bottom, Opacity = 1.6 });

        FrameworkElement ImageTile(ImageSource src)
        {
            var img = new Image { Source = src, Stretch = Stretch.Uniform, Width = Size, Height = Size, Tag = 1.0 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        FrameworkElement GlyphTile(string glyph, string bg)
        {
            var b = new Border
            {
                Width = Size, Height = Size, Tag = 1.0,
                CornerRadius = new CornerRadius(Size * 0.23),
                Background = ThemeManager.Brush(bg),
                Child = new Viewbox { Margin = new Thickness(Size * 0.24), Child = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), Foreground = Brushes.White } },
            };
            return b;
        }

        FrameworkElement CalendarTile()
        {
            _calMonth = new TextBlock { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            _calDay = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = ThemeManager.Brush("#FF1F1F25"), HorizontalAlignment = HorizontalAlignment.Center };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            grid.RowDefinitions.Add(new RowDefinition());
            var head = new Border { Background = ThemeManager.Brush("#FFEF4444"), Child = _calMonth };
            var body = new Viewbox { Child = _calDay, Margin = new Thickness(4, 0, 4, 2) };
            Grid.SetRow(body, 1);
            grid.Children.Add(head);
            grid.Children.Add(body);
            UpdateCalendarTile();
            return new Border { Width = Size, Height = Size, Tag = 1.0, CornerRadius = new CornerRadius(Size * 0.23), Background = Brushes.White, ClipToBounds = true, Child = grid };
        }

        void UpdateCalendarTile()
        {
            var n = DateTime.Now;
            _calMonth.Text = n.ToString("MMM").ToUpperInvariant();
            _calDay.Text = n.Day.ToString();
        }

        FrameworkElement StatsTile()
        {
            TextBlock L(double size) => new() { FontSize = size, Foreground = (Brush)FindResource("WidgetFg"), HorizontalAlignment = HorizontalAlignment.Left };
            _statCpu = L(11); _statRam = L(11); _statNet = L(10);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            sp.Children.Add(_statCpu); sp.Children.Add(_statRam); sp.Children.Add(_statNet);
            return new Border
            {
                Width = Size * 1.9, Height = Size, Tag = 1.9,
                CornerRadius = new CornerRadius(Size * 0.23),
                Background = (Brush)FindResource("WidgetBg"),
                BorderBrush = (Brush)FindResource("DockBorder"), BorderThickness = new Thickness(1),
                Child = new Viewbox { Child = sp, Stretch = Stretch.Uniform, Margin = new Thickness(2) },
            };
        }

        void UpdateStats()
        {
            if (_statCpu == null || !IsVisible) return;
            _statCpu.Text = $"CPU  {SystemStats.Cpu,3:0}%";
            _statRam.Text = $"RAM  {SystemStats.Ram,3:0}%";
            _statNet.Text = "↓ " + SystemStats.Speed(SystemStats.DownBps);
        }

        // ---------------- actions ----------------
        void OnAppClick(DockTile t)
        {
            if (t.Windows.Count > 0) { WindowTracker.ToggleApp(t.Windows); return; }
            if (t.Pinned != null) SystemControl.Launch(t.Pinned.Path, t.Pinned.Args);
            else if (t.ExePath != null) SystemControl.Launch(t.ExePath);
        }

        ContextMenu AppMenu(DockTile t)
        {
            var m = new ContextMenu();
            void Add(string text, Action a) { var i = new MenuItem { Header = text }; i.Click += (_, __) => a(); m.Items.Add(i); }
            foreach (var w in t.Windows.Take(8))
            {
                var win = w;
                Add(win.Title.Length > 48 ? win.Title.Substring(0, 47) + "…" : win.Title, () => WindowTracker.Activate(win.Handle));
            }
            if (t.Windows.Count > 0) m.Items.Add(new Separator());
            string launchPath = t.Pinned?.Path ?? t.ExePath;
            if (launchPath != null) Add(t.Windows.Count > 0 ? "Buka tetingkap baharu" : "Buka", () => SystemControl.Launch(launchPath, t.Pinned?.Args));
            if (t.Pinned != null)
                Add("Buang dari Dock", () => { Config.Current.DockItems.Remove(t.Pinned); Config.Save(); Rebuild(); });
            else if (t.ExePath != null)
                Add("Simpan di Dock", () =>
                {
                    Config.Current.DockItems.Add(new DockItem { Name = WindowTracker.AppName(t.Windows.FirstOrDefault()?.Handle ?? IntPtr.Zero), Path = t.ExePath });
                    Config.Save(); Rebuild();
                });
            if (t.Windows.Count > 0)
            {
                m.Items.Add(new Separator());
                Add(t.Windows.Count > 1 ? "Tutup semua tetingkap" : "Tutup", () => { foreach (var w in t.Windows) Native.PostMessage(w.Handle, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); });
            }
            m.Items.Add(new Separator());
            Add("Tetapan Dock…", () => App.Instance.ShowSettings());
            return m;
        }

        void Pill_DragOver(object s, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        }

        void Pill_Drop(object s, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            foreach (var f in files)
            {
                if (Config.Current.DockItems.Any(d => string.Equals(d.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
                Config.Current.DockItems.Add(new DockItem { Name = Path.GetFileNameWithoutExtension(f), Path = f });
            }
            Config.Save();
            Rebuild();
        }

        static void Bounce(FrameworkElement el)
        {
            var tt = el.RenderTransform as TranslateTransform;
            if (tt == null) return;
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(-16, KeyTime.FromPercent(0.25), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.5), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(-7, KeyTime.FromPercent(0.72), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            tt.BeginAnimation(TranslateTransform.YProperty, a);
        }

        // ---------------- magnification (smooth, frame-synced) ----------------
        void Pill_MouseMove(object s, MouseEventArgs e)
        {
            _mouseInside = true;
            double x = e.GetPosition(Items).X;
            double range = Size * 2.4;
            foreach (var t in _tiles)
            {
                var v = t.View;
                double center = v.TranslatePoint(new Point(v.ActualWidth / 2, 0), Items).X;
                double d = Math.Abs(x - center);
                double k = d >= range ? 0 : (Math.Cos(d / range * Math.PI) + 1) / 2; // smooth bell curve
                t.Target = 1 + (MaxMag - 1) * k;
            }
            StartAnim();
        }

        void Pill_MouseLeave(object s, MouseEventArgs e)
        {
            _mouseInside = false;
            foreach (var t in _tiles) t.Target = 1;
            StartAnim();
        }

        void StartAnim()
        {
            if (_animating) return;
            _animating = true;
            CompositionTarget.Rendering += OnFrame;
        }

        void OnFrame(object s, EventArgs e)
        {
            bool settled = true;
            foreach (var t in _tiles)
            {
                double diff = t.Target - t.Scale;
                if (Math.Abs(diff) > 0.002) { t.Scale += diff * 0.28; settled = false; }
                else t.Scale = t.Target;
                double ratio = t.View.Tag is double r ? r : 1.0;
                double sz = Size * t.Scale;
                t.View.Width = sz * ratio;
                t.View.Height = sz;
            }
            if (settled)
            {
                CompositionTarget.Rendering -= OnFrame; // stop rendering loop when idle = 0% CPU
                _animating = false;
            }
        }

        // ---------------- smart hide ----------------
        void CheckAutoHide()
        {
            if (!Config.Current.DockSmartHide || _hwnd == IntPtr.Zero) { if (_hidden) SlideTo(false); return; }
            if (!Native.GetWindowRect(_hwnd, out var dockRect)) return;
            Native.GetCursorPos(out var cur);
            var scr = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            double s = VisualTreeHelper.GetDpi(this).DpiScaleY;

            // pill rect in screen pixels
            var pillTopLeft = Pill.PointToScreen(new Point(0, 0));
            int pillTop = (int)pillTopLeft.Y, pillLeft = (int)pillTopLeft.X, pillRight = pillLeft + (int)(Pill.ActualWidth * s);

            bool cursorAtEdge = cur.Y >= scr.Bottom - 3 && cur.X >= pillLeft - 40 && cur.X <= pillRight + 40;
            bool cursorOnDock = !_hidden && cur.Y >= pillTop - 10 && cur.X >= pillLeft && cur.X <= pillRight;

            bool overlap = false;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && WindowTracker.PidOf(fg) != (uint)Environment.ProcessId && !Native.IsIconic(fg))
            {
                string cls = Native.GetClass(fg);
                if (cls != "Progman" && cls != "WorkerW" && Native.GetWindowRect(fg, out var r))
                    overlap = r.Bottom > pillTop && r.Right > pillLeft && r.Left < pillRight && r.Top < scr.Bottom;
            }

            if (_hidden && cursorAtEdge) SlideTo(false);
            else if (!_hidden && overlap && !cursorOnDock && !_mouseInside) SlideTo(true);
            else if (_hidden && !overlap && !cursorAtEdge) SlideTo(false);
        }

        void SlideTo(bool hide)
        {
            if (_hidden == hide) return;
            _hidden = hide;
            double h = Pill.ActualHeight + 14;
            Slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(hide ? h : 0, TimeSpan.FromMilliseconds(hide ? 260 : 200)) { EasingFunction = new CubicEase { EasingMode = hide ? EasingMode.EaseIn : EasingMode.EaseOut } });
        }
    }
}
