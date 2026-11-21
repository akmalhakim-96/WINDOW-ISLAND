using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WindowIsland.Core;

namespace WindowIsland.Widgets
{
    /// <summary>A desktop widget: lives on the desktop layer, survives Win+D, draggable, remembers its place.</summary>
    public class WidgetWindow : Window
    {
        public string Id { get; }
        readonly IDisposable _view;

        public WidgetWindow(string id, FrameworkElement content, double width)
        {
            Id = id;
            _view = content as IDisposable;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = width + 24;
            Title = "Window Island Widget";
            FontFamily = (FontFamily)FindResource("UiFont");
            FontSize = 13;
            SetResourceReference(ForegroundProperty, "WidgetFg");

            var card = new Border
            {
                CornerRadius = new CornerRadius(22),
                Padding = new Thickness(16),
                Margin = new Thickness(12),
                Child = content,
                Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.22, RenderingBias = RenderingBias.Performance },
            };
            card.SetResourceReference(Border.BackgroundProperty, "WidgetBg");
            card.SetResourceReference(Border.BorderBrushProperty, "DockBorder");
            card.BorderThickness = new Thickness(1);
            Content = card;

            card.MouseLeftButtonDown += (_, e) =>
            {
                if (IsInteractive(e.OriginalSource as DependencyObject)) return;
                try { DragMove(); } catch { }
                WidgetManager.Dropped(this);
            };

            var menu = new ContextMenu();
            var hide = new MenuItem { Header = "Sembunyi widget ini" };
            hide.Click += (_, __) => { Config.Widget(Id).Visible = false; Config.Save(); WidgetManager.Apply(); };
            var settings = new MenuItem { Header = "Tetapan widget…" };
            settings.Click += (_, __) => App.Instance.ShowSettings();
            menu.Items.Add(hide);
            menu.Items.Add(settings);
            card.ContextMenu = menu;

