using KbWebSocketServer.WebSockets;

namespace KbWebSocketServer.ObjectModels {
    /// <summary>
    /// WebSocket连接关闭事件参数。
    /// </summary>
    public readonly struct WebSocketClientClosedEventArgs {
        
        /// <summary>
        /// 断开连接的WebSocket客户端。
        /// </summary>
        public ConnectedWebSocket Client { get; init; }

        /// <summary>
        /// 关闭连接的代码。
        /// </summary>
        public int Code { get; init; }

        /// <summary>
        /// 关闭连接的原因。
        /// </summary>
        public string Reason { get; init; }

        public bool WasClean { get; init; }

    }
}
