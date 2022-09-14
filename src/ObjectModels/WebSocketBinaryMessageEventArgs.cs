using System;
using KbWebSocketServer.WebSockets;

namespace KbWebSocketServer.ObjectModels {
    public readonly struct WebSocketBinaryMessageEventArgs {

        /// <summary>
        /// 发送此消息的客户端连接。
        /// </summary>
        public ConnectedWebSocket Client { get; init; }

        /// <summary>
        /// 二进制数据。
        /// </summary>
        /// <remarks>
        /// 事件处理函数执行完后，支持此属性值的内存缓冲区将被自动回收重用，请勿将此属性的使用范围扩展到其他函数栈帧。
        /// 如果确实需要，将此属性值复制一份使用。
        /// </remarks>
        public ArraySegment<byte> Bytes { get; init; }

    }
}
