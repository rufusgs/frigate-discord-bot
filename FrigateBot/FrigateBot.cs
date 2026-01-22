using Discord;
using Discord.WebSocket;
using Serilog;
using Serilog.Events;
using System.Text;

namespace FrigateBot;

public sealed class FrigateBot
{
    private const string configureCommandName = "configure";
    private const string configureCctvChannelOptionName = "cctv-channel";
    private const string postClipCommandPrefix = "post-clip-";

    private static readonly Emoji filmFramesEmoji = new("🎞️");
    private readonly ILogger logger;
    private readonly State state;
    private readonly TimeSpan frigatePollInterval;
    private readonly FrigateClient frigate;
    private readonly DiscordSocketClient discord;
    private readonly string discordToken;
    private readonly bool showDetections;
    private readonly TimeSpan maxIncompleteEventAge;
    private TaskCompletionSource discordReadyTcs;

    public FrigateBot(FrigateBotOptions options, ILogger logger)
    {
        this.logger = logger;
        state = State.Load(options.StatePath);
        frigatePollInterval = TimeSpan.FromSeconds(options.FrigatePollIntervalSeconds);
        frigate = new(options.FrigateAddress);
        discordReadyTcs = new();
        discord = new(new()
        {
            GatewayIntents = GatewayIntents.Guilds,
            MaxWaitBetweenGuildAvailablesBeforeReady = int.MaxValue,
        });
        discordToken = options.DiscordToken;
        showDetections = options.ShowDetections;
        maxIncompleteEventAge = TimeSpan.FromSeconds(options.MaxIncompleteEventAgeSeconds);

        discord.JoinedGuild += Discord_JoinedGuild;
        discord.Ready += Discord_Ready;
        discord.Disconnected += Discord_Disconnected;
        discord.Log += Discord_Log;
        discord.SlashCommandExecuted += Discord_SlashCommandExecuted;
        discord.ButtonExecuted += Discord_ButtonExecuted;
    }

    private Task Discord_Log(LogMessage msg)
    {
        // this is the only way I could see to subscribe to this info
        if (msg.Message.StartsWith("Resumed previous session") && msg.Source == "Gateway")
        {
            discordReadyTcs.TrySetResult();
        }

        var serilogSeverity = msg.Severity switch
        {
            LogSeverity.Critical or LogSeverity.Error => LogEventLevel.Error,
            LogSeverity.Warning => LogEventLevel.Warning,
            LogSeverity.Info => LogEventLevel.Information,
            LogSeverity.Verbose => LogEventLevel.Verbose,
            LogSeverity.Debug => LogEventLevel.Debug,
            var unknownSeverity => throw new ArgumentException($"Unknown log severity {unknownSeverity}", nameof(msg)),
        };

        logger.Write(serilogSeverity, msg.Exception, "Discord message {Message} from {Source}", msg.Message, msg.Source);

        return Task.CompletedTask;
    }

    private async Task Discord_SlashCommandExecuted(SocketSlashCommand command)
    {
        switch (command.CommandName)
        {
            case configureCommandName:
                await HandleConfigureCommand(command);
                break;
            default:
                await command.RespondAsync("Unknown command");
                break;
        }
    }

