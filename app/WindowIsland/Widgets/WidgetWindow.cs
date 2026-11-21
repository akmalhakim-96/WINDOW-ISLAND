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
                SavePosition();
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

        void SavePosition()
        {
            var s = Config.Widget(Id);
            s.X = Left; s.Y = Top;
            Config.Save(false);
        }
    }

    public static class WidgetManager
    {
        public static readonly (string Id, string Name, bool DefaultOn)[] All =
        {
            ("today", "Hari Ini & Nota", true),
            ("weather", "Cuaca", true),
            ("clock", "Jam Analog", true),
            ("prayer", "Waktu Solat", true),
            ("reminders", "Peringatan", true),
            ("system", "Prestasi Sistem", false),
        };

        static readonly Dictionary<string, WidgetWindow> Open = new();

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
            foreach (var (id, _, _) in All)
            {
                bool want = Config.Current.ShowWidgets && IsOn(id);
                if (want && !Open.ContainsKey(id))
                {
                    try
                    {
                        var w = Create(id);
                        Place(w);
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
        }

        public static void ResetPositions()
        {
            foreach (var s in Config.Current.Widgets.Values) { s.X = double.NaN; s.Y = double.NaN; }
            foreach (var w in Open.Values.ToList()) Place(w);
            Config.Save(false);
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

        static void Place(WidgetWindow w)
        {
            var s = Config.Widget(w.Id);
            var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (!double.IsNaN(s.X) && !double.IsNaN(s.Y) && vs.Contains(new Point(s.X + 40, s.Y + 40)))
            {
                w.Left = s.X; w.Top = s.Y; return;
            }
            var wa = SystemParameters.WorkArea;
            double top = wa.Top + (Config.Current.ShowTopBar ? 44 : 12);
            (double x, double y) = w.Id switch
            {
                "weather" => (wa.Left + 12, top),
                "clock" => (wa.Left + 12, top + 290),
                "reminders" => (wa.Left + 186, top + 290),
                "today" => (wa.Right - 326, top),
                "prayer" => (wa.Right - 266, top + 400),
                "system" => (wa.Right - 530, top),
                _ => (wa.Left + 40, top),
            };
            w.Left = x; w.Top = y;
        }
    }
}
