using MainServer.Configs;
using MainServer.Services.Client;
using SocketServer;

namespace MainServer;

internal static class BuildRunnerManager
{
    private static readonly Dictionary<string, BuildRunnerClientService> _buildRunners = new();
    public static IEnumerable<BuildRunnerClientService> All => _buildRunners.Values;

    public static void Init(List<BuildRunnerConfig>? configRunners)
    {
        if (configRunners is null)
            return;

        foreach (var runner in configRunners)
        {
            var client = new Client(runner.Ip!, runner.Port);
            _buildRunners.Add(runner.Id!, new BuildRunnerClientService(client));
        }
    }

    public static BuildRunnerClientService GetOffloadServer(string operatingSystem)
    {
        return _buildRunners.Values.FirstOrDefault(x => x.OperatingSystem == operatingSystem)
            ?? throw new NullReferenceException(
                $"Server not found with operating system: {operatingSystem}"
            );
    }
}
