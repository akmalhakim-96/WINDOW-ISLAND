using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WindowIsland.Core;

namespace WindowIsland.Widgets
{
    /// <summary>Small helpers so widget code stays readable.</summary>
    static class UI
    {
        public static TextBlock Text(string s, double size = 13, bool bold = false, string brush = "WidgetFg", double opacity = 1)
        {
            var t = new TextBlock { Text = s, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Opacity = opacity, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        public static Border Bar(double fraction, out Border fill)
        {
            fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            fill.SetResourceReference(Border.BackgroundProperty, "Accent");
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 140)), Child = fill, Margin = new Thickness(0, 4, 0, 10) };
            return track;
        }

        public static readonly CultureInfo Ms = CreateMs();
        static CultureInfo CreateMs() { try { return new CultureInfo("ms-MY"); } catch { return CultureInfo.InvariantCulture; } }

        public static string Hijri(DateTime d)
        {
            try
            {
                var cal = new UmAlQuraCalendar();
                string[] months = { "Muharram", "Safar", "Rabiulawal", "Rabiulakhir", "Jamadilawal", "Jamadilakhir", "Rejab", "Syaaban", "Ramadan", "Syawal", "Zulkaedah", "Zulhijjah" };
                return $"{cal.GetDayOfMonth(d)} {months[cal.GetMonth(d) - 1]} {cal.GetYear(d)}H";
            }
            catch { return ""; }
        }

