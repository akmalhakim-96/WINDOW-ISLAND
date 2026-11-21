using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WindowIsland.Core;
using WindowIsland.Shell;
using WindowIsland.Widgets;
using WinForms = System.Windows.Forms;

namespace WindowIsland
{
    public partial class App : Application
    {
        public static App Instance => (App)Current;
        public static readonly MediaService Media = new();

        static Mutex _mutex;
        EventWaitHandle _signal;
        WinForms.NotifyIcon _tray;
        TopBarWindow _bar;
        IslandWindow _island;
        DockWindow _dock;
        SettingsWindow _settings;
        bool _cleanedUp;

        public IslandWindow Island => _island;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool quit = e.Args.Contains("--quit"), uninstall = e.Args.Contains("--uninstall");
            _mutex = new Mutex(true, "WindowIsland.SingleInstance.v1", out bool first);
            if (quit || uninstall)
            {
                // Used by the installer/uninstaller: close the running copy gracefully so the taskbar is restored.
                if (!first)
                {
                    try { EventWaitHandle.OpenExisting("WindowIsland.Quit.v1").Set(); } catch { }
                    try { _mutex.WaitOne(TimeSpan.FromSeconds(6)); } catch (AbandonedMutexException) { }
                }
                Config.Load();
                SystemControl.ApplyTaskbar(false);
                if (uninstall) SystemControl.ApplyAutostart(false);
                Shutdown();
                return;
            }
            if (!first)
            {
                try { EventWaitHandle.OpenExisting("WindowIsland.Show.v1").Set(); } catch { }
                Shutdown();
                return;
            }
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, "WindowIsland.Show.v1");
            ThreadPool.RegisterWaitForSingleObject(_signal, (_, __) => Dispatcher.BeginInvoke(ShowSettings), null, -1, false);
            var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "WindowIsland.Quit.v1");
            ThreadPool.RegisterWaitForSingleObject(quitSignal, (_, __) => Dispatcher.BeginInvoke(Quit), null, -1, true);

            base.OnStartup(e);
            DispatcherUnhandledException += (_, ex) => { Log.Error("UI", ex.Exception); ex.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (_, ex) => { Log.Error("Fatal", ex.ExceptionObject as Exception); Cleanup(); };
            TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("Task", ex.Exception); ex.SetObserved(); };
            SessionEnding += (_, __) => Cleanup();

            Config.Load();
            Log.Info("Mula v" + UpdateChecker.CurrentVersionText);
            ThemeManager.Apply(Config.Current.Theme);

            WindowTracker.Start();
            FullscreenWatcher.Start();
            SystemStats.Start();
            PrayerAlerts.Start();
            PrivacyMonitor.Start();
            ReminderAlerts.Start();
            _ = Media.StartAsync();
            if (Config.Current.ShowNotifications) _ = NotificationService.StartAsync();

            NotificationService.Received += a => Dispatcher.BeginInvoke(() => _island?.ShowAlert(a));
            PrayerAlerts.Due += a => _island?.ShowAlert(a);
            ReminderAlerts.Due += a => _island?.ShowAlert(a);

            ApplyLayout();
            SystemControl.ApplyAutostart(Config.Current.StartWithWindows);
            CreateTray();

            if (!Config.Current.FirstRunDone)
            {
                Config.Current.FirstRunDone = true;
                Config.SaveNow();
                ShowSettings();
            }

            // Hand idle memory back to Windows once everything is up.
            var trim = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            trim.Tick += (_, __) => { trim.Interval = TimeSpan.FromMinutes(10); SystemControl.TrimMemory(); };
            trim.Start();

            if (Config.Current.CheckUpdates) _ = CheckUpdateLater();
        }

        /// <summary>Creates/destroys shell windows to match the selected layout.</summary>
        public void ApplyLayout()
        {
            var c = Config.Current;
            // Each part is isolated: if one fails, the others still appear.
            Safe("Bar", () =>
            {
                if (c.FullBar) { if (_bar == null) { _bar = new TopBarWindow(); _bar.Show(); } }
                else { _bar?.CloseBar(); _bar = null; }
            });
            Safe("Island", () =>
            {
                if (c.ShowTopBar) { if (_island == null) { _island = new IslandWindow(); _island.Show(); } _island.ApplyMode(); }
                else { _island?.Close(); _island = null; }
            });
            Safe("Dock", () =>
            {
                if (c.ShowDock) { if (_dock == null) { _dock = new DockWindow(); _dock.Show(); } _dock.Rebuild(); }
                else { _dock?.Close(); _dock = null; }
            });
            Safe("Taskbar", () => SystemControl.ApplyTaskbar(c.HideTaskbar && c.ShowDock));
            Safe("Widgets", WidgetManager.Apply);
        }

        static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { Log.Error(what, ex); }
        }

        public void RefreshDock() { _dock?.Rebuild(); WidgetManager.RequestArrange(); }

        /// <summary>Explorer restarted: re-hide the taskbar and re-dock the bar.</summary>
        public void OnTaskbarCreated()
        {
            SystemControl.ApplyTaskbar(Config.Current.HideTaskbar && Config.Current.ShowDock);
        }

        public void ShowSettings()
        {
            if (_settings == null || !_settings.IsLoaded)
            {
                _settings = new SettingsWindow();
                _settings.Closed += (_, __) => _settings = null;
                _settings.Show();
            }
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
        }

        void CreateTray()
        {
            _tray = new WinForms.NotifyIcon { Text = "Window Island", Visible = true };
            try
            {
                using var s = GetResourceStream(new Uri("pack://application:,,,/icon.ico")).Stream;
                _tray.Icon = new System.Drawing.Icon(s);
            }
            catch { _tray.Icon = System.Drawing.SystemIcons.Application; }

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Tetapan…", null, (_, __) => ShowSettings());
            var layout = new WinForms.ToolStripMenuItem("Susun atur");
            foreach (var (key, label) in LayoutNames)
            {
                var item = new WinForms.ToolStripMenuItem(label) { Tag = key };
                item.Click += (_, __) => { Config.Current.Layout = key; Config.Save(); ApplyLayout(); };
                layout.DropDownItems.Add(item);
            }
            layout.DropDownOpening += (_, __) =>
            {
                foreach (WinForms.ToolStripMenuItem i in layout.DropDownItems) i.Checked = (string)i.Tag == Config.Current.Layout;
            };
            menu.Items.Add(layout);
            menu.Items.Add("Tunjuk / sembunyi widget", null, (_, __) => { Config.Current.ShowWidgets = !Config.Current.ShowWidgets; Config.Save(); WidgetManager.Apply(); });
            menu.Items.Add("Semak kemas kini", null, async (_, __) => await CheckUpdateNow(true));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Keluar", null, (_, __) => Quit());
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (_, ev) => { if (ev.Button == WinForms.MouseButtons.Left) ShowSettings(); };
        }

        public static readonly (string Key, string Label)[] LayoutNames =
        {
            ("NotchDock", "Notch + Dock"),
            ("BarDock", "Bar penuh + Dock"),
            ("NotchOnly", "Notch sahaja"),
            ("DockOnly", "Dock sahaja"),
        };

        async Task CheckUpdateLater()
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            await CheckUpdateNow(false);
        }

        public async Task CheckUpdateNow(bool manual)
        {
            string tag = await UpdateChecker.CheckAsync();
            if (tag != null)
            {
                _island?.ShowAlert(new IslandAlert { App = "Kemas kini", Title = "Window Island " + tag + " tersedia", Body = "Buka Tetapan → Tentang untuk muat turun", Glyph = "", Seconds = 10 });
                if (manual && MessageBox.Show($"Versi baharu {tag} tersedia. Buka halaman muat turun?", "Window Island", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    SystemControl.Launch(UpdateChecker.ReleasesPage);
            }
            else if (manual) MessageBox.Show("Anda sudah guna versi terkini (v" + UpdateChecker.CurrentVersionText + ").", "Window Island");
        }

        public void Quit()
        {
            Cleanup();
            Shutdown();
        }

        /// <summary>Always give the screen back exactly as we found it.</summary>
        void Cleanup()
        {
            if (_cleanedUp) return;
            _cleanedUp = true;
            try { _bar?.CloseBar(); } catch { }
            try { SystemControl.ApplyTaskbar(false); } catch { }
            try { WindowTracker.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
            try { Config.SaveNow(); } catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Cleanup();
            base.OnExit(e);
        }
    }
}
