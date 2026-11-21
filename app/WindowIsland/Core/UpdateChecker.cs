using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace WindowIsland.Core
{
    public static class UpdateChecker
    {
        public const string Repo = "akmalhakim-96/WINDOW-ISLAND";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
        public static readonly HttpClient Http = CreateHttp();

        static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("WindowIsland/" + CurrentVersion);
            return h;
        }

        public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

        /// <summary>Returns the newer version tag, or null if up to date / offline.</summary>
        public static async Task<string> CheckAsync()
        {
            try
            {
                var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
                using var doc = JsonDocument.Parse(json);
                string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                if (Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                {
                    var cur = CurrentVersion;
                    if (latest > new Version(cur.Major, cur.Minor, Math.Max(cur.Build, 0))) return tag;
                }
            }
            catch (Exception ex) { Log.Info("Semakan kemas kini gagal: " + ex.Message); }
            return null;
        }
    }
}
