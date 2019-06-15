using Discord;
using Discord.WebSocket;
using Discord.Commands;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordYoutubeDL
{
    // TODO: Refactor for Discord.Net 2.0
    class Program
    {
        private readonly IConfiguration _config;
        private DiscordSocketClient _client;

        static void Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2Support", true);
            new Program().MainAsync().GetAwaiter().GetResult();
        }

        public async Task MainAsync()
		{
            if (_config.GetValue<bool>("debug", false))
            {
                Console.WriteLine("Hello World!");
                string msg = "";
                foreach (System.Collections.Generic.KeyValuePair<string,string> i in _config.AsEnumerable())
                    msg += i.ToString();
                await Log(new LogMessage(LogSeverity.Debug, "Config Dump", msg));
            }

            using (var services = ConfigureServices())
            {
                var client = services.GetRequiredService<DiscordSocketClient>();
                _client = client;

                client.Log += Log;

                services.GetRequiredService<CommandService>().Log += Log;

                await client.LoginAsync(TokenType.Bot, _config["api_token"]);
                // Set bot status
                await client.SetStatusAsync(UserStatus.Online);
                // await _client.SetGameAsync("");
                await client.StartAsync();

                await services.GetRequiredService<CommandHandler>().InitializeAsync();

                // Block this task until the program is closed.
                await Task.Delay(-1);
            }
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());

	        if (_client != null && _client.ConnectionState == ConnectionState.Connected && _client.LoginState == LoginState.LoggedIn &&
            _config.GetValue<bool>("debug", false))
		        _client.SetGameAsync(msg.ToString(), "https://github.com/varGeneric/DiscordYoutubeDownloader/");
            return Task.CompletedTask;
        }

        public Program()
        {
            var builder = new ConfigurationBuilder()  // Create a new instance of the config builder
            #if DEBUG
                .SetBasePath(AppContext.BaseDirectory + "../../..")
            #else
                .SetBasePath(AppContext.BaseDirectory)
            #endif
                .AddXmlFile("_configuration.xml", optional: false, reloadOnChange: true);
            _config = builder.Build(); // Build the configuration
        }
        
        private ServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                .AddSingleton(_config)
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton<CommandService>()
                .AddSingleton<CommandHandler>()
                .BuildServiceProvider();
        }
    }
}