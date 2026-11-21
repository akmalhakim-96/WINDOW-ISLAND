using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WindowIsland.Core;

namespace WindowIsland.Shell
{
    /// <summary>
    /// The notch / Dynamic Island. Compact by default, expands on hover with
    /// music controls and info, and pops open for notifications.
    /// Never takes keyboard focus (WS_EX_NOACTIVATE).
    /// </summary>
    public partial class IslandWindow : Window
    {
        enum State { Compact, Expanded, Alert }
        State _state = State.Compact;
        bool _pinned, _hover;
        IntPtr _hwnd;

        readonly Queue<IslandAlert> _alerts = new();
        readonly DispatcherTimer _alertTimer = new();
        readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
        readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
        readonly DispatcherTimer _clock = new(DispatcherPriority.Background);
        readonly DispatcherTimer _topmost = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        readonly DispatcherTimer _power = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        Storyboard _eqAnim;
        int _lastAc = -1;

        double CompactW => Config.Current.FullBar ? 170 : 200;
        double CompactH => Config.Current.FullBar ? 26 : 30;

        public IslandWindow()
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

            _hoverTimer.Tick += (_, __) => { _hoverTimer.Stop(); if (_hover && _state == State.Compact) Go(State.Expanded); };
            _leaveTimer.Tick += (_, __) => { _leaveTimer.Stop(); if (!_hover && !_pinned && _state == State.Expanded) Go(State.Compact); };
            _alertTimer.Tick += (_, __) => { _alertTimer.Stop(); NextAlert(); };

            _clock.Tick += (_, __) => Tick();
            Tick();
            _clock.Start();

            _topmost.Tick += (_, __) =>
            {
                if (_hwnd != IntPtr.Zero && IsVisible)
                    Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            };
            _topmost.Start();

            _power.Tick += (_, __) => CheckPower();
            _power.Start();

            App.Media.Updated += () => Dispatcher.BeginInvoke(UpdateMedia);
            SystemStats.Updated += UpdateStats;
            PrivacyMonitor.Changed += UpdatePrivacy;
            FullscreenWatcher.Changed += fs => Visibility = fs ? Visibility.Hidden : Visibility.Visible;

            BuildEq();
            UpdateMedia();
        }

        public void ApplyMode()
        {
            bool bar = Config.Current.FullBar;
            Pill.CornerRadius = bar ? new CornerRadius(13) : new CornerRadius(0, 0, 16, 16);
            Pill.Margin = bar ? new Thickness(0, 3, 0, 0) : new Thickness(0);
            Position();
            Go(_state);
        }

