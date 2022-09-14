using System.Threading;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Net.WebSockets;
using System.Buffers;
using System.Text;

namespace KbWebSocketServer.WebSockets;

internal static class WebSocketReceiveMessagesAsyncExtension
{
    /// <summary>
    /// 接收消息。使用 await foreach 处理返回的异步集合。
    /// </summary>
    /// <remarks>
    /// 如果连接意外中断，异步集合将正常结束而不会抛出异常。
    /// </remarks>
    public static async IAsyncEnumerable<WebSocketMessage> ReceiveMessagesAsync(
        WebSocket webSocket,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 在此函数生命周期内，一直使用这个缓冲区。函数尾释放它。
        byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
        int receivedByteSize = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // 完成一次数据接收。
            ValueWebSocketReceiveResult receiveResult;
            try
            {
                receiveResult = await webSocket
                    .ReceiveAsync(
                        buffer.AsMemory(receivedByteSize, buffer.Length - receivedByteSize), 
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
                catch
                {
                    //
                }
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
                buffer = GrowBuffer(buffer, receivedByteSize);
                continue;
            }

            // 整条消息已接受完整，将完整消息返回给调用者。                
            if (messageType == WebSocketMessageType.Binary)
            {
                // 收到的是二进制消息，可以直接交给调用者。
                yield return new WebSocketMessage(buffer, receivedByteSize);
            }
            else if (messageType == WebSocketMessageType.Text)
            {
                // 收到的是文本消息，需要先解析出文本内容，再交给调用者。
                Encoding utf8 = Encoding.UTF8;
                int charsBufferSize = utf8.GetCharCount(buffer, 0, receivedByteSize);
                char[] charsBuffer = ArrayPool<char>.Shared.Rent(charsBufferSize);
                int charsSize = utf8.GetChars(buffer, 0, receivedByteSize, charsBuffer, 0);
                try
                {
                    yield return new WebSocketMessage(charsBuffer, charsSize);
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(charsBuffer);
                }
            }

            // 处理并返回了一个完整消息，继续下一轮接收。
            receivedByteSize = 0;
        }

        // 此时连接已断开。

        ArrayPool<byte>.Shared.Return(buffer);
    }

    /// <summary>
    /// 按需增大缓冲区，返回增大后的新缓冲区。
    /// </summary>
    /// <param name="buffer">旧的缓冲区。</param>
    /// <param name="usedSize">旧的缓冲区中已经用了多少。</param>
    private static T[] GrowBuffer<T>(T[] buffer, int usedSize)
    {
        // 如果buffer够用，无需grow。
        if (buffer.Length >= usedSize * 2)
        {
            return buffer;
        }

        // 请求一个双倍大小的新buffer，把原buffer的内容复制进来。
        int newBufferCapacity = usedSize > 0 ? usedSize * 2 : buffer.Length * 2;
        T[] newBuffer = ArrayPool<T>.Shared.Rent(newBufferCapacity);
        Buffer.BlockCopy(buffer, 0, newBuffer, 0, usedSize);

        // 归还旧buffer。
        ArrayPool<T>.Shared.Return(buffer);

        return newBuffer;
    }
}