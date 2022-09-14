using System;
using System.Buffers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using KbWebSocketServer.ObjectModels;

namespace KbWebSocketServer.WebSockets;

/// <summary>
/// 与客户端的WebSocket连接。
/// </summary>
internal sealed partial class ConnectedWebSocket : WebSocket
{
    // 此连接所属的WebSocket服务端。
    private readonly WebSocketServer _wsServer;

    private readonly WebSocket _webSocket;
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private int _messageReceivingLoopStarted;

    // 从WebSocket连接接收到一个完整文本消息后，将它送给这个TransformBlock对文本内容进行解析。
    // TransformBlock会并行处理，并且可以保证按照消息到达的顺序，将结果发送给下游ActionBlock。
    private static readonly TransformBlock<WholeMessage, WholeMessage> s_textMessageParser
        = new TransformBlock<WholeMessage, WholeMessage>(
            ParseTextWholeMessage,
            new ExecutionDataflowBlockOptions
            {
                // 并发执行数量：CPU的1/4。
                MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount / 4, 1),
                TaskScheduler = TaskScheduler.Default,
            }
        );

    // 接收到的消息给上面的TransformBlock处理后，传递到这个ActionBlock，由它负责按消息到达的顺序触发TextMessage和BinaryMessage事件。
    private static readonly ActionBlock<WholeMessage> s_messageEventsRaiser = new ActionBlock<WholeMessage>(RaiseMessageEvents);

    static ConnectedWebSocket()
    {
        // 把TransformBlock和ActionBlock链接起来。
        s_textMessageParser.LinkTo(s_messageEventsRaiser);
    }

    /// <summary>
    /// 使用底层WebSocket对象初始化对象。
    /// </summary>
    internal ConnectedWebSocket(
        long clientId,
        WebSocketServer server,
        TcpClient tcpClient,
        NetworkStream stream,
        WebSocket webSocket,
        bool startReceivingImmediately = true)
    {
        ClientId = clientId;
        _wsServer = server;
        _tcpClient = tcpClient;
        _stream = stream;
        _webSocket = webSocket;
        if (startReceivingImmediately)
        {
            StartReceiving();
        }
    }

    ~ConnectedWebSocket() => Dispose(false);

    /// <summary>
    /// 标识客户端连接的序列号。
    /// </summary>
    public long ClientId { get; }

    /// <summary>
    /// WebSocket连接被关闭。
    /// </summary>
    public event EventHandler<WebSocketClientClosedEventArgs>? Closed;

    /// <summary>
    /// WebSocket连接出错。
    /// </summary>
    public event EventHandler<Exception>? Error;

    /// <summary>
    /// 接收到文本消息。
    /// </summary>
    public event EventHandler<WebSocketTextMessageEventArgs>? TextMessageReceived;

    /// <summary>
    /// 接收到二进制消息。
    /// </summary>
    public event EventHandler<WebSocketBinaryMessageEventArgs>? BinaryMessageReceived;

    /// <summary>
    /// 向客户端发送二进制消息。
    /// </summary>
    public ValueTask SendBinaryAsync(byte[] bytes, CancellationToken cancelToken = default)
    {
        return _webSocket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Binary, true, cancelToken);
    }

    /// <summary>
    /// 向客户端发送二进制消息。
    /// </summary>
    public ValueTask SendBinaryAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancelToken = default)
    {
        return _webSocket.SendAsync(bytes, WebSocketMessageType.Binary, true, cancelToken);
    }

    /// <summary>
    /// 向客户端发送二进制消息。
    /// </summary>
    public ValueTask SendBinaryAsync(ArraySegment<byte> bytes, CancellationToken cancelToken = default)
    {
        return new ValueTask(_webSocket.SendAsync(bytes, WebSocketMessageType.Binary, true, cancelToken));
    }

    /// <summary>
    /// 启动消息接收处理循环。收到消息后，将触发<see cref="BinaryMessageReceived"/>和<see cref="TextMessageReceived"/>事件。
    /// </summary>
    public void StartReceiving()
    {
        if (Interlocked.CompareExchange(ref _messageReceivingLoopStarted, 1, 0) == 0)
        {
            Task.Run(MessageReceiveLoop);
        }
    }

    /// <summary>
    /// 消息接收和处理循环。连接存续期间始终循环执行。
    /// </summary>
    private async ValueTask MessageReceiveLoop()
    {

        byte[]? buffer = null;
        int bufferSize = 0;

        while (true)
        {

            buffer = GrowBuffer(buffer, 0, bufferSize);

            // 完成一次数据接收。
            ValueWebSocketReceiveResult receiveResult;

            try
            {

                Memory<byte> availableBuffer = buffer.AsMemory(bufferSize, buffer.Length - bufferSize);

                receiveResult = await _webSocket
                    .ReceiveAsync(availableBuffer, CancellationToken.None)
                    .ConfigureAwait(false);

            }
            catch (Exception e)
            {

                // ReceiveAsync()抛出异常，通常代表着WebSocket连接已中断。
                ReleaseBuffer(buffer);
                Error?.Invoke(this, e);
                TryRaiseCloseEvent($"尝试从WebSocket接收数据时出错：{e.Message}");
                break;

            }

            WebSocketMessageType messageType = receiveResult.MessageType;
            int messageLength = receiveResult.Count;
            bool endOfMessage = receiveResult.EndOfMessage;

            // 收到对方的close请求，这时State是CloseReceived。
            // 响应一个close，之后State将变成Closed。
            if (messageType == WebSocketMessageType.Close)
            {
                ReleaseBuffer(buffer);
                await _webSocket.CloseAsync(_webSocket.CloseStatus ?? WebSocketCloseStatus.NormalClosure, _webSocket.CloseStatusDescription, CancellationToken.None).ConfigureAwait(false);
                TryRaiseCloseEvent("客户端请求断开连接。");
                break;
            }

            // 如果这时底层WebSocket的连接状态不是null，表示连接已断开。
            if (_webSocket.CloseStatus != null)
            {
                ReleaseBuffer(buffer);
                TryRaiseCloseEvent("客户端请求断开连接。");
                break;
            }

            bufferSize += messageLength;

            // message尚未接收完整，等待下一轮接收。
            if (!endOfMessage)
            {
                continue;
            }

            // 如果收到的是二进制消息，可以直接交给使用者，
            // 如果收到的是文本消息，需要先解析出文本内容，再交给使用者。
            if (messageType == WebSocketMessageType.Binary)
            {
                s_messageEventsRaiser.Post(new WholeMessage(this, messageType, buffer, bufferSize));
            }
            else if (messageType == WebSocketMessageType.Text)
            {
                s_textMessageParser.Post(new WholeMessage(this, messageType, buffer, bufferSize));
            }

            // 重置缓冲。下一轮申请新的缓冲区。
            buffer = null;
            bufferSize = 0;

        }

    }

    /// <summary>
    /// 如果状态确实是已关闭，触发断开连接事件。
    /// </summary>
    private void TryRaiseCloseEvent(string? reason)
    {
        var state = _webSocket.State;
        var closeStatus = _webSocket.CloseStatus;
        var closeStatusDescription = _webSocket.CloseStatusDescription;

        int code = closeStatus != null ? (int)closeStatus : 0;
        string closeReason = $"{reason}|State={state}|CloseStatus={closeStatus}|CloseStatusDescription={closeStatusDescription}";

        if (state == WebSocketState.Closed)
        {
            var e = new WebSocketClientClosedEventArgs
            {
                Client = this,
                Code = code,
                Reason = closeReason,
                WasClean = true
            };
            Closed?.Invoke(this, e);
            _wsServer.OnClientClosed(e);
        }

        if (state == WebSocketState.Aborted)
        {
            var e = new WebSocketClientClosedEventArgs
            {
                Client = this,
                Code = code,
                Reason = closeReason,
                WasClean = false
            };
            Closed?.Invoke(this, e);
            _wsServer.OnClientClosed(e);
        }

    }

    /// <summary>
    /// 解析出文本消息包包含的文本内容。
    /// </summary>
    private static WholeMessage ParseTextWholeMessage(WholeMessage message)
    {

        if (message.Type == WebSocketMessageType.Binary)
        {
            return message;
        }

        Encoding utf8 = Encoding.UTF8;
        int charsCount = utf8.GetCharCount(message.Buffer, 0, message.BufferSize);
        char[] charsBuffer = ArrayPool<char>.Shared.Rent(charsCount);
        int charsSize = utf8.GetChars(message.Buffer, 0, message.BufferSize, charsBuffer, 0);

        message.TextBuffer = charsBuffer;
        message.TextBufferSize = charsSize;

        return message;

    }

    /// <summary>
    /// 触发消息事件，正确地处理缓冲区。
    /// </summary>
    private static void RaiseMessageEvents(WholeMessage message)
    {
        if (message.Type == WebSocketMessageType.Text)
        {

            var e = new WebSocketTextMessageEventArgs
            {
                Client = message.Client,
                Text = new ArraySegment<char>(message.TextBuffer!, 0, message.TextBufferSize),
            };

            message.Client.TextMessageReceived?.Invoke(message.Client, e);
            message.Client._wsServer.OnTextMessage(e);

            ReleaseBuffer(message.TextBuffer);
            ReleaseBuffer(message.Buffer);

        }

        if (message.Type == WebSocketMessageType.Binary)
        {

            var e = new WebSocketBinaryMessageEventArgs
            {
                Client = message.Client,
                Bytes = new ArraySegment<byte>(message.Buffer, 0, message.BufferSize),
            };

            message.Client.BinaryMessageReceived?.Invoke(message.Client, e);
            message.Client._wsServer.OnBinaryMessage(e);

            ReleaseBuffer(message.Buffer);

        }

    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            GC.SuppressFinalize(this);
        }
        _webSocket.Dispose();
        _stream.Dispose();
        _tcpClient.Dispose();
    }

    private static T[] GrowBuffer<T>(T[]? buffer, int startIndex, int size)
    {

        if (buffer == null)
        {
            return ArrayPool<T>.Shared.Rent(4096);
        }

        // 如果buffer够用，无需grow。
        if (buffer.Length >= size * 2)
        {
            return buffer;
        }

        // 请求一个双倍大小的新buffer，把原buffer的内容复制进来。
        int newBufferCapacity = size > 0 ? size * 2 : buffer.Length * 2;
        T[] newBuffer = ArrayPool<T>.Shared.Rent(newBufferCapacity);
        Buffer.BlockCopy(buffer, startIndex, newBuffer, 0, size);

        // 归还旧buffer。
        ArrayPool<T>.Shared.Return(buffer);

        return newBuffer;

    }

    private static void ReleaseBuffer<T>(T[]? buffer)
    {
        if (buffer != null)
        {
            ArrayPool<T>.Shared.Return(buffer);
        }
    }

}