namespace ASPNET.StandUp.App
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Xml;
    using System.Xml.Serialization;
    using Google.Apis.Services;
    using Google.Apis.YouTube.v3;
    using Google.Apis.YouTube.v3.Data;
    using YoutubeExtractor;

    internal class Program
    {
        private static void Main()
        {
            new Program().Start();

            Console.ReadLine();
        }

        internal async void Start()
        {
            const string YoutubePubliApiKey = "*** YOUR KEY ***"; // You can get it from: http://console.developers.google.com/
            const string UserName = "shanselman";
            const string AspNetPlayListId = "PL0M0zPgJ3HSftTAAHttA3JQU4vOjXFquF";
            const string OutputDirectoryPath = @"C:\ASP.NET_StandUp_Videos";

            var videos = (await GetVideoInfosFromPlaylistAsync(YoutubePubliApiKey, UserName, AspNetPlayListId)).ToArray();

            // Store this somewhere so when you are reading the videos back, you can map the fileName 
            // (video position) to the original title. Cannot use Title for the file name as it contains invalid path chars.
            var xml = Serialize(videos);
            File.WriteAllText(Path.Combine(OutputDirectoryPath, "Meta.xml"), xml);

            videos
                .AsParallel()
                .WithDegreeOfParallelism(4) // download 4 at the time
                .ForAll(vid =>
                {
                    // use the position of the video in the playlist instead of the video title as the title has invalid path chars
                    var saveAs = new FileInfo(Path.Combine(OutputDirectoryPath, vid.Position.ToString() + ".mp4"));
                    DownloadVideo(vid.Id, saveAs);
                });
        }

        private static async Task<IEnumerable<YoutubeVideoInfo>> GetVideoInfosFromPlaylistAsync(string apiKey, string userName, string playListId)
        {
            var result = new List<YoutubeVideoInfo>();

            var yt = new YouTubeService(new BaseClientService.Initializer { ApiKey = apiKey });

            var channelListRequest = yt.Channels.List("contentDetails");
            channelListRequest.ForUsername = userName;
            var channelListResponse = channelListRequest.Execute();
            
            // ReSharper disable once UnusedVariable
            foreach (var channel in channelListResponse.Items)
            {
                var nextPageToken = string.Empty;
                while (nextPageToken != null)
                {
                    var playlistItemsListRequest = yt.PlaylistItems.List("snippet");
                    playlistItemsListRequest.PlaylistId = playListId;
                    playlistItemsListRequest.MaxResults = 50;
                    playlistItemsListRequest.PageToken = nextPageToken;

                    var playlistItemsListResponse = await playlistItemsListRequest.ExecuteAsync();
                    foreach (var item in playlistItemsListResponse.Items)
                    {
                        var vidInfo = new YoutubeVideoInfo
                        {
                            Id = item.Snippet.ResourceId.VideoId,
                            Title = item.Snippet.Title,
                            Position = item.Snippet.Position,
                            PublishTime = item.Snippet.PublishedAt,
                            Thumbnail = item.Snippet.Thumbnails.Standard
                        };

                        result.Add(vidInfo);
                    }

                    nextPageToken = playlistItemsListResponse.NextPageToken;
                }
            }

            return result;
        }

        private static void DownloadVideo(string videoId, FileSystemInfo saveAs)
        {
            var videoUri = new Uri($"https://youtu.be/{videoId}");

            var allMp4Videos = DownloadUrlResolver
                .GetDownloadUrls(videoUri.AbsoluteUri)
                .Where(v => v.VideoType == VideoType.Mp4)
                .OrderByDescending(v => v.Resolution);

            var candidateVideo = allMp4Videos.First(); // take the highest resolution available
            if (candidateVideo.RequiresDecryption)
            {
                DownloadUrlResolver.DecryptDownloadUrl(candidateVideo);
            }

            var videoDownloader = new VideoDownloader(candidateVideo, saveAs.FullName);
            videoDownloader.Execute();
        }

        internal static string Serialize<T>(T value)
        {
            if (value == null) { return string.Empty; }

            try
            {
                var xmlserializer = new XmlSerializer(typeof(T));
                var stringWriter = new StringWriter();
                using (var writer = XmlWriter.Create(stringWriter))
                {
                    xmlserializer.Serialize(writer, value);
                    return stringWriter.ToString();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Ooopsie", ex);
            }
        }
    }

    public sealed class YoutubeVideoInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public long? Position { get; set; }
        public DateTime? PublishTime { get; set; }
        public Thumbnail Thumbnail { get; set; }
    }
}
