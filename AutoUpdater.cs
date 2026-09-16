using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace YoutubeCounterApp
{
    public class AutoUpdater
    {
        // 💡 リポジトリ名を実際の「tusumumeter」に修正
        private const string GitHubOwner = "tsu-mu-chan";
        private const string GitHubRepo = "tusumumeter";

        public async Task CheckAndUpdateAsync()
        {
            try
            {
                // 1. 現在のアセンブリバージョンを取得
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion == null) return;

                // 2. GitHub APIで最新リリース情報を取得
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("tusumumeter", "1.0"));

                string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode) return;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 💡 大文字の "V" でも小文字の "v" でも両方除去できるように対応
                string rawTag = root.GetProperty("tag_name").GetString() ?? "";
                string tagName = rawTag.TrimStart('v', 'V');
                if (!Version.TryParse(tagName, out Version? latestVersion)) return;

                string skippedVersion = AppSettings.Instance.SkippedVersion;

                // 既にスキップ設定されているバージョンなら何も処理しない
                if (latestVersion.ToString() == skippedVersion)
                {
                    return;
                }

                // 3. バージョン比較（GitHub側が大きい場合のみ処理）
                if (latestVersion > currentVersion)
                {
                    // 添付ファイル（ZIP）の直リンクURLを取得
                    string? downloadUrl = null;
                    if (root.TryGetProperty("assets", out var assets))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string name = asset.GetProperty("name").GetString() ?? "";
                            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(downloadUrl)) return;

                    // 4. ダイアログで確認
                    UpdateDialogResult result = UpdateDialogResult.No;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var dialog = new UpdateWindow(latestVersion.ToString())
                        {
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            Topmost = true
                        };        
                        var mainWindow = Application.Current.MainWindow;
                        if (mainWindow != null && mainWindow.IsVisible)
                        {
                            dialog.Owner = Application.Current.MainWindow;
                        }              
                        dialog.ShowDialog();
                        result = dialog.Result;
                    });

                    if (result == UpdateDialogResult.Yes)
                    {
                        // 今すぐアップデート
                        await ExecuteUpdateAsync(client, downloadUrl);
                    }
                    else if (result == UpdateDialogResult.Skip)
                    {
                        // スキップ登録
                        AppSettings.Instance.SkippedVersion = latestVersion.ToString();
                        AppSettings.Instance.Save();

                        MessageBox.Show($"バージョン {latestVersion} をスキップするように設定しました。");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update Check Failed: {ex.Message}");
            }
        }

        private async Task ExecuteUpdateAsync(HttpClient client, string downloadUrl)
        {
            try
            {
                string tempZipPath = Path.Combine(Path.GetTempPath(), "update_temp.zip");
                string appDir = AppDomain.CurrentDomain.BaseDirectory;

                // ZIPファイルのダウンロード
                var bytes = await client.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(tempZipPath, bytes);

                int processId = Environment.ProcessId;
                string exeName = Process.GetCurrentProcess().MainModule?.ModuleName ?? "つむメーター.exe";

                // アプリ終了後にZIPを展開して上書き・再起動するPowerShell処理
                string psScript = $@"
                                Start-Sleep -Seconds 1
                                Wait-Process -Id {processId} -ErrorAction SilentlyContinue
                                Expand-Archive -Path '{tempZipPath}' -DestinationPath '{appDir}' -Force
                                Remove-Item '{tempZipPath}' -Force
                                Start-Process '{Path.Combine(appDir, exeName)}'
                                ";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(startInfo);

                // メインアプリを終了して更新に引き継ぐ
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アップデートの開始に失敗しました: {ex.Message}");
            }
        }
    }
}