        public static string Time(DateTime t) => Config.Current.Use24h ? t.ToString("HH:mm") : t.ToString("h:mm tt", Ms);
    }

    /// <summary>Per-view timer that stops when the widget closes (no leaks).</summary>
    public abstract class TimedView : StackPanel, IDisposable
    {
        readonly DispatcherTimer _t;
        protected TimedView(TimeSpan every)
        {
            _t = new DispatcherTimer(DispatcherPriority.Background) { Interval = every };
            _t.Tick += (_, __) => Refresh();
            Loaded += (_, __) => { Refresh(); _t.Start(); };
        }
        protected abstract void Refresh();
        public virtual void Dispose() => _t.Stop();
    }

    // ------------------------------------------------------------------ TODAY & NOTES
    public class TodayView : TimedView
    {
        readonly TextBlock _day, _date, _hijri;
        readonly UniformGrid _grid = new() { Columns = 7, Margin = new Thickness(0, 10, 0, 12) };
        int _builtFor = -1;

        public TodayView() : base(TimeSpan.FromMinutes(1))
        {
            _day = UI.Text("", 12, true, "Accent");
            _date = UI.Text("", 26, true);
            _hijri = UI.Text("", 12, false, "WidgetSub");
            Children.Add(_day); Children.Add(_date); Children.Add(_hijri);
            Children.Add(_grid);

            Children.Add(UI.Text("NOTA", 11, true, "WidgetSub"));
            var notes = new TextBox
            {
                Text = Config.Current.Notes ?? "",
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                MinHeight = 70, MaxHeight = 220, FontSize = 13, Margin = new Thickness(0, 4, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            notes.SetResourceReference(TextBox.ForegroundProperty, "WidgetFg");
            notes.SetResourceReference(TextBox.CaretBrushProperty, "Accent");
            notes.TextChanged += (_, __) => { Config.Current.Notes = notes.Text; Config.Save(false); };
            Children.Add(notes);
        }

        protected override void Refresh()
        {
            var n = DateTime.Now;
            _day.Text = n.ToString("dddd", UI.Ms).ToUpper(UI.Ms);
            _date.Text = n.ToString("d MMMM yyyy", UI.Ms);
            _hijri.Text = UI.Hijri(n);
            if (_builtFor == n.DayOfYear) return;
            _builtFor = n.DayOfYear;
            _grid.Children.Clear();
            foreach (var h in new[] { "I", "S", "R", "K", "J", "S", "A" })
            {
                var t = UI.Text(h, 11, true, "WidgetSub"); t.HorizontalAlignment = HorizontalAlignment.Center; _grid.Children.Add(t);
            }
            var first = new DateTime(n.Year, n.Month, 1);
            int offset = ((int)first.DayOfWeek + 6) % 7;
            for (int i = 0; i < offset; i++) _grid.Children.Add(new Border());
            for (int d = 1; d <= DateTime.DaysInMonth(n.Year, n.Month); d++)
            {
                bool today = d == n.Day;
                var t = UI.Text(d.ToString(), 12, today, today ? "WidgetBg" : "WidgetFg");
                t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
                var cell = new Border { Width = 26, Height = 24, CornerRadius = new CornerRadius(12), Child = t, Margin = new Thickness(0, 1, 0, 1) };
                if (today) cell.SetResourceReference(Border.BackgroundProperty, "Accent");
                if (new DateTime(n.Year, n.Month, d).DayOfWeek == DayOfWeek.Sunday && !today) t.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                _grid.Children.Add(cell);
            }
        }
    }

    // ------------------------------------------------------------------ ANALOG CLOCK
    public class ClockView : TimedView
    {
        readonly RotateTransform _h = new(), _m = new(), _s = new();
        readonly TextBlock _digital;

        public ClockView() : base(TimeSpan.FromSeconds(1))
        {
            const double S = 150, C = S / 2;
            var canvas = new Canvas { Width = S, Height = S };
            var face = new Ellipse { Width = S, Height = S, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), StrokeThickness = 1 };
            canvas.Children.Add(face);
            for (int i = 1; i <= 12; i++)
            {
                double a = i * Math.PI / 6;
                var t = new TextBlock { Text = i.ToString(), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, Width = 22, TextAlignment = TextAlignment.Center };
                Canvas.SetLeft(t, C + Math.Sin(a) * 56 - 11);
                Canvas.SetTop(t, C - Math.Cos(a) * 56 - 10);
                canvas.Children.Add(t);
            }
            Line Hand(double len, double w, Brush b, RotateTransform rt)
            {
                var l = new Line { X1 = C, Y1 = C + 8, X2 = C, Y2 = C - len, Stroke = b, StrokeThickness = w, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = rt };
                rt.CenterX = C; rt.CenterY = C;
                return l;
            }
            var orange = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            canvas.Children.Add(Hand(36, 5, Brushes.Black, _h));
            canvas.Children.Add(Hand(54, 3.5, Brushes.Black, _m));
            canvas.Children.Add(Hand(60, 1.5, orange, _s));
            var hub = new Ellipse { Width = 8, Height = 8, Fill = orange };
            Canvas.SetLeft(hub, C - 4); Canvas.SetTop(hub, C - 4);
            canvas.Children.Add(hub);
            Children.Add(canvas);
            _digital = UI.Text("", 12, false, "WidgetSub");
            _digital.HorizontalAlignment = HorizontalAlignment.Center;
            _digital.Margin = new Thickness(0, 8, 0, 0);
            Children.Add(_digital);
        }

        protected override void Refresh()
        {
            var n = DateTime.Now;
            _s.Angle = n.Second * 6;
            _m.Angle = n.Minute * 6 + n.Second * 0.1;
            _h.Angle = (n.Hour % 12) * 30 + n.Minute * 0.5;
            _digital.Text = Config.Current.City;
        }
    }

    // ------------------------------------------------------------------ PRAYER TIMES (offline)
    public class PrayerView : TimedView
    {
        readonly TextBlock _next, _city;
        readonly StackPanel _rows = new() { Margin = new Thickness(0, 10, 0, 0) };

        public PrayerView() : base(TimeSpan.FromSeconds(30))
        {
            var head = new DockPanel();
            var icon = UI.Text("", 14, false, "Accent"); icon.FontFamily = (FontFamily)Application.Current.FindResource("IconFont");
            icon.Margin = new Thickness(0, 0, 8, 0);
            head.Children.Add(icon);
            head.Children.Add(UI.Text("Waktu Solat", 14, true));
            Children.Add(head);
            _city = UI.Text("", 12, false, "WidgetSub");
            _next = UI.Text("", 12, true, "Accent");
            _next.Margin = new Thickness(0, 6, 0, 0);
            Children.Add(_city); Children.Add(_next); Children.Add(_rows);
        }

        protected override void Refresh()
        {
            var c = Config.Current;
            var now = DateTime.Now;
            _city.Text = c.City + " · " + UI.Hijri(now);
            var next = PrayerTimes.Next(now, c.Lat, c.Lon);
            var left = next.Time - now;
            _next.Text = $"{next.Name} dalam {(left.TotalHours >= 1 ? $"{(int)left.TotalHours}j " : "")}{left.Minutes + (left.Seconds > 0 ? 1 : 0)}m";
            _rows.Children.Clear();
            foreach (var p in PrayerTimes.For(now, c.Lat, c.Lon))
            {
                bool isNext = p.Name == next.Name && p.Time.Date == next.Time.Date;
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var t = UI.Text(UI.Time(p.Time), 13, isNext); DockPanel.SetDock(t, Dock.Right);
                row.Children.Add(t);
                row.Children.Add(UI.Text(p.Name, 13, isNext, p.Name == "Syuruk" ? "WidgetSub" : "WidgetFg"));
                var box = new Border { Child = row, Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(10) };
                if (isNext) box.Background = new SolidColorBrush(Color.FromArgb(40, 139, 124, 255));
                _rows.Children.Add(box);
            }
        }
    }

    // ------------------------------------------------------------------ REMINDERS (timed)
    public class RemindersView : StackPanel, IDisposable
    {
        readonly StackPanel _list = new();
        readonly TextBlock _count;

        public RemindersView()
        {
            var head = new DockPanel();
            _count = UI.Text("0", 26, true); DockPanel.SetDock(_count, Dock.Right);
            head.Children.Add(_count);
            var title = UI.Text("Peringatan", 15, true, "Accent"); title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            Children.Add(head);

            var input = new TextBox
            {
                Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 140)), BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 8, 0, 6), FontSize = 13,
                ToolTip = "Tekan Enter. Tambah masa dengan @, cth: Mesyuarat @3pm atau Hantar fail @15:30",
            };
            input.SetResourceReference(TextBox.ForegroundProperty, "WidgetFg");
            input.SetResourceReference(TextBox.CaretBrushProperty, "Accent");
            var hint = UI.Text("+ Tambah… (cth: Mesyuarat @3pm)", 12, false, "WidgetSub");
            hint.IsHitTestVisible = false; hint.Margin = new Thickness(10, 0, 0, 0); hint.VerticalAlignment = VerticalAlignment.Center;
            var g = new Grid(); g.Children.Add(input); g.Children.Add(hint);
            input.TextChanged += (_, __) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            input.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
                var (text, due) = ReminderAlerts.Parse(input.Text);
                Config.Current.Reminders.Insert(0, new ReminderItem { Text = text, Due = due });
                input.Clear();
                Config.Save(false);
                Build();
            };
            Children.Add(g);
            Children.Add(new ScrollViewer { Content = _list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Build();
        }

        void Build()
        {
            _list.Children.Clear();
            var items = Config.Current.Reminders;
            _count.Text = items.Count(r => !r.Done).ToString();
            foreach (var r in items.OrderBy(r => r.Done).ToList())
            {
                var item = r;
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var del = new Button { Content = "", FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 10, Style = (Style)Application.Current.FindResource("Flat"), Opacity = 0.5, ToolTip = "Padam" };
                del.SetResourceReference(Button.ForegroundProperty, "WidgetSub");
                del.Click += (_, __) => { Config.Current.Reminders.Remove(item); Config.Save(false); Build(); };
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                var cb = new CheckBox { IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                cb.Checked += (_, __) => { item.Done = true; Config.Save(false); Build(); };
                cb.Unchecked += (_, __) => { item.Done = false; Config.Save(false); Build(); };
                row.Children.Add(cb);
                var sp = new StackPanel();
                var t = UI.Text(item.Text, 13, false, item.Done ? "WidgetSub" : "WidgetFg");
                if (item.Done) t.TextDecorations = TextDecorations.Strikethrough;
                sp.Children.Add(t);
                if (item.Due != null) sp.Children.Add(UI.Text("⏰ " + item.Due.Value.ToString(item.Due.Value.Date == DateTime.Today ? "h:mm tt" : "d MMM, h:mm tt", UI.Ms), 11, false, "Accent"));
                row.Children.Add(sp);
                _list.Children.Add(row);
            }
        }

        public void Dispose() { }
    }

    // ------------------------------------------------------------------ SYSTEM
    public class SystemView : StackPanel, IDisposable
    {
        readonly TextBlock _cpu, _ram, _disk, _net;
        readonly Border _cpuFill, _ramFill, _diskFill;
        const double BarW = 198;

        public SystemView()
        {
            Children.Add(UI.Text("Prestasi Sistem", 14, true));
            Children.Add(new Border { Height = 8 });
            _cpu = UI.Text("CPU", 12); Children.Add(_cpu); Children.Add(UI.Bar(0, out _cpuFill));
            _ram = UI.Text("RAM", 12); Children.Add(_ram); Children.Add(UI.Bar(0, out _ramFill));
            _disk = UI.Text("Cakera C:", 12); Children.Add(_disk); Children.Add(UI.Bar(0, out _diskFill));
            _net = UI.Text("", 12, false, "WidgetSub"); Children.Add(_net);
            SystemStats.Updated += Update;
            Update();
        }

        void Update()
        {
            _cpu.Text = $"CPU  {SystemStats.Cpu:0}%";
            _cpuFill.Width = BarW * SystemStats.Cpu / 100;
            _ram.Text = $"RAM  {SystemStats.Ram:0}%";
            _ramFill.Width = BarW * SystemStats.Ram / 100;
            try
            {
                var d = new DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory));
                double used = 1 - (double)d.AvailableFreeSpace / d.TotalSize;
                _disk.Text = $"Cakera {d.Name.TrimEnd('\\')}  {d.AvailableFreeSpace / 1e9:0} GB kosong";
                _diskFill.Width = BarW * used;
            }
            catch { }
            _net.Text = "↓ " + SystemStats.Speed(SystemStats.DownBps) + "    ↑ " + SystemStats.Speed(SystemStats.UpBps);
        }

        public void Dispose() => SystemStats.Updated -= Update;
    }

    // ------------------------------------------------------------------ WEATHER (Open-Meteo, no API key)
    public class WeatherView : TimedView
    {
        readonly TextBlock _city, _temp, _desc, _status;
        readonly UniformGrid _hours = new() { Columns = 6, Margin = new Thickness(0, 12, 0, 8) };
        readonly StackPanel _days = new();

        public WeatherView() : base(TimeSpan.FromMinutes(30))
        {
            _city = UI.Text("", 14, true);
            _temp = UI.Text("--°", 44); _temp.FontWeight = FontWeights.Light;
            _desc = UI.Text("", 13, false, "WidgetSub");
            _status = UI.Text("Memuatkan cuaca…", 12, false, "WidgetSub");
            Children.Add(_city); Children.Add(_temp); Children.Add(_desc); Children.Add(_hours); Children.Add(_days); Children.Add(_status);
        }

        protected override async void Refresh()
        {
            var c = Config.Current;
            _city.Text = c.City;
            try
            {
                string url = FormattableString.Invariant(
                    $"https://api.open-meteo.com/v1/forecast?latitude={c.Lat}&longitude={c.Lon}&current=temperature_2m,weather_code&hourly=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=5");
                string json = await UpdateChecker.Http.GetStringAsync(url);
                Render(json);
                _status.Visibility = Visibility.Collapsed;
            }
            catch
            {
                _status.Text = "Tiada sambungan — cuba lagi nanti";
                _status.Visibility = Visibility.Visible;
            }
        }

        void Render(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cur = root.GetProperty("current");
            int code = cur.GetProperty("weather_code").GetInt32();
            var (icon, text) = Describe(code);
            _temp.Text = $"{cur.GetProperty("temperature_2m").GetDouble():0}°";
            var daily = root.GetProperty("daily");
            double hi = daily.GetProperty("temperature_2m_max")[0].GetDouble(), lo = daily.GetProperty("temperature_2m_min")[0].GetDouble();
            _desc.Text = $"{icon} {text}  ·  ↓{lo:0}° ↑{hi:0}°";

            _hours.Children.Clear();
            var hourly = root.GetProperty("hourly");
            var times = hourly.GetProperty("time");
            int start = 0;
            var now = DateTime.Now;
            for (int i = 0; i < times.GetArrayLength(); i++)
                if (DateTime.Parse(times[i].GetString(), CultureInfo.InvariantCulture) >= now.AddMinutes(-30)) { start = i; break; }
            for (int i = start; i < start + 6 && i < times.GetArrayLength(); i++)
            {
                var t = DateTime.Parse(times[i].GetString(), CultureInfo.InvariantCulture);
                var col = new StackPanel();
                foreach (var tb in new[] {
                    UI.Text(t.ToString("HH"), 11, false, "WidgetSub"),
                    UI.Text(Describe(hourly.GetProperty("weather_code")[i].GetInt32()).Icon, 15),
                    UI.Text($"{hourly.GetProperty("temperature_2m")[i].GetDouble():0}°", 12, true) })
                { tb.HorizontalAlignment = HorizontalAlignment.Center; col.Children.Add(tb); }
                _hours.Children.Add(col);
            }

            _days.Children.Clear();
            var dTimes = daily.GetProperty("time");
            for (int i = 1; i < dTimes.GetArrayLength(); i++)
            {
                var d = DateTime.Parse(dTimes[i].GetString(), CultureInfo.InvariantCulture);
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var name = UI.Text(d.ToString("dddd", UI.Ms), 12);
                var ic = UI.Text(Describe(daily.GetProperty("weather_code")[i].GetInt32()).Icon, 13); Grid.SetColumn(ic, 1);
                var range = UI.Text($"{daily.GetProperty("temperature_2m_min")[i].GetDouble():0}°  –  {daily.GetProperty("temperature_2m_max")[i].GetDouble():0}°", 12, false, "WidgetSub");
                range.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(range, 2);
                row.Children.Add(name); row.Children.Add(ic); row.Children.Add(range);
                _days.Children.Add(row);
            }
        }

        static (string Icon, string Text) Describe(int code) => code switch
        {
            0 => ("☀", "Cerah"),
            1 or 2 => ("⛅", "Sebahagian berawan"),
            3 => ("☁", "Mendung"),
            45 or 48 => ("🌫", "Berkabus"),
            >= 51 and <= 57 => ("🌦", "Gerimis"),
            >= 61 and <= 67 => ("🌧", "Hujan"),
            >= 71 and <= 77 => ("❄", "Salji"),
            >= 80 and <= 82 => ("🌦", "Hujan lebat sekejap"),
            >= 95 => ("⛈", "Ribut petir"),
            _ => ("☁", "Berawan"),
        };
    }
}
