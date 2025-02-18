﻿using System.Text;
using Newtonsoft.Json.Linq;

namespace SocketServer;

public abstract class ServerService(Server server) : IService
{
    public abstract string Name { get; }
    public abstract void OnStringMessage(string message);
    public abstract void OnDataMessage(byte[] data);
    public abstract void OnJsonMessage(JObject payload);

    public async Task SendString(string message)
    {
        var packet = Encoding.UTF8.GetBytes(message);
        await server.SendToClients(new TpcPacket(Name, MessageType.String, packet));
    }

    public async Task SendBinary(byte[] data)
    {
        await server.SendToClients(new TpcPacket(Name, MessageType.Binary, data));
    }

    public async Task SendJson(JObject payload)
    {
        var data = Encoding.UTF8.GetBytes(payload.ToString());
        await server.SendToClients(new TpcPacket(Name, MessageType.Json, data));
    }
}
