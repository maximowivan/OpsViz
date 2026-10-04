using System;
using System.IO;

namespace OpsViz.Infra
{
    // Диагностический журнал: %AppData%\OpsViz\debug.log.
    // Пишет только действия пользователя и ошибки (не горячие пути),
    // чтобы по логу было видно, что реально произошло. Сам себя
    // обрезает свыше 1 МБ. Падения не вызывает никогда.
    public static class Logger
    {
        static readonly object _lock = new object();
        static string _path;
        static bool _trimmed;

        public static string LogPath
        {
            get
            {
                if (_path == null)
                {
                    try
                    {
                        string dir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "OpsViz");
                        Directory.CreateDirectory(dir);
                        _path = Path.Combine(dir, "debug.log");
                    }
                    catch { _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log"); }
                }
                return _path;
            }
        }

        public static void Log(string msg)
        {
            try
            {
                lock (_lock)
                {
                    if (!_trimmed)
                    {
                        _trimmed = true;
                        try
                        {
                            var fi = new FileInfo(LogPath);
                            if (fi.Exists && fi.Length > 1024 * 1024) fi.Delete();
                        }
                        catch { }
                    }
                    File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n");
                }
            }
            catch { }
        }

        public static void OpenLog()
        {
            try { System.Diagnostics.Process.Start(LogPath); }
            catch { }
        }
    }
}
