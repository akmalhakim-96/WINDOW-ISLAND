using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace WindowIsland.Core
{
    /// <summary>Now-playing info from any app (Spotify, YouTube in browser, etc.) via Windows media session.</summary>
    public class MediaService
    {
        GlobalSystemMediaTransportControlsSessionManager _mgr;
        GlobalSystemMediaTransportControlsSession _session;

        public string Title { get; private set; }
        public string Artist { get; private set; }
        public bool IsPlaying { get; private set; }
        public BitmapImage Art { get; private set; }
        public bool HasMedia => !string.IsNullOrEmpty(Title);

        /// <summary>Raised on any thread; consumers must marshal to UI.</summary>
        public event Action Updated;

        public async Task StartAsync()
        {
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.CurrentSessionChanged += (_, __) => Attach();
                Attach();
            }
            catch (Exception ex) { Log.Error("Media service tidak tersedia", ex); }
        }

        void Attach()
        {
            try
            {
                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnProps;
                    _session.PlaybackInfoChanged -= OnPlayback;
                }
                _session = _mgr?.GetCurrentSession();
                if (_session != null)
                {
                    _session.MediaPropertiesChanged += OnProps;
                    _session.PlaybackInfoChanged += OnPlayback;
                }
                _ = RefreshAsync();
            }
            catch (Exception ex) { Log.Error("Media attach", ex); }
        }

        void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshAsync();
        void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ = RefreshAsync();

        async Task RefreshAsync()
        {
            try
            {
                var s = _session;
                if (s == null) { Title = Artist = null; IsPlaying = false; Art = null; Updated?.Invoke(); return; }
                var props = await s.TryGetMediaPropertiesAsync();
                var pb = s.GetPlaybackInfo();
                IsPlaying = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                string newTitle = props?.Title;
                bool trackChanged = newTitle != Title;
                Title = newTitle;
                Artist = props?.Artist;
                if (trackChanged)
                {
                    Art = null;
                    if (props?.Thumbnail != null)
                    {
                        using var ras = await props.Thumbnail.OpenReadAsync();
                        using var stream = ras.AsStreamForRead();
                        var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        ms.Position = 0;
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 96;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        Art = bmp;
                    }
                }
                Updated?.Invoke();
            }
            catch (Exception ex) { Log.Error("Media refresh", ex); }
        }

        public async void PlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
        public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
        public async void Previous() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }
    }
}
