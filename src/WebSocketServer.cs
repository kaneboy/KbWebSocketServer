﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KbWebSocketServer.ObjectModels;
using KbWebSocketServer.WebSockets;

namespace KbWebSocketServer;

/// <summary>
/// WebSocket服务器。
/// </summary>
public sealed class WebSocketServer
{
    private static long s_clientIdCounter;
    private static readonly ThreadLocal<StringBuilder> s_stringBuilderCache = new ThreadLocal<StringBuilder>(() => new StringBuilder());

    private readonly IPAddress _hostIp;
    private readonly int _hostPort;
    private readonly TcpListenerEx _tcpListener;

    private readonly object _startedLocker = new object();

    private CancellationTokenSource? _acceptClientsCancelTokenSource;

    private ImmutableArray<WebSocket> _clients = ImmutableArray<WebSocket>.Empty;

    /// <summary>
    /// 在当前所有可用IP地址的指定端口上初始化WebSocket服务器。
    /// </summary>
    public WebSocketServer(int port) : this(IPAddress.Any, port) { }

    /// <summary>
    /// 在指定IP地址和端口上初始化WebSocket服务器。
    /// </summary>
    public WebSocketServer(string ip, int port) : this(IPAddress.Parse(ip), port) { }

    /// <summary>
    /// 在指定IP地址和端口上初始化WebSocket服务器。
    /// </summary>
    public WebSocketServer(IPAddress ip, int port)
    {
        _hostIp = ip;
        _hostPort = port;
        _tcpListener = new TcpListenerEx(ip, port);
    }

    /// <summary>
    /// 事件：收到一条文本消息。
    /// </summary>
    public event EventHandler<WebSocketTextMessageEventArgs>? TextMessageReceived;

    /// <summary>
    /// 事件：收到一条二进制消息。
    /// </summary>
    public event EventHandler<WebSocketBinaryMessageEventArgs>? BinaryMessageReceived;

    /// <summary>
    /// 事件：新的客户端请求连接到服务器（握手）。
    /// </summary>
    /// <remarks>
    /// 服务器端可以利用此事件拒绝客户端的握手请求。通过设置<see cref="WebSocketClientConnectingEventArgs.Accepted"/>的值，指定是否接受此连接请求。
    /// </remarks>
    public event EventHandler<WebSocketClientConnectingEventArgs>? ClientConnecting;

    /// <summary>
    /// 事件：新的客户端完成连接（握手成功）。
    /// </summary>
    public event EventHandler<WebSocketClientConnectedEventArgs>? ClientConnected;

    /// <summary>
    /// 事件：客户端从服务器断开。
    /// </summary>
    public event EventHandler<WebSocketClientClosedEventArgs>? ClientClosed;

    /// <summary>
    /// WebSocket服务器的宿主IP地址。
    /// </summary>
    public IPAddress HostIP => _hostIp;

    /// <summary>
    /// WebSocket服务器的宿主网络端口。
    /// </summary>
    public int HostPort => _hostPort;

    /// <summary>
    /// 是否已启动。
    /// </summary>
    public bool Active => _tcpListener.Active;

    /// <summary>
    /// 当前所有连接到服务器的WebSocket客户端连接。
    /// </summary>
    public ImmutableArray<WebSocket> Clients => _clients;

    /// <summary>
    /// 启动服务器，开始响应客户端请求。
    /// </summary>
    public void Start()
    {
        // 避免重复启动。
        lock (_startedLocker)
        {
            if (Active)
                return;
        }

        _tcpListener.Start();

        _acceptClientsCancelTokenSource = new CancellationTokenSource();
        Task.Run(() => AcceptClients(_acceptClientsCancelTokenSource.Token));
    }

    /// <summary>
    /// 停止服务器。
    /// </summary>
    public void Stop()
    {
        _acceptClientsCancelTokenSource?.Cancel();
        _tcpListener.Stop();
    }

    /// <summary>
    /// 触发TextMessage事件。
    /// </summary>
    internal void OnTextMessage(in WebSocketTextMessageEventArgs e)
    {
        TextMessageReceived?.Invoke(this, e);
    }

    /// <summary>
    /// 触发BinaryMessage事件。
    /// </summary>
    internal void OnBinaryMessage(in WebSocketBinaryMessageEventArgs e)
    {
        BinaryMessageReceived?.Invoke(this, e);
    }

    /// <summary>
    /// 触发ClientClose事件。
    /// </summary>
    internal void OnClientClosed(in WebSocketClientClosedEventArgs e)
    {
        ImmutableInterlocked.Update(
            ref _clients,
            (arr, item) => arr.Remove(item),
            e.Client);

        ClientClosed?.Invoke(this, e);
    }

