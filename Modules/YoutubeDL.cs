using Discord;
using Discord.Commands;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

using YoutubeExplode;
using YoutubeExplode.Models.MediaStreams;

using HeyRed.Mime;

namespace DiscordYoutubeDL
{
    public class YoutubeDL : ModuleBase<SocketCommandContext>
    {
        private IConfiguration config;
        public YoutubeDL(IConfiguration configuration) => config = configuration;

        [Command("download", RunMode = RunMode.Async)]
        [Summary("Downloads a YT video.")]
        [Alias("dl")]
        [RequireBotPermission(
            GuildPermission.AttachFiles &
            GuildPermission.SendMessages
        )]
        public async Task ytDownloadVid(string videoURL, string filetype = "mp3")
        {
            var embed = new EmbedBuilder()
            .WithThumbnailUrl("https://cdn.discordapp.com/attachments/110373943822540800/235649976192073728/4AyCE.png")
            .WithTitle("**Grabbing video, please wait...**")
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
                .WithThumbnailUrl("https://cdn.discordapp.com/attachments/588535514575929344/588566397819551744/274c.png")
                .WithTitle("**Maximum video duration exceeded**")
                .WithDescription("For performance reasons, the bot will not process videos longer than an hour.");
                await loadingMessage.ModifyAsync(msg => msg.Embed = embed.Build());
                return;
            }

            var ytStreamMetadataSet = await ytClient.GetVideoMediaStreamInfosAsync(id);
            var ytStreamMetadata = ytStreamMetadataSet.Audio.WithHighestBitrate();

            var ytEmbed = new EmbedBuilder
                {
                    Title = Format.Sanitize(ytMetadata.Title),
                    Author = new EmbedAuthorBuilder().WithName(Format.Sanitize(ytMetadata.Author)),
                    ThumbnailUrl = ytMetadata.Thumbnails.HighResUrl,
                    Footer = new EmbedFooterBuilder().WithText(
                        $"Duration: {ytMetadata.Duration.ToString()} | Views: {ytMetadata.Statistics.ViewCount}"
                        )
                }
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        Name = "Description",
                        Value = Format.Sanitize(ytMetadata.Description).Length <= 2048 ? 
                        Format.Sanitize(ytMetadata.Description) : $"{Format.Sanitize(ytMetadata.Description).Substring(0, 2045)}...",
                        IsInline = false
                    },
                    new EmbedFieldBuilder()
                    .WithName("👍")
                    .WithValue(ytMetadata.Statistics.LikeCount)
                    .WithIsInline(true),
                    new EmbedFieldBuilder()
                    .WithName("👎")
                    .WithValue(ytMetadata.Statistics.DislikeCount)
                    .WithIsInline(true)
                );

            try {
                ytEmbed.Build();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            var ytStream = await ytClient.GetMediaStreamAsync(ytStreamMetadata);

            var encodedStream = new System.IO.MemoryStream();

            using (Process ffmpeg = new Process
            {
                StartInfo =
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i - -f {filetype} pipe:1",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    #if DEBUG
                    WorkingDirectory = @".\..\..\..\"
                    #else
                    WorkingDirectory = @".\"
                    #endif
                },
                EnableRaisingEvents = true
            })
            {
                ffmpeg.ErrorDataReceived += (sender, eventArgs) => Console.WriteLine(eventArgs.Data);
                ffmpeg.Start();
                ffmpeg.BeginErrorReadLine();
                Exception ffmpegException = null;
                var inputTask = Task.Run(() => 
                {
                    try
                    {
                        ytStream.CopyTo(ffmpeg.StandardInput.BaseStream);
                        ffmpeg.StandardInput.Close();
                    }
                    catch (System.IO.IOException e)
                    {
                        ffmpegException = e;
                    }
                });
                var outputTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(encodedStream);
                
                Task.WaitAll(inputTask, outputTask);
                if (ffmpegException != null)
                {
                    embed.WithColor(Color.Red)
                    .WithThumbnailUrl("https://cdn.discordapp.com/attachments/588535514575929344/588566397819551744/274c.png")
                    .WithTitle("**Error Transcoding Audio**")
                    .WithDescription("This is likely caused by an invalid audio output format.\nFor more details see the traceback below.")
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

                var fileExt = MimeGuesser.GuessExtension(encodedStream);
            }

            Discord.Rest.RestUserMessage finishedMessage = null;

            if (encodedStream.Length < 0x800000)
                finishedMessage = await Context.Channel.SendFileAsync(
                    encodedStream, $"{ytMetadata.Title}.{filetype}",
                    embed: ytEmbed.Build()
                );
            else
            {
                embed.WithTitle("Discord file size limit exceeded")
                .WithDescription("Uploading to an alternate host...\nPlease allow time for this to finish.");
                await loadingMessage.ModifyAsync(msg => msg.Embed = embed.Build());

                var newName = String.Join(
                    "_",
                    ytMetadata.Title.Split(System.IO.Path.GetInvalidFileNameChars(),
                    StringSplitOptions.RemoveEmptyEntries) 
                    ).TrimEnd('.');

                using (HttpClient client = new HttpClient { BaseAddress = new Uri("https://transfer.sh/") })
                {
                    var response = client.PutAsync($"{newName}.{filetype}", new StreamContent(encodedStream)).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        ytEmbed.AddField(
                            new EmbedFieldBuilder()
                            .WithName("Download")
                            .WithValue(await response.Content.ReadAsStringAsync())
                        );
                        finishedMessage = await Context.Channel.SendMessageAsync(
                            embed: ytEmbed.Build()
                            );
                    }
                }
            }

            embed.WithColor(Color.Green)
            .WithThumbnailUrl("https://cdn.discordapp.com/attachments/579654646629531650/588520160739196929/check.png")
            .WithTitle("Done")
            .WithDescription($"[Jump to message](https://discordapp.com/channels/{Context.Guild.Id}/{Context.Channel.Id}/{finishedMessage.Id})");
            
            await loadingMessage.ModifyAsync(msg => msg.Embed = embed.Build());
        }
    }
}