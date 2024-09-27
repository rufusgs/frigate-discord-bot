using FrigateBot;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddUserSecrets<Program>()
    .Build();

var options = config.Get<FrigateBotOptions>() ?? new();

if (string.IsNullOrEmpty(options.DiscordToken))
{
    throw new Exception("Missing DiscordToken. Please specify a DiscordToken.");
}

var frigateBot = new FrigateBot.FrigateBot(options);
await frigateBot.ExecuteAsync();