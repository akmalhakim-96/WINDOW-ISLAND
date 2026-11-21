using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WindowIsland.Core
{
    public record ThemeDef(string Name, string WidgetBg, string WidgetFg, string WidgetSub, string DockBg, string DockBorder, string Accent, string BarBg, string BarFg);

    public static class ThemeManager
    {
        public static readonly List<ThemeDef> Themes = new()
        {
            new("Malam",     "#E61C1C24", "#FFF4F4F8", "#FF9A9AAE", "#B31C1C24", "#33FFFFFF", "#FF8B7CFF", "#99101018", "#FFFFFFFF"),
            new("Kertas",    "#F2FFFFFF", "#FF15151A", "#FF6B6B78", "#BFFFFFFF", "#40FFFFFF", "#FF3B82F6", "#66FFFFFF", "#FF15151A"),
            new("Sakura",    "#F2FFF1F5", "#FF3A1C2A", "#FF9B6B80", "#BFFFE4EE", "#66FFFFFF", "#FFEC4899", "#66FFE4EE", "#FF3A1C2A"),
            new("Kopi Susu", "#F2F7EFE5", "#FF3B2A1E", "#FF8A7563", "#BFEFE3D3", "#66FFFFFF", "#FFB07A4F", "#66F7EFE5", "#FF3B2A1E"),
            new("Mint",      "#F2EFFBF5", "#FF12352A", "#FF5E8577", "#BFE3F7EE", "#66FFFFFF", "#FF10B981", "#66EFFBF5", "#FF12352A"),
            new("Lautan",    "#E60B1E33", "#FFEAF4FF", "#FF8FB3D9", "#B30B1E33", "#33FFFFFF", "#FF22D3EE", "#990B1E33", "#FFEAF4FF"),
        };

        public static void Apply(string name)
        {
            var t = Themes.Find(x => x.Name == name) ?? Themes[0];
            var r = Application.Current.Resources;
            r["WidgetBg"] = Brush(t.WidgetBg);
            r["WidgetFg"] = Brush(t.WidgetFg);
            r["WidgetSub"] = Brush(t.WidgetSub);
            r["DockBg"] = Brush(t.DockBg);
            r["DockBorder"] = Brush(t.DockBorder);
            r["Accent"] = Brush(t.Accent);
            r["AccentColor"] = (Color)ColorConverter.ConvertFromString(t.Accent);
            r["BarBg"] = Brush(t.BarBg);
            r["BarFg"] = Brush(t.BarFg);
        }

        public static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }
}
