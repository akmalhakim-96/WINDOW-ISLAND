using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WindowIsland.Core;
using WindowIsland.Widgets;

namespace WindowIsland
{
    public partial class SettingsWindow : Window
    {
        static readonly (string Glyph, string Name)[] Pages =
        {
            ("", "Susun Atur"),
            ("", "Penampilan"),
            ("", "Dock"),
            ("", "Widget"),
            ("", "Lokasi & Solat"),
            ("", "Umum"),
            ("", "Tentang"),
        };

        static AppConfig C => Config.Current;

        public SettingsWindow()
        {
            InitializeComponent();
            VersionText.Text = "v" + UpdateChecker.CurrentVersionText + " · Percuma";
            foreach (var (g, n) in Pages)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = g, FontFamily = (FontFamily)FindResource("IconFont"), Width = 28, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = n });
                Nav.Items.Add(new ListBoxItem { Content = sp, Tag = n });
            }
            Nav.SelectedIndex = 0;
        }

        void Nav_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            Page.Children.Clear();
            switch ((Nav.SelectedItem as ListBoxItem)?.Tag as string)
            {
                case "Susun Atur": LayoutPage(); break;
                case "Penampilan": ThemePage(); break;
                case "Dock": DockPage(); break;
                case "Widget": WidgetPage(); break;
                case "Lokasi & Solat": LocationPage(); break;
                case "Umum": GeneralPage(); break;
                case "Tentang": AboutPage(); break;
            }
        }

        // ---------------- helpers ----------------
        void H1(string t, string sub = null)
        {
            Page.Children.Add(new TextBlock { Text = t, FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            if (sub != null) Page.Children.Add(new TextBlock { Text = sub, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        }
        void H2(string t) => Page.Children.Add(new TextBlock { Text = t, FontSize = 13, FontWeight = FontWeights.SemiBold, Opacity = 0.55, Margin = new Thickness(0, 18, 0, 6) });
        void Note(string t) => Page.Children.Add(new TextBlock { Text = t, FontSize = 12, Opacity = 0.55, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });

        CheckBox Toggle(string label, bool value, Action<bool> set, string note = null)
        {
            var cb = new CheckBox { IsChecked = value };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label });
            if (note != null) sp.Children.Add(new TextBlock { Text = note, FontSize = 12, Opacity = 0.55, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 });
            cb.Content = sp;
            cb.Checked += (_, __) => { set(true); Config.Save(); };
            cb.Unchecked += (_, __) => { set(false); Config.Save(); };
            Page.Children.Add(cb);
            return cb;
        }

        Button Btn(string text, Action a, bool primary = false)
        {
            var b = new Button { Content = text, Style = (Style)FindResource(primary ? "Primary" : "Secondary") };
            b.Click += (_, __) => a();
            return b;
        }

        void Row(params UIElement[] els)
        {
            var wp = new WrapPanel();
            foreach (var e in els) wp.Children.Add(e);
            Page.Children.Add(wp);
        }

        // ---------------- pages ----------------
        void LayoutPage()
        {
            H1("Susun Atur", "Pilih apa yang anda mahu. Anda boleh tukar bila-bila masa — perubahan berlaku serta-merta.");
            var grid = new UniformGridLite(2);
            foreach (var (key, label) in App.LayoutNames)
            {
                bool selected = C.Layout == key;
                var preview = LayoutPreview(key);
                var sp = new StackPanel();
                sp.Children.Add(preview);
                sp.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
                var card = new Border
                {
                    Child = sp, Padding = new Thickness(16), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(16), Cursor = System.Windows.Input.Cursors.Hand,
                    Background = new SolidColorBrush(Color.FromArgb(selected ? (byte)40 : (byte)16, 255, 255, 255)),
                    BorderThickness = new Thickness(2),
                    BorderBrush = selected ? (Brush)FindResource("Accent") : Brushes.Transparent,
                };
                card.MouseLeftButtonUp += (_, __) => { C.Layout = key; Config.Save(); App.Instance.ApplyLayout(); LayoutPage2(); };
                grid.Add(card);
            }
            Page.Children.Add(grid);

            H2("TASKBAR WINDOWS");
            Toggle("Ganti taskbar Windows dengan Dock", C.HideTaskbar, v => { C.HideTaskbar = v; App.Instance.ApplyLayout(); },
                "Taskbar disembunyikan selagi Window Island berjalan, dan dipulihkan sepenuhnya bila anda keluar. Kekunci Windows ⊞ masih membuka menu Start.");
        }

        void LayoutPage2() { Page.Children.Clear(); LayoutPage(); }

        static FrameworkElement LayoutPreview(string key)
        {
            var g = new Grid { Width = 180, Height = 104 };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), Background = new LinearGradientBrush(Color.FromRgb(60, 50, 110), Color.FromRgb(30, 70, 110), 45) });
            if (key == "BarDock")
                g.Children.Add(new Border { Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 2, 2, 0), CornerRadius = new CornerRadius(8, 8, 0, 0), Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)) });
            if (key != "DockOnly")
                g.Children.Add(new Border { Width = 46, Height = key == "BarDock" ? 8 : 11, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, key == "BarDock" ? 3 : 2, 0, 0), CornerRadius = key == "BarDock" ? new CornerRadius(4) : new CornerRadius(0, 0, 6, 6), Background = Brushes.Black });
            if (key != "NotchOnly")
            {
                var dock = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
                var bg = new Border { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), Padding = new Thickness(4), Child = dock, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 7) };
                foreach (var col in new[] { "#FFF59E0B", "#FF3B82F6", "#FF22C55E", "#FFA855F7", "#FFEF4444" })
                    dock.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Margin = new Thickness(1.5, 0, 1.5, 0), Background = ThemeManager.Brush(col) });
                g.Children.Add(bg);
            }
            return g;
        }

        void ThemePage()
        {
            H1("Penampilan", "Tema berlaku pada widget, dock dan bar. Notch kekal hitam seperti telefon.");
            H2("TEMA WARNA");
            var wp = new WrapPanel();
            foreach (var t in ThemeManager.Themes)
            {
                bool sel = C.Theme == t.Name;
                var swatch = new Grid { Width = 110, Height = 70 };
                swatch.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = ThemeManager.Brush(t.WidgetBg), BorderBrush = sel ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), BorderThickness = new Thickness(sel ? 3 : 1) });
                swatch.Children.Add(new Ellipse { Width = 18, Height = 18, Fill = ThemeManager.Brush(t.Accent), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12) });
                swatch.Children.Add(new TextBlock { Text = t.Name, Foreground = ThemeManager.Brush(t.WidgetFg), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 10) });
                var b = new Border { Child = swatch, Margin = new Thickness(0, 0, 10, 10), Cursor = System.Windows.Input.Cursors.Hand };
                var name = t.Name;
                b.MouseLeftButtonUp += (_, __) => { C.Theme = name; ThemeManager.Apply(name); Config.Save(); App.Instance.RefreshDock(); Page.Children.Clear(); ThemePage(); };
                wp.Children.Add(b);
            }
            Page.Children.Add(wp);
            H2("BAR ATAS");
            Toggle("Kesan kaca kabur pada bar penuh", C.TopBarBlur, v => C.TopBarBlur = v, "Berlaku selepas susun atur ditukar atau app dimulakan semula.");
            Toggle("Jam 24 jam", C.Use24h, v => C.Use24h = v);
        }

        void DockPage()
        {
            H1("Dock", "Klik ikon untuk buka atau tukar ke app. Klik lagi untuk minimumkan. Klik kanan untuk pilihan. Seret fail .exe atau pintasan ke dock untuk tambah.");
            H2("SAIZ IKON");
            var slider = new Slider { Minimum = 32, Maximum = 72, Value = C.DockIconSize, TickFrequency = 4, IsSnapToTickEnabled = true, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
            var label = new TextBlock { Text = $"{C.DockIconSize:0} px", Opacity = 0.6 };
            slider.ValueChanged += (_, e) => { C.DockIconSize = e.NewValue; label.Text = $"{e.NewValue:0} px"; Config.Save(); App.Instance.RefreshDock(); };
            Page.Children.Add(slider); Page.Children.Add(label);
            H2("TINGKAH LAKU");
            Toggle("Sembunyi pintar", C.DockSmartHide, v => C.DockSmartHide = v, "Dock turun bila tetingkap menutupinya; gerakkan tetikus ke bawah skrin untuk munculkan semula.");
            Toggle("Tunjuk CPU, RAM & kelajuan internet", C.ShowDockStats, v => { C.ShowDockStats = v; App.Instance.RefreshDock(); });
            H2("APP DALAM DOCK");
            foreach (var item in C.DockItems.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var del = Btn("Buang", () => { C.DockItems.Remove(item); Config.Save(); App.Instance.RefreshDock(); Page.Children.Clear(); DockPage(); });
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                var img = new Image { Source = IconCache.Get(item.Path), Width = 24, Height = 24, Margin = new Thickness(0, 0, 10, 0) };
                row.Children.Add(img);
                row.Children.Add(new TextBlock { Text = item.Name ?? item.Path, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                Page.Children.Add(row);
            }
            Row(
                Btn("+ Tambah app…", () =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "App & pintasan|*.exe;*.lnk;*.url|Semua fail|*.*", Multiselect = true };
                    if (dlg.ShowDialog(this) != true) return;
                    foreach (var f in dlg.FileNames) C.DockItems.Add(new DockItem { Name = System.IO.Path.GetFileNameWithoutExtension(f), Path = f });
                    Config.Save(); App.Instance.RefreshDock(); Page.Children.Clear(); DockPage();
                }, true),
                Btn("Set semula ke lalai", () => { C.DockItems = Config.DefaultDock(); Config.Save(); App.Instance.RefreshDock(); Page.Children.Clear(); DockPage(); }));
        }

        void WidgetPage()
        {
            H1("Widget", "Widget duduk di desktop, kekal bila anda tekan Win+D, dan boleh diseret ke mana-mana.");
            Toggle("Tunjuk widget di desktop", C.ShowWidgets, v => { C.ShowWidgets = v; WidgetManager.Apply(); });
            H2("PILIH WIDGET");
            foreach (var (id, name, _) in WidgetManager.All)
            {
                var wid = id;
                Toggle(name, WidgetManager.IsOn(id), v => { Config.Widget(wid).Visible = v; WidgetManager.Apply(); });
            }
            Row(Btn("Susun semula kedudukan", WidgetManager.ResetPositions));
            Note("Tip: dalam widget Peringatan, tulis masa dengan @ — cth. “Hantar laporan @4:30pm” — dan Window Island akan ingatkan anda di notch.");
        }

        void LocationPage()
        {
            H1("Lokasi & Solat", "Digunakan untuk cuaca dan waktu solat. Waktu solat dikira terus dalam komputer — tidak perlu internet.");
            var current = new TextBlock { Text = $"Lokasi semasa: {C.City} ({C.Lat:0.###}, {C.Lon:0.###})", Margin = new Thickness(0, 0, 0, 10) };
            Page.Children.Add(current);
            var box = new TextBox { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
            var results = new StackPanel();
            async void Search()
            {
                results.Children.Clear();
                if (string.IsNullOrWhiteSpace(box.Text)) return;
                results.Children.Add(new TextBlock { Text = "Mencari…", Opacity = 0.6 });
                try
                {
                    string json = await UpdateChecker.Http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?count=6&language=ms&name=" + Uri.EscapeDataString(box.Text.Trim()));
                    results.Children.Clear();
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.GetArrayLength() == 0)
                    { results.Children.Add(new TextBlock { Text = "Tiada keputusan.", Opacity = 0.6 }); return; }
                    foreach (var r in arr.EnumerateArray())
                    {
                        string name = r.GetProperty("name").GetString();
                        string region = string.Join(", ", new[] { r.TryGetProperty("admin1", out var a) ? a.GetString() : null, r.TryGetProperty("country", out var c) ? c.GetString() : null }.Where(x => !string.IsNullOrEmpty(x)));
                        double lat = r.GetProperty("latitude").GetDouble(), lon = r.GetProperty("longitude").GetDouble();
                        var b = Btn(name + (region.Length > 0 ? " — " + region : ""), () =>
                        {
                            C.City = name; C.Lat = lat; C.Lon = lon; Config.Save();
                            WidgetManager.Apply();
                            Page.Children.Clear(); LocationPage();
                        });
                        b.HorizontalAlignment = HorizontalAlignment.Left;
                        results.Children.Add(b);
                    }
                }
                catch { results.Children.Clear(); results.Children.Add(new TextBlock { Text = "Tiada sambungan internet.", Opacity = 0.6 }); }
            }
            box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Search(); };
            Row(box, Btn("Cari bandar", Search, true));
            Page.Children.Add(results);

            H2("WAKTU SOLAT HARI INI");
            foreach (var p in PrayerTimes.For(DateTime.Now, C.Lat, C.Lon))
                Page.Children.Add(new TextBlock { Text = $"{p.Name,-10} {p.Time:h:mm tt}", FontFamily = new FontFamily("Consolas, Cascadia Mono"), Margin = new Thickness(0, 1, 0, 1) });
            Note("Kaedah: sudut Subuh 20°, Isyak 18°, Asar Syafie, ditambah 2 minit ihtiyat (seperti JAKIM). Mungkin berbeza ±1–2 minit daripada jadual rasmi zon anda.");
            Toggle("Makluman di notch bila masuk waktu solat", C.PrayerAlerts, v => C.PrayerAlerts = v);
        }

        void GeneralPage()
        {
            H1("Umum");
            Toggle("Mula bersama Windows", C.StartWithWindows, v => { C.StartWithWindows = v; SystemControl.ApplyAutostart(v); });
            Toggle("Tunjuk notifikasi app (WhatsApp, Discord…) di notch", C.ShowNotifications, v =>
            {
                C.ShowNotifications = v;
                if (v && !NotificationService.Available) _ = NotificationService.StartAsync();
            }, NotificationService.Available ? "Akses notifikasi: dibenarkan." : "Windows perlu benarkan akses notifikasi. Jika tiada apa berlaku, ciri ini belum disokong pada versi Windows anda.");
            Toggle("Semak kemas kini secara automatik", C.CheckUpdates, v => C.CheckUpdates = v);
            H2("PENYELENGGARAAN");
            Row(
                Btn("Buka folder data", () => Process.Start(new ProcessStartInfo(Config.Dir) { UseShellExecute = true })),
                Btn("Mula semula Window Island", () => { Process.Start(Environment.ProcessPath); App.Instance.Quit(); }),
                Btn("Keluar", () => App.Instance.Quit()));
            Note("Semua data (tetapan, nota, peringatan) disimpan hanya dalam komputer anda di %AppData%\\WindowIsland. Tiada akaun, tiada penjejakan.");
        }

        void AboutPage()
        {
            H1("Window Island", "Jadikan Windows lebih ringan, kemas dan futuristik.");
            Page.Children.Add(new TextBlock { Text = "Versi " + UpdateChecker.CurrentVersionText + " · Pakej Percuma", Margin = new Thickness(0, 0, 0, 12) });
            Row(
                Btn("Semak kemas kini", async () => await App.Instance.CheckUpdateNow(true), true),
                Btn("Laman muat turun", () => SystemControl.Launch(UpdateChecker.ReleasesPage)),
                Btn("Lapor masalah", () => SystemControl.Launch("https://github.com/" + UpdateChecker.Repo + "/issues/new")));
            H2("PAKEJ");
            Note("Free: notch, dock, widget, waktu solat, muzik — percuma selamanya.\nPlus & Pro: ciri tambahan akan datang. Bayar sekali, guna selamanya.");
        }
    }

    /// <summary>Simple N-column wrap grid (avoids UniformGrid sizing quirks inside ScrollViewer).</summary>
    class UniformGridLite : Grid
    {
        readonly int _cols;
        int _n;
        public UniformGridLite(int cols)
        {
            _cols = cols;
            for (int i = 0; i < cols; i++) ColumnDefinitions.Add(new ColumnDefinition());
        }
        public void Add(UIElement e)
        {
            if (_n % _cols == 0) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SetRow(e, _n / _cols);
            SetColumn(e, _n % _cols);
            Children.Add(e);
            _n++;
        }
    }
}
