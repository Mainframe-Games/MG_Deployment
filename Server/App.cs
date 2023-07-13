﻿using System.Reflection;
using Deployment;
using Deployment.Configs;
using Deployment.Deployments;
using Deployment.Server.Unity;
using Server.Configs;
using Server.RemoteBuild;
using SharedLib;

namespace Server;

public static class App
{
	public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
	private static string? RootDirectory { get; set; }
	private static ListenServer? Server { get; set; }
	private static ServerConfig? Config { get; set; }
	private static bool IsLocal { get; set; }

	private static ulong NextPipelineId { get; set; }
	public static readonly Dictionary<ulong, BuildPipeline> Pipelines = new();

	public static async Task RunAsync(Args args)
	{
		Config = ServerConfig.Load();
		RootDirectory = Environment.CurrentDirectory;
		IsLocal = args.IsFlag("-local");

		// for locally running the build process without a listen server
		if (IsLocal)
		{
			var workspace = Workspace.AskWorkspace();
			var pipeline = CreateBuildPipeline(workspace, args);
			if (pipeline.ChangeLog.Length == 0)
			{
				Logger.Log("No changes to build");
				return;
			}
			await RunBuildPipe(pipeline);
		}
		else
		{
			args.TryGetArg("-ip", out var ip, Config.IP);
			args.TryGetArg("-port", out var port, Config.Port.ToString());
			Server = new ListenServer(ip, ushort.Parse(port));
			
			if (Config.AuthTokens is { Count: > 0 })
			{
				Server.GetAuth = () =>
				{
					Config.Refresh();
					return Config.AuthTokens;
				};
			}
			// server should wait for ever
			await Server.RunAsync();
			Logger.Log("Server stopped");
		}
	}

	public static void DumpLogs()
	{
		Logger.WriteToFile(RootDirectory, true);
		Server?.CheckIfServerStillListening();
		Environment.CurrentDirectory = RootDirectory; // reset cur dir back to root of exe
	}

	#region Build Pipeline

	public static BuildPipeline CreateBuildPipeline(Workspace workspace, Args args)
	{
		var parallel = Config?.Offload?.Parallel ?? false;
		var targets = Config?.Offload?.Targets ?? null;
		var offloadUrl = Config?.OffloadServerUrl;
		var pipeline = new BuildPipeline(NextPipelineId++, workspace, args, offloadUrl, parallel, targets);
		return pipeline;
	}
	
	public static async Task RunBuildPipe(BuildPipeline pipeline)
	{
		Pipelines.Add(pipeline.Id, pipeline);
		
		pipeline.OffloadBuildNeeded += SendRemoteBuildRequest;
		pipeline.GetExtraHookLogs += BuildPipelineOnGetExtraHookLog;
		pipeline.DeployEvent += BuildPipelineOnDeployEvent;
		
		var isSuccessful = await pipeline.RunAsync();
		
		if (isSuccessful)
			DumpLogs();
		
		Pipelines.Remove(pipeline.Id);
	}
	
	/// <summary>
	/// Called from main build server. Sends web request to offload server and gets a buildId in return
	/// </summary>
	private static void SendRemoteBuildRequest(OffloadServerPacket offloadPacket)
	{
		var remoteBuild = new RemoteBuildTargetRequest
		{
			SendBackUrl = $"http://{Config.IP}:{Config.Port}",
			Packet = offloadPacket,
		};
		
		var body = new RemoteBuildPacket { BuildTargetRequest = remoteBuild };
		Web.SendAsync(HttpMethod.Post, Config.OffloadServerUrl, body: body)
			.FireAndForget(e =>
			{
				Logger.Log($"Error at {nameof(SendRemoteBuildRequest)}: {e}");
			});
	}

	private static async Task BuildPipelineOnDeployEvent(BuildPipeline pipeline)
	{
		var buildVersionTitle = pipeline.BuildVersionTitle;
		
		// client deploys
		DeployApple(pipeline); // apple first as apple takes longer to process on appstore connect
		await DeployGoogle(pipeline, buildVersionTitle);
		DeploySteam(pipeline, buildVersionTitle);
		
		// server deploys
		await DeployClanforge(pipeline, buildVersionTitle);
		await DeployToS3Bucket(pipeline);
	}

