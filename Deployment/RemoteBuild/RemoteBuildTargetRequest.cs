﻿using System.Net;
using System.Runtime.InteropServices;
using Deployment.Configs;
using Deployment.Misc;
using Deployment.Server;
using SharedLib;

namespace Deployment.RemoteBuild;

/// <summary>
/// Class send across network to do remote builds
/// </summary>
public class RemoteBuildTargetRequest : IRemoteControllable
{
	/// <summary>
	/// Workspace name from the master server. Remapping is done on offload server
	/// </summary>
	public string? WorkspaceName { get; init; }
	public string? SendBackUrl { get; init; }
	public TargetConfig? Config { get; init; }
	
	/// <summary>
	/// 
	/// </summary>
	/// <returns>BuildId</returns>
	public async Task<string> ProcessAsync()
	{
		var buildId = Guid.NewGuid().ToString();
		Console.WriteLine($"Created buildId: {buildId}");
		StartBuilder(buildId);
		await Task.CompletedTask;
		return buildId;
	}
	
	/// <summary>
	/// Fire and forget method for starting a build
	/// </summary>
	/// <param name="buildId"></param>
	/// <exception cref="WebException"></exception>
	private async void StartBuilder(string buildId)
	{
		var mapping = new WorkspaceMapping();
		var workspaceName = mapping.GetRemapping(WorkspaceName);
		var workspace = Workspace.GetWorkspaceFromName(workspaceName);
		Environment.CurrentDirectory = workspace.Directory;
		
		var builder = new LocalUnityBuild(workspace.UnityVersion);
		var success = await builder.Build(Config);

		RemoteBuildResponse response;
		
		if (success)
		{
			// send web request back to sender with zip folder of build
			var base64 = await FilePacker.PackAsync(Config.BuildPath);
			response = new RemoteBuildResponse
			{
				Request = this,
				BuildId = buildId,
				Base64 = base64
			};
		}
		else
		{
			// send web request to sender about the build failing
			response = new RemoteBuildResponse
			{
				Request = this,
				BuildId = buildId,
				Error = "build failed for reasons"
			};
		}

		// build is done or failed, tell sender about it
		var body = new RemoteBuildPacket { BuildResponse = response };
		var res =  await Web.SendAsync(HttpMethod.Post, SendBackUrl, DeviceInfo.UniqueDeviceId, body);
		if (res.StatusCode != HttpStatusCode.OK)
			throw new WebException(res.Reason);
	}
}