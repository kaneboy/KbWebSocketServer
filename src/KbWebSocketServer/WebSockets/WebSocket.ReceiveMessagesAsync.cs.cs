using System;
using System.Buffers;
using System.Collections.Generic;
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
    /// 一个完整的WebSocket消息包。
    /// </summary>
    internal readonly struct WholeMessage
    {
        public IMemoryOwner<byte> Buffer { get; init; }
        public int Size { get; init; }
        public WebSocketMessageType MessageType { get; init; }

        public ReadOnlyMemory<byte> MessageBytes => Buffer.Memory[..Size];
        public void FreeBuffer() => Buffer.Dispose();
    }

    /// <summary>
    /// 接收消息。使用 await foreach 处理返回的异步集合。
    /// </summary>
    /// <remarks>
    /// 如果连接意外中断，异步集合将正常结束而不会抛出异常。
    /// </remarks>
    public static async IAsyncEnumerable<WebSocketMessage> ReceiveMessagesAsync(
        WebSocket webSocket,
        MemoryPool<byte>? memoryPool = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 如果未指定缓冲池，使用默认的。
        memoryPool ??= MemoryPool<byte>.Shared;

        // 收掉一条完整消息之后，立即将消息放在这个Channel。
        Channel<WholeMessage> receivedMessages = Channel.CreateUnbounded<WholeMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        // 异步接收消息，收到消息就扔进Channel。
        _ = ReceiveWhileMessageBytesAsync(
            webSocket,
            memoryPool,
            msg => receivedMessages.Writer.TryWrite(msg),
            cancellationToken);

        // 从Channel逐个读取消息，将消息封装成文本/二进制消息，返回给调用者。
        await foreach (var message in receivedMessages.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                if (message.MessageType == WebSocketMessageType.Binary)
                {
                    yield return new WebSocketMessage(message.MessageBytes);
                }
                else if (message.MessageType == WebSocketMessageType.Text)
                {
                    Encoding utf8 = Encoding.UTF8;
                    int charCount = utf8.GetCharCount(message.MessageBytes.Span);
                    char[] charArr = ArrayPool<char>.Shared.Rent(charCount);
                    Memory<char> charBuffer = new Memory<char>(charArr, 0, charCount);
                    utf8.GetChars(message.MessageBytes.Span, charBuffer.Span);
                    try
                    {
                        yield return new WebSocketMessage(charBuffer);
                    }
                    finally
                    {
                        ArrayPool<char>.Shared.Return(charArr);
                    }
                }
            }
            finally
            {
                message.FreeBuffer();
            }
        }

        receivedMessages.Writer.Complete();
    }

    /// <summary>
    /// 从WebSocket接收完整的消息包。
    /// </summary>
    private static async ValueTask ReceiveWhileMessageBytesAsync(
        WebSocket webSocket,
        MemoryPool<byte> memoryPool,
        Action<WholeMessage> messageHandler,
        CancellationToken cancellationToken = default)
    {
        int maxMessageByteSize = 0;

        IMemoryOwner<byte>? buffer = null;
        int receivedByteSize = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // 从内存池请求一个buffer。
            buffer ??= memoryPool.Rent(maxMessageByteSize);

            // 完成一次数据接收。
            ValueWebSocketReceiveResult receiveResult;
            try
            {
                receiveResult = await webSocket
                    .ReceiveAsync(
                        buffer.Memory[receivedByteSize..],
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // ReceiveAsync()抛出异常，通常代表着WebSocket连接已中断。
                // 结束循环。
                break;
            }

            WebSocketMessageType messageType = receiveResult.MessageType;
            int messageLength = receiveResult.Count;
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

            receivedByteSize += messageLength;

            // message尚未接收完整，进行下一轮接收。
            if (!endOfMessage)
            {
                // 如果buffer空闲空间不太够，增大buffer。
                int freeSize = buffer.Memory.Length - receivedByteSize;
                if (freeSize < 4096)
                {
                    GrowBuffer(ref buffer, memoryPool, receivedByteSize);
                }
                continue;
            }

            // 整条消息已接受完整，将完整消息返回给调用者。
            // buffer的所有权将转交给调用者，这里不再引用这个buffer。
            messageHandler(new WholeMessage { MessageType = messageType, Buffer = buffer, Size = receivedByteSize });
            buffer = null;
            maxMessageByteSize = Math.Max(maxMessageByteSize, receivedByteSize);
            receivedByteSize = 0;
        }

        // 此时连接已断开。

        buffer?.Dispose();
    }

    private static void GrowBuffer(ref IMemoryOwner<byte> buffer, MemoryPool<byte> memoryPool, int usedSize)
    {
        // 请求一个空间倍增的新缓冲区。
        int newSize = Math.Max(buffer.Memory.Length * 2, 4096);
        IMemoryOwner<byte> newBuffer = memoryPool.Rent(newSize);

        // 将旧缓冲区的数据复制到新缓冲区。
        buffer.Memory.Slice(0, usedSize).CopyTo(newBuffer.Memory.Slice(0, usedSize));

        // 释放旧缓冲区。
        buffer.Dispose();

        // 修改ref参数，指向新缓冲区。
        buffer = newBuffer;
    }
}
