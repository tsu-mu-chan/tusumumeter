using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YoutubeCounterApp
{
    public class LocalServerService : IDisposable
    {
        private readonly List<WebSocket> _sockets = new();
        private HttpListener? _wsListener;
        private HttpListener? _staticFileListener;
        private CancellationTokenSource? _cts;

        public string LastBroadcastMessage { get; set; } = string.Empty;

        /// <summary>
        /// 静的ファイルサーバー (8080) と WebSocket サーバー (8081) を起動
        /// </summary>
        public void Start(int staticPort = 8080, int wsPort = 8081)
        {
            _cts = new CancellationTokenSource();

            StartStaticFileServer(staticPort, _cts.Token);
            StartWebSocketServer(wsPort, _cts.Token);
        }

        private void StartStaticFileServer(int port, CancellationToken ct)
        {
            Task.Run(async () =>
            {
                try
                {
                    _staticFileListener = new HttpListener();
                    _staticFileListener.Prefixes.Add($"http://localhost:{port}/");
                    _staticFileListener.Start();
                    Logger.WriteLog($"[Server] 静的ファイルサーバーを開始しました: port {port}");

                    while (!ct.IsCancellationRequested && _staticFileListener.IsListening)
                    {
                        var context = await _staticFileListener.GetContextAsync();
                        string rawPath = context.Request.Url?.LocalPath.TrimStart('/') ?? "";
                        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rawPath);

                        if (File.Exists(filePath))
                        {
                            byte[] buffer = await File.ReadAllBytesAsync(filePath, ct);
                            context.Response.ContentLength64 = buffer.Length;
                            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct);
                        }
                        else
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        }
                        context.Response.OutputStream.Close();
                    }
                }
                catch (Exception ex) when (ct.IsCancellationRequested)
                {
                    Logger.WriteLog("[Server] 静的ファイルサーバーを停止しました。");
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[Server Error] 静的ファイルサーバー例外: {ex.Message}");
                }
            }, ct);
        }

        private void StartWebSocketServer(int port, CancellationToken ct)
        {
            Task.Run(async () =>
            {
                try
                {
                    _wsListener = new HttpListener();
                    _wsListener.Prefixes.Add($"http://localhost:{port}/");
                    _wsListener.Start();
                    Logger.WriteLog($"[Server] WebSocketサーバーを開始しました: port {port}");

                    while (!ct.IsCancellationRequested && _wsListener.IsListening)
                    {
                        var context = await _wsListener.GetContextAsync();
                        if (context.Request.IsWebSocketRequest)
                        {
                            _ = AcceptClientAsync(context, ct);
                        }
                    }
                }
                catch (Exception ex) when (ct.IsCancellationRequested)
                {
                    Logger.WriteLog("[Server] WebSocketサーバーを停止しました。");
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[Server Error] WebSocketサーバー起動例外: {ex.Message}");
                }
            }, ct);
        }

        private async Task AcceptClientAsync(HttpListenerContext context, CancellationToken ct)
        {
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                var socket = wsContext.WebSocket;

                lock (_sockets) { _sockets.Add(socket); }

                // 初回接続時に最新の配信状態を即時送信
                if (!string.IsNullOrEmpty(LastBroadcastMessage))
                {
                    byte[] buffer = Encoding.UTF8.GetBytes(LastBroadcastMessage);
                    await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, ct);
                }

                await HandleSocketDisconnect(socket, ct);
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Server Error] クライアント接続受付時例外: {ex.Message}");
            }
        }

        private async Task HandleSocketDisconnect(WebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[1024];
            try
            {
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch { }
            finally
            {
                lock (_sockets) { _sockets.Remove(socket); }
                socket.Dispose();
            }
        }

        /// <summary>
        /// 接続中の全クライアントへメッセージを一括配信
        /// </summary>
        public async Task BroadcastAsync(string message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            List<WebSocket> listCopy;
            lock (_sockets) { listCopy = _sockets.ToList(); }

            foreach (var socket in listCopy)
            {
                if (socket.State == WebSocketState.Open)
                {
                    try
                    {
                        await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch { }
                }
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _staticFileListener?.Stop(); } catch { }
            try { _wsListener?.Stop(); } catch { }

            lock (_sockets)
            {
                foreach (var s in _sockets)
                {
                    try { s.Dispose(); } catch { }
                }
                _sockets.Clear();
            }
        }
    }
}