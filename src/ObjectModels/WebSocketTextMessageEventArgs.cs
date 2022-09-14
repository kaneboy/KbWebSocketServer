using System;
using KbWebSocketServer.WebSockets;

namespace KbWebSocketServer.ObjectModels {
    public readonly struct WebSocketTextMessageEventArgs {

        /// <summary>
        /// 发送此消息的客户端连接。
        /// </summary>
        public ConnectedWebSocket Client { get; init; }

        /// <summary>
        /// 文本。
        /// </summary>
        /// <remarks>
        /// 事件处理函数执行完后，支持此属性值的内存缓冲区将被自动回收重用，请勿将此属性的使用范围扩展到其他函数栈帧。
        /// 如果确实需要，将此属性值复制一份使用，或访问<see cref="TextAsString"/>获取一个安全的string对象。
        /// </remarks>
        public ArraySegment<char> Text { get; init; }

        /// <summary>
        /// 文本。
        /// </summary>
        /// <remarks>
        /// 每次调用都会创建并返回一个新的string对象。
        /// </remarks>
        public string TextAsString => new String(Text);

    }
}
