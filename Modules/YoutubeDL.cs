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

            var loading_message = await Context.Channel.SendMessageAsync(embed: embed.Build());

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
                await loading_message.ModifyAsync(msg => msg.Embed = embed.Build());
                return;
            }

            var ytStreamMetadataSet = await ytClient.GetVideoMediaStreamInfosAsync(id);
            var ytStreamMetadata = ytStreamMetadataSet.Audio.WithHighestBitrate();
            var ytStream = await ytClient.GetMediaStreamAsync(ytStreamMetadata);

            var ytEmbed = new EmbedBuilder()
            .WithThumbnailUrl(ytMetadata.Thumbnails.HighResUrl)
            .WithAuthor(Format.Sanitize(ytMetadata.Author))
            .WithTitle(Format.Sanitize(ytMetadata.Title))
            .WithDescription(
                Format.Sanitize(ytMetadata.Description).Length <= 2048 ? 
                Format.Sanitize(ytMetadata.Description) : $"{Format.Sanitize(ytMetadata.Description).Substring(0, 2045)}...")
            .WithFields(
                new EmbedFieldBuilder()
                .WithName("👍")
                .WithValue(ytMetadata.Statistics.LikeCount)
                .WithIsInline(true),
                new EmbedFieldBuilder()
                .WithName("👎")
                .WithValue(ytMetadata.Statistics.DislikeCount)
                .WithIsInline(true)
            )
            .WithFooter($"Duration: {ytMetadata.Duration.ToString()} | Views: {ytMetadata.Statistics.ViewCount}");

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
                    await loading_message.ModifyAsync(msg => msg.Embed = embed.Build());
                    return;
                }

                ffmpeg.WaitForExit();

                var fileExt = MimeGuesser.GuessExtension(encodedStream);
            }

            if (encodedStream.Length < 0x800000)
                await Context.Channel.SendFileAsync(
                    encodedStream, $"{ytMetadata.Title}.{filetype}",
                    embed: ytEmbed.Build()
                );
            else
            {
                embed.WithTitle("Discord file size limit exceeded")
                .WithDescription("Uploading to an alternate host.\nPlease wait a bit longer.");
                await loading_message.ModifyAsync(msg => msg.Embed = embed.Build());

                using (HttpClient client = new HttpClient { BaseAddress = new Uri("https://transfer.sh/") })
                {
                    var response = client.PutAsync(ytMetadata.Title.Replace(" ", ""), new StreamContent(encodedStream)).Result;
                    if (response.IsSuccessStatusCode)
                        await ReplyAsync(await response.Content.ReadAsStringAsync());
                }
            }

            embed.WithColor(Color.Green)
            .WithThumbnailUrl("https://cdn.discordapp.com/attachments/579654646629531650/588520160739196929/check.png")
            .WithTitle("Done. Check latest message.");
            await loading_message.ModifyAsync(msg => msg.Embed = embed.Build());
        }
    }
}