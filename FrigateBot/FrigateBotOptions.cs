namespace FrigateBot
{
    public sealed class FrigateBotOptions
    {
        public string StatePath { get; set; } = "state.json";
        public int FrigatePollIntervalSeconds { get; set; } = 5;
        public string FrigateAddress { get; set; } = "http://127.0.0.1:5000/";
        public string DiscordToken { get; set; } = "";
    }
}