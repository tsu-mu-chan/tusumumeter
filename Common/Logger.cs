using System;
using System.IO;

namespace YoutubeCounterApp
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.txt");
        private static readonly object LockObj = new();

        /// <summary>
        /// ログファイル (Log.txt) にタイムスタンプ付きで出力（スレッドセーフ）
        /// </summary>
        public static void WriteLog(string message)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                lock (LockObj)
                {
                    File.AppendAllText(LogPath, line);
                }
            }
            catch
            {
                // ロギング自体の失敗で本体動作を止めないよう保護
            }
        }
    }
}