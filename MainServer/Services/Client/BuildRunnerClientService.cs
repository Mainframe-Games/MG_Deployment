using Newtonsoft.Json.Linq;
using SocketServer;

namespace MainServer.Services.Client;

internal sealed class BuildRunnerClientService(SocketServer.Client client) : ClientService(client)
{
    public string OperatingSystem { get; private set; } = string.Empty;

    public delegate void BuildCompleteDelegate(string targetName, long buildTime);
    public event BuildCompleteDelegate? OnBuildCompleteMessage;

    public event Action<byte[]>? OnDataMessageReceived;

    public override string Name => "build-runner";

    public override void OnStringMessage(string message)
    {
        throw new NotImplementedException();
    }

    public override void OnDataMessage(byte[] data)
    {
        OnDataMessageReceived?.Invoke(data);
    }

    public override void OnJsonMessage(JObject payload)
    {
        var status = payload["Status"]?.ToObject<string>() ?? throw new NullReferenceException();

        switch (status)
        {
            case "Building":
                break;

            case "Complete":
                var targetName =
                    payload["TargetName"]?.ToString() ?? throw new NullReferenceException();
                var buildTime = payload["Time"]?.Value<long>() ?? 0;
                OnBuildCompleteMessage?.Invoke(targetName, buildTime);
                break;

            case "Error":
                Console.WriteLine($"Error: {payload["Message"]}");
                break;
        }
    }
}
