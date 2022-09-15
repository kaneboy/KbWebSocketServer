using System;
using System.Buffers;
using System.Collections.Generic;
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
    private static readonly ThreadLocal<StringBuilder> s_stringBuilderCache = new ThreadLocal<StringBuilder>(() => new StringBuilder());

    private readonly IPAddress _hostIp;
    private readonly int _hostPort;
    private readonly TcpListenerEx _tcpListener;

    private readonly object _startedLocker = new object();

    private CancellationTokenSource? _acceptClientsCancelTokenSource;

    //private ImmutableArray<WebSocket> _clients = ImmutableArray<WebSocket>.Empty;

    private Func<WebSocketUpgradeContext, ValueTask>? _clientRequestHandler;

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
    /// 为客户端Stream指定一个装饰器。
    /// </summary>
    /// <remarks>
    /// 如果希望对客户端Stream进行额外处理（比如使用SslStream或GzipStream封装原始Stream，以提供数据加密和压缩功能），可以通过指定自定义装饰器实现功能。
    /// </remarks>
    public Func<Stream, Stream>? ClientStreamDecorator { get; set; } = null;

    ///// <summary>
    ///// 当前所有连接到服务器的WebSocket客户端连接。
    ///// </summary>
    //public ImmutableArray<WebSocket> Clients => _clients;

    /// <summary>
    /// 启动服务器，开始响应客户端请求。
    /// </summary>
    public void Start(Func<WebSocketUpgradeContext, ValueTask> clientRequestHandler)
    {
        if (clientRequestHandler == null)
            throw new ArgumentNullException(nameof(clientRequestHandler));

        // 避免重复启动。
        lock (_startedLocker)
        {
            if (Active)
                return;
        }

        _clientRequestHandler = clientRequestHandler;

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
        NetworkStream networkStream = tcpClient.GetStream();
        Stream stream = networkStream;

        // 使用自定义客户端Stream装饰器，对Stream进行额外封装。
        var clientStreamDecorator = ClientStreamDecorator;
        if (clientStreamDecorator != null)
        {
            try
            {
                stream = clientStreamDecorator(stream);
            } 
            catch
            {
                tcpClient.Dispose();
                return;
            }
        }

        // 等待客户端把所有握手请求的文本发送完毕。
        string? requestText = await WaitUntilHandshakeRequestReceived(tcpClient, networkStream, stream);
        if (string.IsNullOrWhiteSpace(requestText))
        {
            tcpClient.Dispose();
            return;
        }

        // 构建处理客户端请求的Context，执行传给Start()函数的客户端请求处理器。

        WebSocketUpgradeRequest req = new WebSocketUpgradeRequest
        {
            TcpClient = tcpClient,
            ClientStream = stream,
            RawText = requestText,
            Headers = ParseRequestHeaders(requestText)
        };

        WebSocketUpgradeResponse res = new WebSocketUpgradeResponse();

        WebSocketUpgradeContext context = new WebSocketUpgradeContext(
            req,
            res,
            static (ctx, _) => {
                if (ctx.Response.StatusCode != HttpStatusCode.SwitchingProtocols)
                    throw new InvalidOperationException($"Response.StatusCode should be {HttpStatusCode.SwitchingProtocols}!");
                SendHandshakeSuccessResponse(ctx.Request.ClientStream, ctx.Request.RawText, ctx.Response.Headers);
                WebSocket ws = CreateClientWebSocket(ctx.Request.TcpClient, ctx.Request.ClientStream);
                return ValueTask.FromResult(ws);
            },
            null,
            static (ctx, _) => {
                if (ctx.Response.StatusCode == HttpStatusCode.SwitchingProtocols)
                    throw new InvalidOperationException($"Response.StatusCode should NOT be {HttpStatusCode.SwitchingProtocols}!");
                SendHandshakeRejectResponse(ctx.Request.ClientStream, ctx.Response.StatusCode, ctx.Response.Headers);
                return ValueTask.CompletedTask;
            },
            null);

        try
        {
            await _clientRequestHandler!.Invoke(context).ConfigureAwait(false);
        }
        catch
        {
            tcpClient.Dispose();
        }
    }

    /// <summary>
    /// 等待握手信息接收完毕，返回接收到的请求文本。
    /// </summary>
    private static async ValueTask<string?> WaitUntilHandshakeRequestReceived(
        TcpClient tcpClient, 
        NetworkStream rawNetworkStream,
        Stream stream)
    {
        while (true)
        {
            while (!rawNetworkStream.DataAvailable && tcpClient.Connected)
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
                return null;
            }

            // 解读出文本内容。
            string requestText = ParseRequestText(tcpClient, stream);

            // 没有包含"get"，不是握手信息。
            if (!Regex.IsMatch(requestText, "^GET", RegexOptions.IgnoreCase))
            {
                continue;
            }

            return requestText;
        }
    }

    private static WebSocket CreateClientWebSocket(TcpClient tcpClient, Stream stream)
    {
        WebSocket webSocket = WebSocket.CreateFromStream(
            stream, 
            true, 
            null, 
            WebSocket.DefaultKeepAliveInterval);

        ConnectedWebSocket ws = new ConnectedWebSocket(
            tcpClient, 
            stream, 
            webSocket);

        //ImmutableInterlocked.Update(
        //    ref _clients,
        //    (arr, item) => arr.Add(item),
        //    ws);

        return ws;
    }

    /// <summary>
    /// 发送拒绝客户端握手的响应消息。
    /// </summary>
    private static void SendHandshakeRejectResponse(Stream stream, HttpStatusCode statusCode, IDictionary<string, string>? responseHeaders)
    {
        StringBuilder builder = s_stringBuilderCache.Value!;

        builder.Append("HTTP/1.1 ").Append((int)statusCode).Append(' ').Append(statusCode.ToString()).Append("\r\n");

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
    private static void SendHandshakeSuccessResponse(Stream stream, string requestText, IDictionary<string, string>? responseHeaders)
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
        foreach (ReadOnlyMemory<char> chunk in builder.GetChunks())
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

    private static string ParseRequestText(TcpClient tcpClient, Stream stream)
    {
        int bytesLength = Math.Max(tcpClient.Available, 1024 * 1024);
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