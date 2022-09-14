using System.Net;
using System.Collections.Immutable;
using System.Net.WebSockets;
using KbWebSocketServer;

namespace TestServer;

internal class Program
{
    static void Main(string[] args)
    {
        var wss = new WebSocketServer(8888);

        var clients = ImmutableArray<WebSocket>.Empty;

        wss.Start(async ctx =>
        {
            var ws = await ctx.AcceptWebSocketAsync();
            ImmutableInterlocked.Update(
                ref clients,
                arr => arr.Add(ws));
            Pub(ws);
        });

        async ValueTask Pub(WebSocket ws)
        {
            var textMessages = ws
                .ReceiveMessagesAsync()
                .Where(msg => msg.MessageType == WebSocketMessageType.Text);
            
            await foreach (var message in textMessages)
            {
                if (message.Text.ToString().Equals("PleaseClose", StringComparison.OrdinalIgnoreCase))
                {
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, 
                        "Closed by server.", 
                        CancellationToken.None);
                    break;
                }
            }

            if (ws.State != WebSocketState.Open)
            {
                Console.WriteLine($"a client is disconnected. state={ws.State}");
            }
        }

        Console.ReadLine();
    }

    static async ValueTask Echo(List<WebSocket> clients, WebSocket ws)
    {
        await foreach (var message in ws.ReceiveMessagesAsync())
        {
            if (message.MessageType == WebSocketMessageType.Text)
            {
                string text = message.Text.ToString();
                Console.WriteLine(text);

                ImmutableArray<WebSocket> clientArr;
                lock (clients)
                {
                    clientArr = clients.ToImmutableArray();
                }

                foreach (WebSocket client in clientArr)
                {
                    if (client.State == WebSocketState.Open)
                    {
                        await client.SendTextAsync(text);
                    }
                }
            }
        }

        Console.WriteLine("Someone disconnected.");

        lock (clients)
            clients.Remove(ws);
    }
}