    /// <summary>
    /// 接受客户端的连接请求。
    /// </summary>
    private async Task AcceptClients(CancellationToken cancelToken)
    {
        if (cancelToken.IsCancellationRequested)
        {
            return;
        }

        // 传入的CancellationToken触发时，会使这个Task也一并触发完成。
        //
        // 之所以需要这个Task，是因为.NET5的TcpListener.AcceptTcpClientAsync()不支持传入CancellationToken参数，
        // 所以只能用Task.WhenAny()实现等待连接可立即撤销的功能。
        TaskCompletionSource cancelTokenTaskSource = new TaskCompletionSource();
        Task cancelTokenTask = cancelTokenTaskSource.Task;
        using var ctr = cancelToken.UnsafeRegister(static s => ((TaskCompletionSource)s!).TrySetResult(), cancelTokenTaskSource);

        while (!cancelToken.IsCancellationRequested && _tcpListener.Active)
        {
            TcpClient tcpClient;
            try
            {
                // 等待：新的客户端连接到服务器 or 撤销操作被触发。
                Task completedTask = await Task.WhenAny(_tcpListener.AcceptTcpClientAsync(), cancelTokenTask).ConfigureAwait(false);
                if (completedTask is Task<TcpClient> tcpClientTask)
                {
                    tcpClient = tcpClientTask.Result;
                }
                else
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }
            _ = Task.Factory.StartNew(state => Handshake((TcpClient)state!), tcpClient, cancelToken);
        }
    }

    /// <summary>
    /// 处理客户端的握手请求。
    /// </summary>
    private async ValueTask Handshake(TcpClient tcpClient)
    {
        NetworkStream stream = tcpClient.GetStream();

        while (true)
        {
            while (!stream.DataAvailable && tcpClient.Connected)
            {
                await Task.Delay(0).ConfigureAwait(false);
            }
            // 握手消息至少会包含"get"。
            while (tcpClient.Available < 3 && tcpClient.Connected)
            {
                await Task.Delay(0).ConfigureAwait(false);
            }

            if (!tcpClient.Connected)
            {
                return;
            }

            // 解读出文本内容。
            string requestText = ParseRequestText(tcpClient, stream);

            // 没有包含"get"，不是握手信息。
            if (!Regex.IsMatch(requestText, "^GET", RegexOptions.IgnoreCase))
            {
                continue;
            }

            // 为每个客户端生成一个流水号。
            long clientId = Interlocked.Increment(ref s_clientIdCounter);

            bool accepted = true;
            IDictionary<string, string>? responseHeaders = null;

            // 如果注册了ClientConnecting事件，触发，并根据返回值确定是否接受连接请求。
            var connectingEvent = ClientConnecting;
            if (connectingEvent != null)
            {
                var e = new WebSocketClientConnectingEventArgs(
                    clientId,
                    tcpClient,
                    requestText,
                    ParseRequestHeaders(requestText)
                );

                connectingEvent.Invoke(this, e);

                if (e.Accepted.IsCompleted)
                {
                    accepted = e.Accepted.Result;
                }
                else
                {
                    try
                    {
                        accepted = await e.Accepted.ConfigureAwait(false);
                    }
                    catch
                    {
                        accepted = false;
                    }
                }

                if (e.ResponseHeaders.Count != 0)
                {
                    responseHeaders = e.ResponseHeaders;
                }
            }

            if (accepted)
            {
                SendHandshakeSuccessResponse(stream, requestText, responseHeaders);
                AddNewClient(clientId, tcpClient, stream);
            }
            else
            {
                SendHandshakeRejectResponse(stream, responseHeaders);
                stream.Dispose();
                tcpClient.Dispose();
            }

            return;
        }
    }

    private void AddNewClient(long clientId, TcpClient tcpClient, NetworkStream stream)
    {
        WebSocket client = WebSocket.CreateFromStream(stream, true, null, WebSocket.DefaultKeepAliveInterval);

        ConnectedWebSocket ws = new ConnectedWebSocket(this, tcpClient, stream, client);

        ImmutableInterlocked.Update(
            ref _clients,
            (arr, item) => arr.Add(item),
            ws);

        ClientConnected?.Invoke(
            this, 
            new WebSocketClientConnectedEventArgs { ClientId = clientId, Client = ws });
    }

