using FrigateBot;
using Microsoft.Extensions.Configuration;
using Serilog;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddUserSecrets<Program>()
    .Build();

var logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.File("log.txt")
    .CreateLogger();

var options = config.Get<FrigateBotOptions>() ?? new();

if (string.IsNullOrEmpty(options.DiscordToken))
{
    throw new Exception("Missing DiscordToken. Please specify a DiscordToken.");
}

var frigateBot = new FrigateBot.FrigateBot(options, logger);
await frigateBot.ExecuteAsync();