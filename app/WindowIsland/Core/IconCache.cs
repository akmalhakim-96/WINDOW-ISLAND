using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowIsland.Core
{
    /// <summary>High quality shell icons (256px jumbo, falls back to 48px) with caching.</summary>
    public static class IconCache
    {
        static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
        static Native.IImageList _jumbo, _xl;

        public static ImageSource Get(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (Cache.TryGetValue(path, out var img)) return img;
            img = null;
            try
            {
                string resolved = ResolveForIcon(path);
                if (resolved != null)
                {
                    var info = new Native.SHFILEINFO();
                    Native.SHGetFileInfo(resolved, 0, ref info, (uint)Marshal.SizeOf(info), Native.SHGFI_SYSICONINDEX);
                    img = FromIndex(info.iIcon);
                }
            }
            catch (Exception ex) { Log.Error("Ikon gagal: " + path, ex); }
            Cache[path] = img;
            return img;
        }

        public static ImageSource RecycleBin(bool full)
        {
            string key = full ? "::recycle-full" : "::recycle";
            if (Cache.TryGetValue(key, out var img)) return img;
            try
            {
                var info = new Native.SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<Native.SHSTOCKICONINFO>() };
                if (Native.SHGetStockIconInfo(full ? Native.SIID_RECYCLERFULL : Native.SIID_RECYCLER, Native.SHGSI_SYSICONINDEX, ref info) == 0)
                    img = FromIndex(info.iSysImageIndex);
            }
            catch { }
            Cache[key] = img;
            return img;
        }

        static string ResolveForIcon(string path)
        {
            if (path.Contains(':') && !path.Contains('\\') && !path.Contains('/')) return null; // URI like ms-settings:
            if (File.Exists(path) || Directory.Exists(path)) return path;
            // bare exe name: search System32 / PATH
            string sys = Path.Combine(Environment.SystemDirectory, path);
            if (File.Exists(sys)) return sys;
            return null;
        }

        static ImageSource FromIndex(int index)
        {
            ImageSource best = null;
            try
            {
                var jumbo = List(Native.SHIL_JUMBO, ref _jumbo);
                if (jumbo != null)
                {
                    var bmp = IconFromList(jumbo, index);
                    // Many apps only ship small icons: jumbo then contains a tiny icon in a corner.
                    if (bmp != null && OpaqueExtent(bmp) >= 96) best = bmp;
                }
                if (best == null)
                {
                    var xl = List(Native.SHIL_EXTRALARGE, ref _xl);
                    if (xl != null) best = IconFromList(xl, index);
                }
            }
            catch (Exception ex) { Log.Error("ImageList", ex); }
            return best;
        }

        static Native.IImageList List(int size, ref Native.IImageList cache)
        {
            if (cache != null) return cache;
            Guid iid = typeof(Native.IImageList).GUID;
            return Native.SHGetImageList(size, ref iid, out cache) == 0 ? cache : null;
        }

        static BitmapSource IconFromList(Native.IImageList list, int index)
        {
            IntPtr hIcon = IntPtr.Zero;
            if (list.GetIcon(index, Native.ILD_TRANSPARENT, ref hIcon) != 0 || hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { Native.DestroyIcon(hIcon); }
        }

        /// <summary>Largest width/height of non-transparent content.</summary>
        static int OpaqueExtent(BitmapSource src)
        {
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            conv.CopyPixels(px, stride, 0);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * stride + x * 4 + 3] > 16)
                    {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
            if (maxX < 0) return 0;
            return Math.Max(maxX - minX, maxY - minY) + 1;
        }
    }
}