            SourceInitialized += (_, __) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(hwnd);
                // Owned by the desktop window: stays on the desktop and survives "Show desktop" (Win+D).
                IntPtr progman = Native.FindWindow("Progman", null);
                if (progman != IntPtr.Zero) Native.SetWindowLongPtr(hwnd, Native.GWLP_HWNDPARENT, progman);
            };
            Closed += (_, __) => _view?.Dispose();
        }

        static bool IsInteractive(DependencyObject d)
        {
            while (d != null)
            {
                if (d is TextBoxBase || d is ButtonBase || d is ScrollBar) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>The visible card (window minus its 12px shadow margin), in DIPs.</summary>
        public Rect Card => new(Left + WidgetManager.Pad, Top + WidgetManager.Pad, Math.Max(0, Width - 2 * WidgetManager.Pad), Math.Max(0, ActualHeight - 2 * WidgetManager.Pad));

        public void MoveCardTo(Point p)
        {
            Left = p.X - WidgetManager.Pad;
            Top = p.Y - WidgetManager.Pad;
        }
    }

    /// <summary>
    /// Owns all widgets and guarantees nothing overlaps: widgets never cover each other,
    /// the notch, or the dock. Auto-placed widgets flow in columns using their real height
    /// and re-flow whenever any widget changes size; dragged widgets snap to the nearest free spot.
    /// </summary>
    public static class WidgetManager
    {
        public const double Pad = 12;   // shadow margin around each card
        const double Gap = 14;          // minimum space between cards

        public static readonly (string Id, string Name, bool DefaultOn)[] All =
        {
            ("today", "Hari Ini & Nota", true),
            ("weather", "Cuaca", true),
            ("clock", "Jam Analog", true),
            ("prayer", "Waktu Solat", true),
            ("reminders", "Peringatan", true),
            ("system", "Prestasi Sistem", false),
        };

        // Flow order: left side grows to the right, right side grows to the left.
        static readonly string[] LeftFlow = { "weather", "clock", "reminders" };
        static readonly string[] RightFlow = { "today", "prayer", "system" };

        static readonly Dictionary<string, WidgetWindow> Open = new();
        static DispatcherTimer _arrangeTimer;
        static bool _hooked;

        public static bool IsOn(string id)
        {
            if (!Config.Current.Widgets.ContainsKey(id))
                Config.Current.Widgets[id] = new WidgetState { Visible = All.FirstOrDefault(a => a.Id == id).DefaultOn };
            return Config.Current.Widgets[id].Visible;
        }

        public static void Toggle(string id)
        {
            bool on = !(Config.Current.ShowWidgets && IsOn(id) && Open.ContainsKey(id));
            Config.Widget(id).Visible = on;
            if (on) Config.Current.ShowWidgets = true;
            Config.Save();
            Apply();
            if (on && Open.TryGetValue(id, out var w)) w.Activate();
        }

        public static void Apply()
        {
            if (!_hooked)
            {
                _hooked = true;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, __) => Application.Current.Dispatcher.BeginInvoke(RequestArrange);
            }
            foreach (var (id, _, _) in All)
            {
                bool want = Config.Current.ShowWidgets && IsOn(id);
                if (want && !Open.ContainsKey(id))
                {
                    try
                    {
                        var w = Create(id);
                        w.Opacity = 0; // fade in once it has a real, non-overlapping spot
                        var s = Config.Widget(id);
                        if (s.Manual && !double.IsNaN(s.X) && !double.IsNaN(s.Y)) { w.Left = s.X; w.Top = s.Y; }
                        else { w.Left = SystemParameters.VirtualScreenLeft - 2000; w.Top = 0; }
                        w.SizeChanged += (_, __) => RequestArrange();
                        Open[id] = w;
                        w.Show();
                    }
                    catch (Exception ex) { Log.Error("Widget " + id, ex); }
                }
                else if (!want && Open.TryGetValue(id, out var w))
                {
                    Open.Remove(id);
                    w.Close();
                }
            }
            RequestArrange();
        }

        public static void ResetPositions()
        {
            foreach (var s in Config.Current.Widgets.Values) { s.Manual = false; s.X = double.NaN; s.Y = double.NaN; }
            Config.Save(false);
            Arrange();
        }

        /// <summary>Debounced re-layout (sizes settle after data loads).</summary>
        public static void RequestArrange()
        {
            if (_arrangeTimer == null)
            {
                _arrangeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                _arrangeTimer.Tick += (_, __) => { _arrangeTimer.Stop(); Arrange(); };
            }
            _arrangeTimer.Stop();
            _arrangeTimer.Start();
        }

        static WidgetWindow Create(string id) => id switch
        {
            "today" => new WidgetWindow(id, new TodayView(), 290),
            "weather" => new WidgetWindow(id, new WeatherView(), 290),
            "clock" => new WidgetWindow(id, new ClockView(), 150),
            "prayer" => new WidgetWindow(id, new PrayerView(), 230),
            "reminders" => new WidgetWindow(id, new RemindersView(), 250),
            "system" => new WidgetWindow(id, new SystemView(), 230),
            _ => throw new ArgumentException(id),
        };

        // ---------------- geometry ----------------

        /// <summary>Area widgets may use: below the notch/bar, above the dock.</summary>
        static Rect Usable()
        {
            var wa = SystemParameters.WorkArea;
            var c = Config.Current;
            double top = wa.Top + (c.ShowTopBar && !c.FullBar ? 40 : 12);
            double bottom = wa.Bottom - (c.ShowDock ? Math.Clamp(c.DockIconSize, 32, 80) + 40 : 12);
            return new Rect(wa.Left + 12, top, Math.Max(100, wa.Width - 24), Math.Max(100, bottom - top));
        }

        /// <summary>Space the island needs when fully expanded (top centre).</summary>
        static Rect NotchZone()
        {
            if (!Config.Current.ShowTopBar) return Rect.Empty;
            double w = 480, h = 180;
            return new Rect((SystemParameters.PrimaryScreenWidth - w) / 2, 0, w, h);
        }

        static bool Fits(Rect r, Rect usable, Rect notch, List<Rect> taken)
        {
            if (r.Left < usable.Left - 0.5 || r.Top < usable.Top - 0.5 || r.Right > usable.Right + 0.5 || r.Bottom > usable.Bottom + 0.5) return false;
            if (!notch.IsEmpty && notch.IntersectsWith(r)) return false;
            foreach (var t in taken)
            {
                var g = t; g.Inflate(Gap, Gap);
                if (g.IntersectsWith(r)) return false;
            }
            return true;
        }

        public static void Arrange()
        {
            if (Open.Count == 0) return;
            var usable = Usable();
            var notch = NotchZone();
            var taken = new List<Rect>();

            // 1) Widgets the user dragged keep their place, as long as they are still valid.
            foreach (var w in Open.Values)
            {
                var s = Config.Widget(w.Id);
                if (!s.Manual || w.ActualHeight <= 0) continue;
                var r = w.Card;
                if (Fits(r, usable, notch, taken)) taken.Add(r);
                else s.Manual = false; // screen changed or now overlaps: let the layout handle it
            }

            // 2) Everything else flows into columns.
            Flow(LeftFlow, usable, notch, taken, fromRight: false);
            Flow(RightFlow, usable, notch, taken, fromRight: true);

            Log.Info("Layout usable=" + R(usable) + " notch=" + (notch.IsEmpty ? "-" : R(notch)) + " | " +
                     string.Join(" ", Open.Values.Where(w => w.ActualHeight > 0).Select(w => w.Id + "=" + R(w.Card))));

            foreach (var w in Open.Values)
                if (w.Opacity < 1 && w.ActualHeight > 0)
                    w.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
        }

        static string R(Rect r) => FormattableString.Invariant($"{r.X:0},{r.Y:0},{r.Width:0},{r.Height:0}");

        static void Flow(string[] order, Rect usable, Rect notch, List<Rect> taken, bool fromRight)
        {
            double colX = fromRight ? usable.Right : usable.Left;
            double colWidth = 0, y = usable.Top;
            foreach (var id in order)
            {
                if (!Open.TryGetValue(id, out var w) || Config.Widget(id).Manual || w.ActualHeight <= 0) continue;
                var size = new Size(w.Card.Width, w.Card.Height);
                Rect? spot = null;
                for (int guard = 0; guard < 4000 && spot == null; guard++)
                {
                    if (y + size.Height > usable.Bottom)
                    {
                        // column full: start the next column inward
                        colX = fromRight ? colX - colWidth - Gap : colX + colWidth + Gap;
                        colWidth = 0; y = usable.Top;
                        if (fromRight ? colX - size.Width < usable.Left : colX + size.Width > usable.Right) break;
                    }
                    var r = new Rect(fromRight ? colX - size.Width : colX, y, size.Width, size.Height);
                    if (Fits(r, usable, notch, taken)) spot = r;
                    else y += 8;
                }
                var final = spot ?? Nearest(new Rect(fromRight ? usable.Right - size.Width : usable.Left, usable.Top, size.Width, size.Height), usable, notch, taken) ?? new Rect(new Point(usable.Left, usable.Top), size);
                taken.Add(final);
                w.MoveCardTo(final.TopLeft);
                y = final.Bottom + Gap;
                colWidth = Math.Max(colWidth, size.Width);
            }
        }

        /// <summary>Closest free spot to the wanted rectangle (searched on a 16px grid).</summary>
        static Rect? Nearest(Rect want, Rect usable, Rect notch, List<Rect> taken)
        {
            Rect? best = null;
            double bestD = double.MaxValue;
            const double step = 16;
            for (double y = usable.Top; y + want.Height <= usable.Bottom; y += step)
                for (double x = usable.Left; x + want.Width <= usable.Right; x += step)
                {
                    double d = (x - want.X) * (x - want.X) + (y - want.Y) * (y - want.Y);
                    if (d >= bestD) continue;
                    var r = new Rect(x, y, want.Width, want.Height);
                    if (Fits(r, usable, notch, taken)) { best = r; bestD = d; }
                }
            return best;
        }

        /// <summary>Called after the user drags a widget: snap to the nearest free spot and remember it.</summary>
        public static void Dropped(WidgetWindow w)
        {
            var usable = Usable();
            var notch = NotchZone();
            var others = Open.Values.Where(o => o != w && o.ActualHeight > 0).Select(o => o.Card).ToList();
            var r = w.Card;
            if (!Fits(r, usable, notch, others))
            {
                var free = Nearest(r, usable, notch, others);
                if (free != null)
                {
                    var from = w.Card.TopLeft;
                    w.MoveCardTo(free.Value.TopLeft);
                }
            }
            var s = Config.Widget(w.Id);
            s.Manual = true; s.X = w.Left; s.Y = w.Top;
            Config.Save(false);
            RequestArrange(); // auto widgets re-flow around the moved one
        }
    }
}
