using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace KbWebSocketServer.WebSockets;

internal static class WebSocketReceiveMessagesAsyncExtension
{
    /// <summary>
    /// 从 <paramref name="webSocket"/> 接收消息。使用 await foreach 处理返回的异步集合。
    /// </summary>
    /// <remarks>
    /// 如果 <paramref name="webSocket"/> 连接中断，异步集合将正常结束而不会抛出异常。如果 <paramref name="cancellationToken"/> 触发取消，抛出操作取消异常。
    /// </remarks>
    public static async IAsyncEnumerable<WebSocketMessage> ReceiveMessagesAsync(
        WebSocket webSocket,
        MemoryPool<byte>? memoryPool = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 创建一个 pipe，用于暂存从 websocket 收到的完整消息包的二进制数据。
        Pipe pipe = new Pipe(new PipeOptions(pool: memoryPool ?? MemoryPool<byte>.Shared));

        // 使用这个 channel 当异步队列使用，每收到一个完整消息包，就会往这里塞入一个数据。
        // 消息包的真正二进制内容位于 pipe 里面，这里只记录消息包的类型和长度。
        Channel<(WebSocketMessageType, int)> messageEvents = Channel.CreateUnbounded<(WebSocketMessageType, int)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        // 异步从 websocket 读取完整消息包，将消息包放进事件队列。
        _ = ReadMessageAndWriteToBufferWriterAsync(
            webSocket,
            pipe.Writer,
            onMessage: static (msgQueue, msgType, msgSize) => msgQueue.Writer.TryWrite((msgType, msgSize)),
            onCompleted: static msgQueue => msgQueue.Writer.TryComplete(),
            messageEvents,
            cancellationToken);

        // 逐个接收消息包到达的事件。
        await foreach (var (msgType, msgSize) in messageEvents.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // 从 pipe 读完整消息包。
            var readResult = await ReadAtLeastAsync(pipe.Reader, msgSize, cancellationToken).ConfigureAwait(false);

            if (readResult.Buffer.Length != 0)
            {
                switch (msgType)
                {
                    case WebSocketMessageType.Binary:
                        yield return new WebSocketMessage(readResult.Buffer.Slice(0, msgSize));
                        break;
                    case WebSocketMessageType.Text:
                    {
                        Encoding utf8 = Encoding.UTF8;
                        char[] charArr = ArrayPool<char>.Shared.Rent(utf8.GetMaxCharCount(msgSize));
                        int charCount = utf8.GetChars(readResult.Buffer.Slice(0, msgSize), charArr.AsSpan());
                        try
                        {
                            yield return new WebSocketMessage(charArr.AsMemory(0, charCount));
                        }
                        finally
                        {
                            ArrayPool<char>.Shared.Return(charArr);
                        }
                        break;
                    }
                }
            }

            if (readResult.IsCanceled || readResult.IsCompleted)
                break;

            pipe.Reader.AdvanceTo(readResult.Buffer.GetPosition(msgSize), readResult.Buffer.End);
        }

        // 此 websocket 所有消息接收完成。
        Debug.WriteLine("WebSocket断开连接。");
    }

    /// <summary>
    /// 从 <paramref name="webSocket"/> 接收完整的消息包，写入到 <paramref name="pipeWriter"/>。每次收到一个完整消息包，调用 <paramref name="onMessage"/>，传入消息类型和消息包大小。
    /// </summary>
    private static async ValueTask ReadMessageAndWriteToBufferWriterAsync<TArg>(
        WebSocket webSocket,
        PipeWriter pipeWriter,
        Action<TArg, WebSocketMessageType, int> onMessage,
        Action<TArg> onCompleted,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        // 一个完整消息包的大小。
        int messageSize = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // 请求一个buffer。
            var buffer = pipeWriter.GetMemory();

            // 完成一次数据接收。
            ValueWebSocketReceiveResult receiveResult;
            try
            {
                receiveResult = await webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ReceiveAsync()抛出异常，通常代表着WebSocket连接已中断。
                // 结束循环。
                break;
            }

            WebSocketMessageType messageType = receiveResult.MessageType;
            int receivedSize = receiveResult.Count;
            bool endOfMessage = receiveResult.EndOfMessage;

            // 收到对方的close请求，这时State是CloseReceived。
            // 响应一个close，之后State将变成Closed。
            if (messageType == WebSocketMessageType.Close)
            {
                try
                {
                    await webSocket
                        .CloseAsync(
                            webSocket.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                            webSocket.CloseStatusDescription,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch { /**/ }
                break;
            }

            // 如果这时底层WebSocket的连接状态不是null，表示连接已断开。
            if (webSocket.CloseStatus != null)
            {
                break;
            }

            // 将数据写入到bufferWriter。
            pipeWriter.Advance(receivedSize);
            await pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);

            messageSize += receivedSize;

            // 已经收到一个完整的消息包，告诉调用者完整消息包的类型和大小。
            if (endOfMessage)
            {
                onMessage(arg, messageType, messageSize);
                messageSize = 0;
            }
        }

        // 此时连接已断开。
        onCompleted(arg);
    }

    private static async ValueTask<ReadResult> ReadAtLeastAsync(PipeReader reader, int minimumSize, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (buffer.Length >= minimumSize || result.IsCompleted || result.IsCanceled)
            {
                return result;
            }

            // Keep buffering until we get more data
            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }
}