    private async Task Discord_ButtonExecuted(SocketMessageComponent component)
    {
        if (component.Data.CustomId?.StartsWith(postClipCommandPrefix) ?? false)
        {
            var eventId = component.Data.CustomId[postClipCommandPrefix.Length..];
            if (string.IsNullOrEmpty(eventId))
            {
                await component.RespondAsync("No frigate event ID specified", ephemeral: true);
            }
            else
            {
                await component.DeferLoadingAsync();

                // service the request on the thread pool rather than blocking the gateway
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var clipStream = await frigate.GetEventClipAsync(eventId);
                        if (clipStream is null)
                        {
                            logger.Warning("Unable to retrieve clip for event when responding to button press.");
                            await component.FollowupAsync("Unknown frigate event ID", ephemeral: true);
                        }
                        else
                        {
                            logger.Information("{User} requested clip upload for event {Id}.", component.User.GlobalName, eventId);
                            await component.FollowupWithFileAsync(clipStream, $"{eventId}.mp4", $"{component.User.Mention} requested clip upload.");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warning(ex, "Exception while attempting to retrieve or upload clip for event when responding to button press.");
                        await component.FollowupAsync($"Error while attempting to retrieve or upload clip for event `{eventId}`", ephemeral: true);
                    }
                });
            }
        }
        else
        {
            await component.RespondAsync("Unknown button executed", ephemeral: true);
        }
    }

    private async Task HandleConfigureCommand(SocketSlashCommand command)
    {
        var setting = command.Data.Options.FirstOrDefault();
        switch (setting?.Name)
        {
            case configureCctvChannelOptionName:
                if (command.GuildId.HasValue && setting.Options.FirstOrDefault()?.Value is ITextChannel textChannel)
                {
                    if (state.CctvChannelByGuild.TryGetValue(command.GuildId.Value, out var existingTextChannelId) && existingTextChannelId != textChannel.Id)
                    {
                        logger.Information("Changing CCTV channel for guild {GuildId} from {ExistingChannelId} to {NewChannelId}", command.GuildId.Value, existingTextChannelId, textChannel.Id);
                    }
                    state.Alter(state => state.CctvChannelByGuild[command.GuildId.Value] = textChannel.Id);
                    await command.RespondAsync("CCTV channel updated!");
                }
                else
                {
                    await command.RespondAsync("Please select a text channel.");
                }
                break;
            default:
                await command.RespondAsync("Unknown setting");
                break;
        }
    }

    private Task Discord_Disconnected(Exception arg)
    {
        if (discordReadyTcs.Task.IsCompleted)
        {
            discordReadyTcs = new();
        }

        return Task.CompletedTask;
    }

    private async Task Discord_Ready()
    {
        foreach (var guild in discord.Guilds)
        {
            await RegisterGuildCommandsAsync(guild);
        }

        discordReadyTcs.TrySetResult();
    }

    private async Task Discord_JoinedGuild(SocketGuild guild)
    {
        state.Alter(state =>
        {
            state.CctvChannelByGuild.Remove(guild.Id);
            state.ConfigureCommandByGuildId.Remove(guild.Id);
        });
        await RegisterGuildCommandsAsync(guild);
    }

    private async Task RegisterGuildCommandsAsync(SocketGuild guild)
    {
        if (!state.ConfigureCommandByGuildId.TryGetValue(guild.Id, out var configureCommandId) || await guild.GetApplicationCommandAsync(configureCommandId) is var configureCommand and null)
        {
            var newConfigureCommand = new SlashCommandBuilder()
            .WithName(configureCommandName)
            .WithDescription($"Configure {discord.CurrentUser.GlobalName ?? "Frigate Bot"}")
                .WithDefaultMemberPermissions(GuildPermission.Administrator)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName(configureCctvChannelOptionName)
                    .WithDescription("Set the CCTV alerts channel")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("channel", ApplicationCommandOptionType.Channel, "The CCTV alerts channel where this bot will send alerts.")
                );
            var commandRegistration = await discord.Rest.CreateGuildCommand(newConfigureCommand.Build(), guild.Id);
            state.Alter(state => state.ConfigureCommandByGuildId[guild.Id] = commandRegistration.Id);
        }
    }

    public async Task ExecuteAsync()
    {
        await discord.LoginAsync(TokenType.Bot, discordToken);
        await discord.StartAsync();


        while (true)
        {
            try
            {
                await discordReadyTcs.Task;

                var now = DateTimeOffset.Now;
                var eventsResult = await frigate.GetEventsAsync(state.LastCompletedEventStartUtc);

                if (eventsResult is not null && eventsResult.Count > 0)
                {
                    foreach (var @event in eventsResult)
                    {
                        if (!@event.EndTime.HasValue && (@event.StartTime >= now - maxIncompleteEventAge))
                        {
                            logger.Verbose("Deferring notification about event {Id} which is not yet complete", @event.Id);
                            break;
                        }

                        if (showDetections || string.Equals(@event.Data?.MaxSeverity, "alert"))
                        {
                            Stream? previewStream;
                            try
                            {
                                previewStream = await frigate.GetEventPreviewAsync(@event.Id);
                                // TODO: fallback to thumbnail if no preview
                                // thumbnailStream = await frigate.GetEventThumbnailAsync(@event.Id, "jpg");
                            }
                            catch (Exception ex)
                            {
                                previewStream = null;
                                logger.Warning(ex, "Failed to retrieve thumbnail for event {Id}", @event.Id);
                            }

                            logger.Information("Notifying about event {Id} from {StartTime} to {EndTime}", @event.Id, @event.StartTime, @event.EndTime);

                            foreach (var guild in discord.Guilds)
                            {
                                if (state.CctvChannelByGuild.TryGetValue(guild.Id, out var channelId))
                                {
                                    if (guild.GetTextChannel(channelId) is var cctvChannel and not null)
                                    {
                                        var messageText = new StringBuilder("Camera `");
                                        messageText.Append(@event.Camera);
                                        messageText.Append("` detected `");
                                        messageText.Append(@event.Label);
                                        messageText.Append('`');

                                        if (!string.IsNullOrWhiteSpace(@event.SubLabel))
                                        {
                                            messageText.Append(" (`");
                                            messageText.Append(@event.SubLabel);
                                            messageText.Append("`)");
                                        }

                                        if (@event.Data?.Score != null)
                                        {
                                            messageText.AppendFormat(" with {0:P} confidence", @event.Data.Score);
                                        }

                                        messageText.Append(" on <t:");
                                        messageText.Append(@event.StartTime.ToUnixTimeSeconds());
                                        if (@event.EndTime.HasValue)
                                        {
                                            messageText.Append(":d> between <t:");
                                            messageText.Append(@event.StartTime.ToUnixTimeSeconds());
                                            messageText.Append(":T> and <t:");
                                            messageText.Append(@event.EndTime.Value.ToUnixTimeSeconds());
                                            messageText.Append(":T>.");
                                        }
                                        else
                                        {
                                            messageText.Append(":d> at <t:");
                                            messageText.Append(@event.StartTime.ToUnixTimeSeconds());
                                            messageText.Append(":T> (event is still ongoing).");
                                        }

                                        var postClipButton = new ComponentBuilder()
                                            .WithButton("Post Clip", $"post-clip-{@event.Id}", emote: filmFramesEmoji)
                                            .Build();

                                        if (previewStream is not null)
                                        {
                                            await cctvChannel.SendFilesAsync([new FileAttachment(previewStream, $"{@event.Id}.gif", description: @event.Label)], messageText.ToString(), components: postClipButton);
                                        }
                                        else
                                        {
                                            await cctvChannel.SendMessageAsync(messageText.ToString(), components: postClipButton);
                                        }
                                    }
                                    else
                                    {
                                        logger.Error("Guild channel {ChannelId} does not exist", channelId);
                                    }
                                }
                            }
                        }
                        else
                        {
                            logger.Verbose("Ignoring detection level event {Id}", @event.Id);
                        }

                        // does this event end after the current persist
                        if (!state.LastCompletedEventStartUtc.HasValue || state.LastCompletedEventStartUtc.Value < @event.StartTime)
                        {
                            var newCutoff = @event.StartTime.AddSeconds(1);
                            logger.Information("Advancing event query start time to {NewCutoff}", newCutoff);
                            state.Alter(state => state.LastCompletedEventStartUtc = newCutoff);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during main loop");
            }
            finally
            {
                await Task.Delay(frigatePollInterval);
            }
        }
    }
}
