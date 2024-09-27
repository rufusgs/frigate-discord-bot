# About
I wrote this to send Frigate NVR events to a private discord server. It's very basic and currently could easily fail to deliver a message about an event.

# Installing
1. Create a discord application to host your bot: https://discord.com/developers/applications
    1. Under Installation -> Installation Contexts choose Guild Install but not User Install
    2. Under Installation -> Install Link choose None
    3. Under Bot -> Disable Public Bot
    4. Under OAuth2 -> OAuth2 URL Generator select `bot` and `applications.commands` as scopes, then under bot permissions select `Send Messages` and `Attach Files`.
    5. Open the generated OAuth2 URL and authorise the bot for the server where you want the bot to relay Frigate events.
    6. Verify that your bot has joined the server.
    7. Under Bot -> Token choose Reset Token and copy down the authorisation token.
2. Configure permissions for your bot's automatically generated role. Ensure that it has permission to view, post and attach files to the text channel where you want it to relay events.
3. Configure your Frigate instance to expose the API port locally:
```
services:
  frigate:
    ports:
      - "127.0.0.1:5000:5000" # Internal unauthenticated access. Expose carefully.
```
4. Install .NET 8 runtime on your system: https://learn.microsoft.com/en-us/dotnet/core/install/linux
5. Download and unzip the latest release (or build it from source with `dotnet publish`). You might want to configure a separate user with limited privileges and a systemd service for it so it starts up automatically. Note that the bot stores its (very limited) state in a json file called `state.json` by default so it will need write access to that file.
6. Modify `appsettings.json` and enter the discord token you generated back in step 1.
7. Run the bot: `dotnet FrigateBot.dll`
8. Once the bot has finished starting up, you can tell it which channel it should post events in in your discord server by using the discord slash command `/configure cctv-channel #channel-name`
