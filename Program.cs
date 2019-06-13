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
            new Program().MainAsync().GetAwaiter().GetResult();
        }

        public async Task MainAsync()
		{
            #if DEBUG
            Console.WriteLine("Hello World!");
            string msg = "";
            foreach (System.Collections.Generic.KeyValuePair<string,string> i in _config.AsEnumerable())
                msg += i.ToString();
            Console.WriteLine(new LogMessage(LogSeverity.Debug, "Config Dump", msg));
            #endif

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

            #if DEBUG
	        if (_client.ConnectionState == ConnectionState.Connected && _client.LoginState == LoginState.LoggedIn)
		        _client.SetGameAsync(msg.ToString(), "https://github.com/varGeneric/DiscordYT-DL/");
            #endif
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
                .AddJsonFile("_configuration.json");
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