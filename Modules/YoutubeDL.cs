using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

using YoutubeExplode;
using YoutubeExplode.Models.MediaStreams;

//using HeyRed.Mime;
// Nuget package Mime to reenable

namespace DiscordYoutubeDL
{
    public class YoutubeDL : ModuleBase<SocketCommandContext>
    {
        private IConfiguration config;
        private DiscordSocketClient client;
        public YoutubeDL(IConfiguration configuration, DiscordSocketClient _client)
        {
            config = configuration; 
            client = _client;
        }

        [Command("download", RunMode = RunMode.Async)]
        [Summary("Downloads a YT video.")]
        [Alias("dl", "grab", "get", "down")]
        [RequireBotPermission(
            GuildPermission.AttachFiles &
            GuildPermission.SendMessages
        )]
        public async Task ytDownloadVid(string videoURL, string filetype = "mp3")
        {
            DateTime timerStart = DateTime.Now;
            var embed = new EmbedBuilder()
            .WithThumbnailUrl(config["icons:loading_url"])
            .WithTitle(config["strings:start_get_video"])
            .WithColor(Color.Blue);

            var loadingMessage = await Context.Channel.SendMessageAsync(embed: embed.Build());

            Uri videoURI = new Uri(videoURL, UriKind.Absolute);
            var id = YoutubeClient.ParseVideoId(videoURI.ToString());
            // TODO: Add proper URI error handling. 
            //if (videoURI.Host != )
            //    throw new ArgumentException("Address provided is not a YouTube URI.");
            var ytClient = new YoutubeClient();
            var ytMetadata = await ytClient.GetVideoAsync(id);

            if (ytMetadata.Duration > new TimeSpan(1, 0, 0))
            {
                embed.WithColor(Color.Red)
                .WithThumbnailUrl(config["icons:error_url"])
                .WithTitle(config["strings:max_duration_exceeded_title"])
                .WithDescription(config["strings:max_duration_exceeded_desc"]);
                await loadingMessage.ModifyAsync(msg => msg.Embed = embed.Build());
                return;
            }

            var ytStreamMetadataSet = await ytClient.GetVideoMediaStreamInfosAsync(id);
            var ytStreamMetadata = ytStreamMetadataSet.Audio.WithHighestBitrate();

            var ytEmbed = new EmbedBuilder
                {
                    Title = String.Format(config["strings:video_embed_title"], Format.Sanitize(ytMetadata.Title)),
                    Author = new EmbedAuthorBuilder
                    {
                        Name = String.Format(config["strings:video_embed_author"], Format.Sanitize(ytMetadata.Author)),
                        IconUrl = config["strings:video_embed_author_thumb_url"]
                    },
                    ThumbnailUrl = ytMetadata.Thumbnails.HighResUrl,
                    Description = String.Format(config["strings:video_embed_description"], Format.Sanitize(ytMetadata.Description)).Length <= 2048 ? 
                                  String.Format(config["strings:video_embed_description"], Format.Sanitize(ytMetadata.Description)) :
                                  $"{String.Format(config["strings:video_embed_description"], Format.Sanitize(ytMetadata.Description)).Substring(0, 2045)}...",
                    Footer = new EmbedFooterBuilder().WithText(
                        String.Format(config["strings:video_embed_footer"], ytMetadata.Duration.ToString(), ytMetadata.Statistics.ViewCount)
                        )
                }
                .WithFields(
                    new EmbedFieldBuilder()
                    .WithName(config["strings:video_embed_likes_title"])
                    .WithValue(String.Format(config["strings:video_embed_likes_description"], ytMetadata.Statistics.LikeCount))
                    .WithIsInline(true),
                    new EmbedFieldBuilder()
                    .WithName(config["strings:video_embed_dislikes_title"])
                    .WithValue(String.Format(config["strings:video_embed_dislikes_description"], ytMetadata.Statistics.DislikeCount))
                    .WithIsInline(true)
                );
            ytEmbed.Build();

            var ytStreamTask = ytClient.GetMediaStreamAsync(ytStreamMetadata);
            var ytStream = await ytStreamTask;
            if (config.GetValue<bool>("debug", false))
            {
                if (ytStreamTask.IsCompletedSuccessfully)
                    Console.WriteLine($"Successfully got stream data for video id \"{ytMetadata.Id}\"");
                else
                    Console.WriteLine($"Failed to get stream data for video id \"{ytMetadata.Id}\"");
            }

            var encodedStream = new MemoryStream();

            using (Process ffmpeg = new Process
            {
                StartInfo =
                {
                    FileName = config["ffmpeg_location"],
                    Arguments = $"-i - -f {filetype} pipe:1",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            })
            {
                Exception ffmpegException = null;
                
                if (config.GetValue<bool>("debug", false))
                    ffmpeg.ErrorDataReceived += (sender, eventArgs) => Console.WriteLine(eventArgs.Data);
                
                ffmpeg.Start();
                ffmpeg.BeginErrorReadLine();
                var inputTask = Task.Run(() => 
                {
                    try
                    {
                        ytStream.CopyTo(ffmpeg.StandardInput.BaseStream);
                        ffmpeg.StandardInput.Close();
                    }
                    catch (IOException e)
                    {
                        ffmpegException = e;
                    }
                });
                var outputTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(encodedStream);
                
                Task.WaitAll(inputTask, outputTask);
                if (ffmpegException != null)
                {
                    embed.WithColor(Color.Red)
                    .WithThumbnailUrl(config["icons:error_url"])
                    .WithTitle(config["strings:ffmpeg_exception_title"])
                    .WithDescription(config["strings:ffmpeg_exception_description"])
                    .WithFields(
                        new EmbedFieldBuilder()
                        .WithName("*Stack Traceback:*")
                        .WithValue(Format.Sanitize(ffmpegException.StackTrace))
                        .WithIsInline(false) 
                    );
                    await loadingMessage.ModifyAsync(msg => msg.Embed = embed.Build());
                    return;
                }

                ffmpeg.WaitForExit();

                //var fileExt = MimeGuesser.GuessExtension(encodedStream);
            }

            Discord.Rest.RestUserMessage finishedMessage = null;

            if (encodedStream.Length < 0x800000)
            {
                if (config.GetValue<bool>("debug", false))
                    Console.WriteLine("Uploading transcoded file to Discord.");
                encodedStream.Position = 0;
                finishedMessage = await Context.Channel.SendFileAsync(
                    encodedStream, $"{ytMetadata.Title}.{filetype}",
                    embed: ytEmbed.Build()
                );
            }
            else
            {
                if (config.GetValue<bool>("debug", false))
                    Console.WriteLine("Uploading transcoded file to alternate host.");
                embed.WithTitle(config["strings:file_too_large_title"])
                .WithDescription(config["strings:file_too_large_description"]);
                await loadingMessage.ModifyAsync(msg => msg.Embed = embed.Build());

                var newName = String.Join(
                    "_",
                    ytMetadata.Title.Split(Path.GetInvalidFileNameChars(),
                    StringSplitOptions.RemoveEmptyEntries) 
                    ).TrimEnd('.');

                using (HttpClient client = new HttpClient { BaseAddress = new Uri(config["http_put_url"]) })
                {
                    using (var response = client.PutAsync($"{newName}.{filetype}", new StreamContent(encodedStream)))
                    {
                        /*
                        DateTime lastUpdate = DateTime.Now;
                        while (response.Status == TaskStatus.Running)
                        {
                            if (DateTime.Now.Subtract(lastUpdate).TotalSeconds > 3)
                            {
                                await loadingMessage.ModifyAsync(
                                    msg => msg.Content = $"Uploading to an alternate host...\nPlease allow time for this to finish.\n{}% uploaded..."
                                    );
                                lastUpdate = DateTime.Now;
                            }
                        }
                        */
                        
                        if (response.Result.IsSuccessStatusCode)
                        {
                            ytEmbed.AddField(
                                new EmbedFieldBuilder()
                                .WithName(config["strings:external_download_title"])
                                .WithValue(String.Format(config["strings:external_download_description"], await response.Result.Content.ReadAsStringAsync()))
                            );
                            finishedMessage = await Context.Channel.SendMessageAsync(
                                embed: ytEmbed.Build()
                                );
                        }
                    }
                }
            }

            embed.WithColor(Color.Green)
            .WithThumbnailUrl(config["icons:success_url"])
            .WithTitle(config["strings:finished_message_description"])
            .WithDescription(
                $"[{config["strings:finished_message_link"]}](https://discordapp.com/channels/{Context.Guild.Id}/{Context.Channel.Id}/{finishedMessage.Id})"
                );
            
            if (config.GetValue<bool>("debug", false))
                Console.WriteLine($"Successfully handled video id \"{ytMetadata.Id}\" in {DateTime.Now.Subtract(timerStart).Seconds} seconds.");

            await loadingMessage.ModifyAsync(msg => msg.Embed = embed.Build());
            Task.WaitAll(ytStream.DisposeAsync().AsTask(), encodedStream.DisposeAsync().AsTask());
        }
    }
}