    /// <summary>
    /// 发送拒绝客户端握手的响应消息。
    /// </summary>
    private static void SendHandshakeRejectResponse(NetworkStream stream, IDictionary<string, string>? responseHeaders)
    {
        StringBuilder builder = s_stringBuilderCache.Value!;

        builder.Append("HTTP/1.1 401 Unauthorized\r\n");

        if (responseHeaders != null)
        {
            foreach (var item in responseHeaders)
            {
                builder.Append(item.Key).Append(": ").Append(item.Value).Append('\r').Append('\n');
            }
        }

        builder.Append('\r').Append('\n');

        WriteUtf8TextToStream(builder, stream);
        builder.Clear();
    }

    /// <summary>
    /// 发送客户端握手成功的响应消息（允许客户端握手）。
    /// </summary>
    private static void SendHandshakeSuccessResponse(NetworkStream stream, string requestText, IDictionary<string, string>? responseHeaders)
    {
        string swkaSha1Base64 = GenerateSecWebSocketAccept(requestText);

        StringBuilder builder = s_stringBuilderCache.Value!;

        builder
            .Append("HTTP/1.1 101 Switching Protocols\r\n")
            .Append("Connection: Upgrade\r\n")
            .Append("Upgrade: websocket\r\n")
            .Append("Sec-WebSocket-Accept: ").Append(swkaSha1Base64).Append("\r\n")
            .Append("X-WSS-Library-Author: kaneboy\r\n");

        if (responseHeaders != null)
        {
            foreach (var item in responseHeaders)
            {
                builder.Append(item.Key).Append(": ").Append(item.Value).Append('\r').Append('\n');
            }
        }

        builder.Append("\r\n");

        WriteUtf8TextToStream(builder, stream);
        builder.Clear();
    }

    private static void WriteUtf8TextToStream(StringBuilder builder, Stream stream)
    {
        foreach (ReadOnlyMemory<Char> chunk in builder.GetChunks())
        {
            WriteUtf8TextToStream(chunk.Span, stream);
        }
    }

    private static void WriteUtf8TextToStream(ReadOnlySpan<char> text, Stream stream)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(text.Length * 8);
        int bytesLength = Encoding.UTF8.GetBytes(text, buffer);
        stream.Write(buffer, 0, bytesLength);
        ArrayPool<byte>.Shared.Return(buffer);
    }

    private static string ParseRequestText(TcpClient tcpClient, NetworkStream stream)
    {
        int bytesLength = tcpClient.Available;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(bytesLength);
        int readLength = stream.Read(bytes, 0, bytesLength);
        string requestText = Encoding.UTF8.GetString(bytes, 0, readLength);
        ArrayPool<byte>.Shared.Return(bytes);
        return requestText;
    }

    /// <summary>
    /// 解析Sec-WebSocket-Key，生成需要返回的Sec-WebSocket-Accept。
    /// </summary>
    private static string GenerateSecWebSocketAccept(string requestText)
    {
        // 1. Obtain the value of the "Sec-WebSocket-Key" request header without any leading or trailing whitespace
        // 2. Concatenate it with "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" (a special GUID specified by RFC 6455)
        // 3. Compute SHA-1 and Base64 hash of the new value
        // 4. Write the hash back as the value of "Sec-WebSocket-Accept" response header in an HTTP response
        string swk = Regex.Match(requestText, "Sec-WebSocket-Key: (.*)").Groups[1].Value.Trim();
        string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        byte[] swkaSha1 = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
        string swkaSha1Base64 = Convert.ToBase64String(swkaSha1);
        return swkaSha1Base64;
    }

    /// <summary>
    /// 从HTTP请求文本解析所有主机头。
    /// </summary>
    private static Dictionary<string, string> ParseRequestHeaders(string requestText)
    {

        Dictionary<string, string> headers = new Dictionary<string, string>();

        string[] lines = requestText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            // 每行的格式大致是："Host: example.com:8000"。
            int splitIndex = line.IndexOf(':');
            if (splitIndex != -1)
            {
                string key = line.Substring(0, splitIndex);
                string value = line.Substring(splitIndex + 1);
                // ":"后面通常跟着一个空格。
                if (value.StartsWith(' '))
                {
                    value = value.Substring(1);
                }
                headers[key] = value;
            }
        }

        return headers;

    }

    /// <summary>
    /// 这个子类的唯一作用是将原本protected属性Active(标识是否已开始监听)暴露出来。
    /// </summary>
    private sealed class TcpListenerEx : TcpListener
    {
        public TcpListenerEx(IPAddress localaddr, Int32 port) : base(localaddr, port) { }
        public new bool Active => base.Active;
    }

}