	private static async Task DeployToS3Bucket(BuildPipeline pipeline)
	{
		if (Config?.S3 == null || pipeline.Config.Deploy?.S3 is not true)
			return;
		
		// upload to s3
		var pathToBuild = pipeline.Config.GetBuildTarget(UnityTarget.Linux64, true).BuildPath;
		var s3 = new AmazonS3Deploy(Config.S3.AccessKey, Config.S3.SecretKey, Config.S3.BucketName);
		await s3.DeployAsync(pathToBuild);
		
		if (Config.Ugs?.ServerHosting == null)
			return;

		if (Config.Ugs.ServerHosting.BuildId == 0)
			throw new Exception("Invalid build Id");

		var project = Config.Ugs.GetProjectFromName(pipeline.Workspace.Name);
		var gameServer = new UnityGameServerRequest(Config.Ugs.KeyId, Config.Ugs.SecretKey);
		await gameServer.CreateNewBuildVersion(
			project.ProjectId,
			project.EnvironmentId,
			Config.Ugs.ServerHosting.BuildId,
			Config.S3.Url,
			Config.S3.AccessKey,
			Config.S3.SecretKey);

		Logger.Log("Unity server updated");
	}

	private static async Task DeployClanforge(BuildPipeline pipeline, string buildVersionTitle)
	{
		if (pipeline.Config.Deploy?.Clanforge is null or false)
			return;

		pipeline.Args.TryGetArg("-setlive", out var beta, Config.Steam.DefaultSetLive);
		pipeline.Args.TryGetArg("-clanforge", out var profile, Config.Clanforge.DefaultProfile);
		var clanforge = new ClanForgeDeploy(Config.Clanforge, profile, buildVersionTitle, beta);
		await clanforge.Deploy();
	}

	private static void DeployApple(BuildPipeline pipeline)
	{
		if (pipeline.Config.Deploy == null || pipeline.Config.Deploy.AppleStore is null or false)
			return;
		
		var iosBuild = pipeline.Config.GetBuildTarget(UnityTarget.iOS);
		var buildSettingsAsset = iosBuild.GetBuildSettingsAsset(pipeline.Workspace.BuildSettingsDirPath);
		var productName = buildSettingsAsset.GetValue<string>("ProductName");
		var buildPath = buildSettingsAsset.GetValue<string>("BuildPath");
		var workingDir = Path.Combine(buildPath, productName);
		var exportOptionPlist = $"{pipeline.Workspace.Directory}/BuildScripts/ios/exportOptions.plist";

		if (!File.Exists(exportOptionPlist))
			throw new FileNotFoundException(exportOptionPlist);

		XcodeDeploy.Deploy(
			workingDir,
			Config.AppleStore.AppleId,
			Config.AppleStore.AppSpecificPassword,
			exportOptionPlist);
	}

	private static async Task DeployGoogle(BuildPipeline pipeline, string buildVersionTitle)
	{
		if (pipeline.Config.Deploy?.GoogleStore == null)
			return;
		
		var packageName = pipeline.Workspace.ProjectSettings.GetValue<string>("applicationIdentifier.Android");
		var changeLogArr = pipeline.ChangeLog;
		var changeLog = string.Join("\n", changeLogArr);
		var androidBuild = pipeline.Config.GetBuildTarget(UnityTarget.Android);
		var buildSettingsAsset = androidBuild.GetBuildSettingsAsset(pipeline.Workspace.BuildSettingsDirPath);
		var productName = buildSettingsAsset.GetValue<string>("ProductName");
		var buildPath = buildSettingsAsset.GetValue<string>("BuildPath");
		var path = Path.Combine(buildPath, $"{productName}.aab");
		var aabFile = new FileInfo(path);

		if (!aabFile.Exists)
			throw new FileNotFoundException($"aab file not found: {path}");

		await GooglePlayDeploy.Deploy(
			packageName,
			aabFile.FullName,
			Config.GoogleStore.CredentialsPath,
			Config.GoogleStore.ServiceUsername,
			buildVersionTitle,
			changeLog);
	}

	private static void DeploySteam(BuildPipeline pipeline, string buildVersionTitle)
	{
		var vdfPaths = pipeline.Config.Deploy?.Steam;
		
		if (vdfPaths == null)
			return;
		
		pipeline.Args.TryGetArg("-setlive", out var setLive, Config.Steam.DefaultSetLive);
		
		foreach (var vdfPath in vdfPaths)
		{
			var path = Config.Steam.Path;
			var password = Config.Steam.Password;
			var username = Config.Steam.Username;
			var steam = new SteamDeploy(vdfPath, password, username, path);
			steam.Deploy(buildVersionTitle, setLive);
		}
	}

	private static string? BuildPipelineOnGetExtraHookLog(BuildPipeline pipeline)
	{
		pipeline.Args.TryGetArg("-clanforge", out var profile, Config.Clanforge.DefaultProfile);
		return Config.Clanforge?.BuildHookMessage(profile, "Updated");
	}
	
	#endregion
}