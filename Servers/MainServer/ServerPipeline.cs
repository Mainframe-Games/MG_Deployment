﻿using System.Diagnostics;
using MainServer.Configs;
using MainServer.Deployment;
using MainServer.Hooks;
using MainServer.Services.Client;
using MainServer.Services.Packets;
using MainServer.Workspaces;
using SocketServer.Messages;

namespace MainServer;

internal class ServerPipeline(
    Guid projectGuid,
    Workspace workspace,
    List<string> buildTargets,
    ServerConfig serverConfig
)
{
    public static List<Guid> ActiveProjects { get; } = [];
    private readonly List<BuildRunnerProcess> buildProcesses = [];

    public async void Run()
    {
        BuildRunnerClientService.OnBuildCompleteMessage += OnBuildCompleted;
        ActiveProjects.Add(projectGuid);
        {
            var sw = Stopwatch.StartNew();
            var startTime = DateTime.Now;

            // changelog
            var changeLog = workspace.GetChangeLog();

            // version bump
            var fullVersion = RunVersionBump();
            Console.WriteLine($"Pre Build Complete\n  time: {sw.ElapsedMilliseconds}ms");
            sw.Restart();

            // builds
            await RunBuildAsync();
            Console.WriteLine($"Build Complete\n  time: {sw.ElapsedMilliseconds}ms");
            sw.Restart();

            // deploys
            await RunDeploy(fullVersion, changeLog);
            Console.WriteLine($"Deploy Complete\n  time: {sw.ElapsedMilliseconds}ms");
            sw.Restart();

            // hooks
            RunHooks(changeLog, fullVersion, sw.ElapsedMilliseconds);
            Console.WriteLine($"hooks Complete\n  time: {sw.ElapsedMilliseconds}ms");
            sw.Restart();

            // apply tag
            workspace.Tag($"v{fullVersion}");

            Console.WriteLine("############################################");
            Console.WriteLine("# Project build completed");
            Console.WriteLine($"# {workspace.ProjectName}");
            Console.WriteLine($"# {projectGuid}");
            Console.WriteLine($@"# TotalTime: {DateTime.Now - startTime:h\:mm\:ss}");
            Console.WriteLine("############################################");
            sw.Restart();
        }
        ActiveProjects.Remove(projectGuid);
        BuildRunnerClientService.OnBuildCompleteMessage -= OnBuildCompleted;
    }

    private string RunVersionBump()
    {
        var standalone = buildTargets.Any(
            x =>
                x.Contains("Windows", StringComparison.OrdinalIgnoreCase)
                || x.Contains("Linux", StringComparison.OrdinalIgnoreCase)
                || x.Contains("OSX", StringComparison.OrdinalIgnoreCase)
        );
        var android = buildTargets.Any(x => x.Contains("Android"));
        var ios = buildTargets.Any(x => x.Contains("iOS"));
        var fullVersion = workspace.VersionBump(standalone, android, ios);
        return fullVersion;
    }

    #region Build

    private async Task RunBuildAsync()
    {
        var runnerMap = new Dictionary<BuildRunnerClientService, BuildRunnerPacket>();

        // start processes
        foreach (var buildTarget in buildTargets)
        {
            var runner = GetUnityRunner(buildTarget, false);

            if (!runnerMap.TryGetValue(runner, out var value))
            {
                value = new BuildRunnerPacket
                {
                    ProjectGuid = projectGuid,
                    BuildTargets = [],
                    GitUrl = workspace.GitUrl!,
                    Branch = workspace.Branch!,
                };
                runnerMap[runner] = value;
            }

            value.BuildTargets.Add(buildTarget);
            var process = new BuildRunnerProcess(buildTarget);
            buildProcesses.Add(process);
        }

        foreach (var runner in runnerMap)
            await runner.Key.SendJson(runner.Value.ToJson());

        // wait for them all to finish
        while (buildProcesses.Any(p => !p.IsComplete))
            await Task.Delay(1000);
    }

    private void OnBuildCompleted(string targetName, long buildTime, string outputDirectoryName)
    {
        foreach (var process in buildProcesses)
            process.OnStringMessage(targetName, buildTime, outputDirectoryName);
    }

    private static BuildRunnerClientService GetUnityRunner(string targetName, bool isIL2CPP)
    {
        if (!isIL2CPP)
            return ClientServicesManager.GetDefaultRunner();
        if (targetName.Contains("Windows"))
            return ClientServicesManager.GetRunner(OperationSystemType.Windows);
        if (targetName.Contains("OSX"))
            return ClientServicesManager.GetRunner(OperationSystemType.MacOS);
        if (targetName.Contains("Linux"))
            return ClientServicesManager.GetRunner(OperationSystemType.Linux);

        throw new NotSupportedException($"Target not supported: {targetName}");
    }

    #endregion

    #region Deploy

    private async Task RunDeploy(string fullVersion, string[] changeLog)
    {
        var deployRunner = new DeploymentRunner(
            workspace,
            buildProcesses,
            fullVersion,
            changeLog,
            serverConfig
        );
        await deployRunner.Deploy();
    }

    #endregion

    private void RunHooks(string[] changeLog, string fullVersion, long elapsedMilliseconds)
    {
        var buildResults = buildProcesses
            .Select(p => (p.BuildName, TimeSpan.FromMilliseconds(p.TotalBuildTime)))
            .ToList();
        var hookRunner = new HooksRunner(
            workspace,
            TimeSpan.FromMilliseconds(elapsedMilliseconds),
            buildResults,
            changeLog,
            fullVersion
        );
        hookRunner.Run();
    }
}

internal class BuildRunnerProcess(string buildName)
{
    public readonly string BuildName = buildName;
    public bool IsComplete { get; private set; }
    public long TotalBuildTime { get; private set; }
    public string OutputDirectoryName { get; private set; } = string.Empty;

    public void OnStringMessage(string targetName, long buildTime, string outputDirectoryName)
    {
        if (targetName != BuildName)
            return;

        TotalBuildTime = buildTime;
        IsComplete = true;
        OutputDirectoryName = outputDirectoryName;
    }
}
