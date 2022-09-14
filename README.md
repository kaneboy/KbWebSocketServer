[![NuGet](https://img.shields.io/nuget/v/KbWebSocketServer.svg?label=NuGet&logo=NuGet)](https://www.nuget.org/packages/KbWebSocketServer/)

# KbWebSocketServer

`KbWebSocketServer` is a lightweight .net websocket server library.
- Simple Api interface
- No 3rd-party package dependency required
- Target framework: net5.0;net6.0;net7.0

# Usage Tutorials

## Start up a websocket server and accept client request

```c#
var wss = new WebSocketServer(8000);

wss.Start(async ctx => 
{
    // put custom authentication codes here...

    var ws = await ctx.AcceptWebSocketAsync();
    Echo(ws);
});

async ValueTask Echo(WebSocket ws)
{
    await foreach (var message in ws.ReceiveMessagesAsync())
    {
        if (message.MessageType == WebSocketMessageType.Text)
        {
            Console.WriteLine(message.Text.ToString());
        }
    }
}
```

