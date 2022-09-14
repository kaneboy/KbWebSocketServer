using System.Net.WebSockets;

namespace KbWebSocketServer.WebSockets;

sealed partial class ConnectedWebSocket {

    /// <summary>
    /// 一个完整的消息包。
    /// </summary>
    /// <remarks>
    /// WebSocket会将一个完整消息包自动拆分成多帧，分批发送到目标。
    /// 目标逐个接收到每帧后，将把包含一个消息包的所有数据都整合到此类型对象中。
    /// </remarks>
    private struct WholeMessage {

        public readonly ConnectedWebSocket Client;
        public readonly WebSocketMessageType Type;
        public readonly byte[] Buffer;
        public readonly int BufferSize;

        // 如果是文本消息，下面的2个属性存储解析出来的文本内容。
        public char[]? TextBuffer;
        public int TextBufferSize;
            
        public WholeMessage(ConnectedWebSocket client, WebSocketMessageType type, byte[] buffer, int bufferSize) {
            Client = client;
            Type = type;
            Buffer = buffer;
            BufferSize = bufferSize;
            TextBuffer = null;
            TextBufferSize = 0;
        }

    }

}