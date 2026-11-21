using System;
using System.IO;

namespace WindowIsland.Core
{
    public static class Log
    {
        static readonly object Gate = new();
        static string FilePath => Path.Combine(Config.Dir, "log.txt");

        public static void Info(string msg) => Write("INFO", msg);
        public static void Error(string msg, Exception ex = null) => Write("ERROR", ex == null ? msg : msg + " :: " + ex);

        static void Write(string level, string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Config.Dir);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) File.Move(FilePath, FilePath + ".old", true);
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
