﻿using Discord;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using SharedLib;

namespace DiscordBot.Commands;

public class GameServerUpdateCommand : Command
{
	public string? BuildServerUrl { get; set; }

	public GameServerUpdateCommand(string? commandName, string? description) : base(commandName, description)
	{
	}

	public override SlashCommandProperties Build()
	{
		var opt = new SlashCommandOptionBuilder()
			.WithName("backend")
			.WithDescription("UGS or Clanforge backend")
			.WithType(ApplicationCommandOptionType.Integer)
			.AddChoice("usg", 0)
			.AddChoice("clanforge", 1);
		
		return CreateCommand()
			.AddOption(opt)
			.Build();
	}

	public override async Task ExecuteAsync(SocketSlashCommand command)
	{
		try
		{
			await command.DeferAsync();
			
			// TODO: Dynamic way of getting options

			var body = new JObject
			{
				["gameServerUpdate"] = new JObject
				{

				}
			};
			
			var res = await Web.SendAsync(HttpMethod.Post, BuildServerUrl, body: body);
			await command.RespondSuccessDelayed(command.User, "Game Server Update Started", res.Content);
		}
		catch (Exception e)
		{
			Logger.Log(e);
			await command.RespondErrorDelayed(command.User, "Build Server request failed", e.Message);
		}
	}

	public override async Task ModifyOptions(SocketSlashCommand command, SocketInteraction interaction)
	{
		// Get the value of the first option
		var firstOption = command.Data.Options.FirstOrDefault(x => x.Name == "backend");

		if (firstOption == null)
			return;

		// Modify the original response based on the first option's value
		var firstOptionValue = (int)(long)firstOption.Value;
		// var newOptionName = firstOptionValue == 0 ? "backend" : "";
		var newOptions = GetNewOptions(firstOptionValue); // Implement this method to return the dynamic options based on the first option's value

		// Create a new message component with the modified options
		var component = new ComponentBuilder()
			.WithSelectMenu("second_option", newOptions)
			.Build();

		// Modify the original response to update the options
		await interaction.ModifyOriginalResponseAsync(x =>
		{
			x.Content = "Options have been updated.";
			x.Components = new Optional<MessageComponent>(component);
		});
	}

	private static List<SelectMenuOptionBuilder> GetNewOptions(int optionIndex)
	{
		return optionIndex switch
		{
			// ugs
			0 => new List<SelectMenuOptionBuilder>
			{
			},
			
			// clanforge
			1 => new List<SelectMenuOptionBuilder>
			{
				new()
				{
					Label = "profile",
					Description = "Profile to update on clanforge"
				}
			},
			
			_ => throw new ArgumentException($"Index not recognised: {optionIndex}")
		};
	}
}