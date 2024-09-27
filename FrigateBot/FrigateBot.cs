using Discord;
using Discord.WebSocket;
using Serilog;
using Serilog.Events;
using System.Buffers.Text;
using System.Text;

namespace FrigateBot
{
    public sealed class FrigateBot
    {
        private const string configureCommandName = "configure";
        private const string configureCctvChannelOptionName = "cctv-channel";

        private readonly ILogger logger;
        private readonly State state;
        private readonly TimeSpan frigatePollInterval;
        private readonly FrigateClient frigate;
        private readonly DiscordSocketClient discord;
        private readonly string discordToken;
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

            discord.JoinedGuild += Discord_JoinedGuild;
            discord.Ready += Discord_Ready;
            discord.Disconnected += Discord_Disconnected;
            discord.Log += Discord_Log;
            discord.SlashCommandExecuted += Discord_SlashCommandExecuted;
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
                .WithDescription("Configure Astolfo Bot")
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

                    var eventsResult = await frigate.GetEventsAsync(state.LastCompletedEventStartUtc);

                    if (eventsResult is not null && eventsResult.Count > 0)
                    {
                        foreach (var @event in eventsResult)
                        {
                            var thumbnailStream = default(MemoryStream);
                            if (!string.IsNullOrEmpty(@event.Thumbnail))
                            {
                                var thumbnailBase64 = Encoding.UTF8.GetBytes(@event.Thumbnail);
                                Base64.DecodeFromUtf8InPlace(thumbnailBase64, out var thumbnailBytes);
                                thumbnailStream = new(thumbnailBase64, 0, thumbnailBytes);
                            }

                            logger.Information("Notifying about event {Id} from {StartTime} to {EndTime}", @event.Id, @event.StartTime, @event.EndTime);

                            foreach (var guild in discord.Guilds)
                            {
                                if (state.CctvChannelByGuild.TryGetValue(guild.Id, out var channelId))
                                {
                                    if (guild.GetTextChannel(channelId) is var cctvChannel and not null)
                                    {
                                        var messageText = $"Camera `{@event.Camera}` detected `{@event.Label}` on <t:{@event.StartTime.ToUnixTimeSeconds()}:d> between <t:{@event.StartTime.ToUnixTimeSeconds()}:T> and <t:{@event.EndTime.ToUnixTimeSeconds()}:T>.";

                                        if (thumbnailStream is not null)
                                        {
                                            await cctvChannel.SendFilesAsync([new FileAttachment(thumbnailStream, $"{@event.Id}.jpg", description: @event.Label)], messageText);
                                        }
                                        else
                                        {
                                            await cctvChannel.SendMessageAsync(messageText);
                                        }
                                    }
                                    else
                                    {
                                        logger.Error("Guild channel {ChannelId} does not exist", channelId);
                                    }
                                }
                            }

                            // does this event end after the current persist
                            if ((!state.LastCompletedEventStartUtc.HasValue || state.LastCompletedEventStartUtc.Value < @event.StartTime))
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
}