        void Position()
        {
            if (_hwnd == IntPtr.Zero) return;
            var scr = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int w = (int)(Width * s), h = (int)(Height * s);
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, scr.Left + (scr.Width - w) / 2, scr.Top, w, h, Native.SWP_NOACTIVATE);
        }

        // ---------------- state machine ----------------
        void Go(State s)
        {
            _state = s;
            switch (s)
            {
                case State.Compact: Morph(CompactW, CompactH, Compact); break;
                case State.Expanded: Morph(440, 150, Expanded); UpdateStats(); break;
                case State.Alert: Morph(400, 70, Alert); break;
            }
        }

        void Morph(double w, double h, FrameworkElement show)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(320);
            var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 };
            Pill.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = spring });
            Pill.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = spring });
            double r = Config.Current.FullBar || h > 40 ? Math.Min(h / 2, 26) : 16;
            Pill.CornerRadius = Config.Current.FullBar ? new CornerRadius(r) : new CornerRadius(0, 0, r, r);

            foreach (var el in new FrameworkElement[] { Compact, Expanded, Alert })
            {
                if (el == show)
                {
                    el.Visibility = Visibility.Visible;
                    el.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(el == Compact ? 120 : 90), EasingFunction = ease });
                }
                else
                {
                    var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
                    var target = el;
                    fade.Completed += (_, __) => { if (target != (_state == State.Compact ? Compact : _state == State.Expanded ? (FrameworkElement)Expanded : Alert)) target.Visibility = Visibility.Collapsed; };
                    el.BeginAnimation(OpacityProperty, fade);
                }
            }
        }

        void Pill_MouseEnter(object s, MouseEventArgs e) { _hover = true; _leaveTimer.Stop(); _hoverTimer.Start(); }
        void Pill_MouseLeave(object s, MouseEventArgs e) { _hover = false; _hoverTimer.Stop(); _leaveTimer.Start(); }

        void Pill_Click(object s, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && FindParent<System.Windows.Controls.Button>(d) != null) return;
            if (_state == State.Alert) { _alertTimer.Stop(); NextAlert(); return; }
            _pinned = _state != State.Expanded || !_pinned;
            Go(_pinned ? State.Expanded : State.Compact);
        }

        static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && d is not T) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        // ---------------- alerts ----------------
        public void ShowAlert(IslandAlert a)
        {
            if (a == null) return;
            _alerts.Enqueue(a);
            if (_state != State.Alert) NextAlert();
        }

        void NextAlert()
        {
            if (_alerts.Count == 0) { Go(_pinned || _hover ? State.Expanded : State.Compact); return; }
            var a = _alerts.Dequeue();
            AlertApp.Text = a.App;
            AlertTitle.Text = a.Title;
            AlertBody.Text = a.Body;
            AlertBody.Visibility = string.IsNullOrWhiteSpace(a.Body) ? Visibility.Collapsed : Visibility.Visible;
            AlertGlyph.Text = a.Glyph ?? "";
            Go(State.Alert);
            _alertTimer.Interval = TimeSpan.FromSeconds(a.Seconds);
            _alertTimer.Start();
        }

        // ---------------- content ----------------
        void Tick()
        {
            var now = DateTime.Now;
            CompactTime.Text = Config.Current.Use24h ? now.ToString("HH:mm") : now.ToString("h:mm");
            var c = Config.Current;
            var p = PrayerTimes.Next(now, c.Lat, c.Lon);
            var left = p.Time - now;
            string inT = left.TotalHours >= 1 ? $"{(int)left.TotalHours}j {left.Minutes}m" : $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m";
            PrayerChip.Text = $"{p.Name} {p.Time:h:mm tt} · {inT} lagi";
            _clock.Interval = TimeSpan.FromSeconds(60 - now.Second + 0.05);
            if (!App.Media.HasMedia) UpdateMedia();
        }

        void UpdateMedia()
        {
            var m = App.Media;
            if (m.HasMedia)
            {
                BigTitle.Text = m.Title;
                BigSub.Text = m.Artist;
                ArtImage.ImageSource = m.Art;
                ArtGlyph.Visibility = m.Art == null ? Visibility.Visible : Visibility.Collapsed;
                MediaControls.Visibility = Visibility.Visible;
                PlayBtn.Content = m.IsPlaying ? "" : "";
            }
            else
            {
                var now = DateTime.Now;
                BigTitle.Text = now.ToString("dddd, d MMMM");
                BigSub.Text = "Tiada muzik dimainkan · " + Config.Current.City;
                ArtImage.ImageSource = null;
                ArtGlyph.Visibility = Visibility.Visible;
                MediaControls.Visibility = Visibility.Collapsed;
            }
            bool playing = m.HasMedia && m.IsPlaying;
            Eq.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            if (playing) _eqAnim.Begin(this, true); else _eqAnim.Stop(this);
            UpdateCompactRight();
        }

        void UpdateStats()
        {
            if (_state != State.Expanded) return;
            CpuChip.Text = $"CPU {SystemStats.Cpu:0}%  ·  RAM {SystemStats.Ram:0}%";
            NetChip.Text = "↓ " + SystemStats.Speed(SystemStats.DownBps) + "  ↑ " + SystemStats.Speed(SystemStats.UpBps);
        }

        void UpdatePrivacy()
        {
            MicDot.Visibility = PrivacyMonitor.Mic ? Visibility.Visible : Visibility.Collapsed;
            CamDot.Visibility = PrivacyMonitor.Camera ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateCompactRight()
        {
            if (App.Media.HasMedia && App.Media.IsPlaying) { CompactRight.Text = ""; return; }
            if (Native.GetSystemPowerStatus(out var ps) && ps.BatteryFlag != 128 && ps.BatteryLifePercent <= 100)
                CompactRight.Text = (ps.ACLineStatus == 1 ? "⚡" : "") + ps.BatteryLifePercent + "%";
            else CompactRight.Text = DateTime.Now.ToString("ddd d");
        }

        void CheckPower()
        {
            UpdateCompactRight();
            if (!Native.GetSystemPowerStatus(out var ps) || ps.BatteryFlag == 128) { BatChipBox.Visibility = Visibility.Collapsed; return; }
            BatChipBox.Visibility = Visibility.Visible;
            BatChip.Text = (ps.ACLineStatus == 1 ? "⚡ " : "🔋 ") + ps.BatteryLifePercent + "%";
            if (_lastAc != -1 && ps.ACLineStatus != _lastAc)
                ShowAlert(new IslandAlert
                {
                    App = "Bateri",
                    Title = ps.ACLineStatus == 1 ? "Sedang mengecas" : "Guna bateri",
                    Body = ps.BatteryLifePercent + "%",
                    Glyph = ps.ACLineStatus == 1 ? "" : "",
                    Seconds = 3,
                });
            _lastAc = ps.ACLineStatus;
        }

        void BuildEq()
        {
            _eqAnim = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            int i = 0;
            foreach (var r in new[] { Eq1, Eq2, Eq3 })
            {
                var a = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(i++ * 140), AutoReverse = true, Duration = TimeSpan.FromMilliseconds(420) };
                a.KeyFrames.Add(new EasingDoubleKeyFrame(0.3, KeyTime.FromPercent(0)));
                a.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new SineEase()));
                Storyboard.SetTarget(a, r);
                Storyboard.SetTargetProperty(a, new PropertyPath("RenderTransform.ScaleY"));
                _eqAnim.Children.Add(a);
            }
            Timeline.SetDesiredFrameRate(_eqAnim, 30); // smooth enough, half the GPU work
        }

        void Prev_Click(object s, RoutedEventArgs e) => App.Media.Previous();
        void Play_Click(object s, RoutedEventArgs e) => App.Media.PlayPause();
        void Next_Click(object s, RoutedEventArgs e) => App.Media.Next();
    }
}
