using Deployment.Deployments;
using Deployment.Misc;
using Deployment.Server.Config;
using Deployment.Webhooks;

namespace Deployment.RemoteBuild;

public class RemoteClanforgeImageUpdate : IRemoteControllable
{
	public ClanforgeConfig? Config { get; set; }
	public string? Desc { get; set; }

	public string Process()
	{
		ProcessInternalAsync().FireAndForget();
		return "ok";
	}

	private async Task ProcessInternalAsync()
	{
		try
		{
			var clanforge = new ClanForgeDeploy(Config, Desc);
			await clanforge.Deploy();
			SendHook(Desc, Config?.BuildHookMessage("Updated"));
		}
		catch (Exception e)
		{
			SendHook(Config?.BuildHookMessage($"Failed ({e.GetType().Name})"), e.Message, true);
		}
	}

	private static void SendHook(string? header, string? message, bool isError = false)
	{
		if (ServerConfig.Instance.Hooks == null)
			return;

		foreach (var hook in ServerConfig.Instance.Hooks)
		{
			if (hook.IsDiscord())
				Discord.PostMessage(hook.Url, message, hook.Title, header, isError ? Discord.Colour.RED : Discord.Colour.GREEN);
			else if (hook.IsSlack())
				Slack.PostMessage(hook.Url, $"{hook.Title} | {header}\n{message}");
		}
	}
}