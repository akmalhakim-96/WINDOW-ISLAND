using System;
using System.Collections.Generic;

namespace WindowIsland.Core
{
    /// <summary>
    /// Offline prayer time calculation (no internet needed).
    /// Uses JAKIM parameters: Fajr 20°, Isha 18°, Asr Shafi'i, + 2 min ihtiyat.
    /// Based on the public-domain PrayTimes.org astronomical formulas.
    /// </summary>
    public static class PrayerTimes
    {
        public static readonly string[] Names = { "Subuh", "Syuruk", "Zohor", "Asar", "Maghrib", "Isyak" };
        public const double FajrAngle = 20, IshaAngle = 18;
        public const int Ihtiyat = 2;

        public static List<(string Name, DateTime Time)> For(DateTime date, double lat, double lon)
        {
            double tz = TimeZoneInfo.Local.GetUtcOffset(date).TotalHours;
            double jd = Julian(date.Year, date.Month, date.Day) - lon / (15.0 * 24.0);
            var (decl, eqt) = Sun(jd + 0.5);

            double noon = 12 + tz - lon / 15.0 - eqt;
            double fajr = noon - HourAngle(FajrAngle, lat, decl);
            double sunrise = noon - HourAngle(0.833, lat, decl);
            double asr = noon + AsrAngle(1, lat, decl);
            double maghrib = noon + HourAngle(0.833, lat, decl);
            double isha = noon + HourAngle(IshaAngle, lat, decl);

            var d = date.Date;
            DateTime At(double h, int adj) => double.IsNaN(h) ? DateTime.MinValue : d.AddHours(h).AddMinutes(adj);
            return new()
            {
                ("Subuh", Round(At(fajr, Ihtiyat))),
                ("Syuruk", Round(At(sunrise, -Ihtiyat))),
                ("Zohor", Round(At(noon, Ihtiyat))),
                ("Asar", Round(At(asr, Ihtiyat))),
                ("Maghrib", Round(At(maghrib, Ihtiyat))),
                ("Isyak", Round(At(isha, Ihtiyat))),
            };
        }

        /// <summary>Next prayer (skips Syuruk) from now, looking into tomorrow if needed.</summary>
        public static (string Name, DateTime Time) Next(DateTime now, double lat, double lon)
        {
            foreach (var p in For(now, lat, lon))
                if (p.Name != "Syuruk" && p.Time > now) return p;
            var tomorrow = For(now.AddDays(1), lat, lon);
            return tomorrow[0];
        }

        static DateTime Round(DateTime t) => t == DateTime.MinValue ? t : new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0).AddMinutes(t.Second >= 30 ? 1 : 0);

        static double Julian(int y, int m, int d)
        {
            if (m <= 2) { y -= 1; m += 12; }
            double a = Math.Floor(y / 100.0), b = 2 - a + Math.Floor(a / 4.0);
            return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + b - 1524.5;
        }

        static (double decl, double eqt) Sun(double jd)
        {
            double D = jd - 2451545.0;
            double g = Fix(357.529 + 0.98560028 * D, 360);
            double q = Fix(280.459 + 0.98564736 * D, 360);
            double L = Fix(q + 1.915 * Sin(g) + 0.020 * Sin(2 * g), 360);
            double e = 23.439 - 0.00000036 * D;
            double ra = Fix(ATan2(Cos(e) * Sin(L), Cos(L)) / 15.0, 24);
            double eqt = q / 15.0 - ra;
            if (eqt > 12) eqt -= 24; if (eqt < -12) eqt += 24;
            double decl = ASin(Sin(e) * Sin(L));
            return (decl, eqt);
        }

        static double HourAngle(double angle, double lat, double decl)
            => ACos((-Sin(angle) - Sin(decl) * Sin(lat)) / (Cos(decl) * Cos(lat))) / 15.0;

        static double AsrAngle(double factor, double lat, double decl)
        {
            double a = -ACot(factor + Tan(Math.Abs(lat - decl)));
            return HourAngle(a, lat, decl);
        }

        const double R = Math.PI / 180.0;
        static double Sin(double d) => Math.Sin(d * R);
        static double Cos(double d) => Math.Cos(d * R);
        static double Tan(double d) => Math.Tan(d * R);
        static double ASin(double x) => Math.Asin(x) / R;
        static double ACos(double x) => Math.Acos(Math.Clamp(x, -1, 1)) / R;
        static double ATan2(double y, double x) => Math.Atan2(y, x) / R;
        static double ACot(double x) => Math.Atan(1 / x) / R;
        static double Fix(double a, double b) { a -= b * Math.Floor(a / b); return a < 0 ? a + b : a; }
    }
}
