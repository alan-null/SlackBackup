using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;
using SlackAPI;
using File = System.IO.File;

namespace SlackBackup
{
    public class SlackBackup
    {
        protected ConcurrentStack<string> RunningTasks = new ConcurrentStack<string>();
        protected SlackTaskClient Client { get; set; }
        protected RestClient RestClient { get; }
        protected ConcurrentBag<SerializableBackup> Bag { get; set; } = new ConcurrentBag<SerializableBackup>();
        protected string Token { get; }
        protected string FolderPath { get; set; }
        protected string FilePath => $"{FolderPath}/{Token}.json";

        public SlackBackup(string token, string folderPath)
        {
            Client = new SlackTaskClient(token);
            RestClient = new RestClient("https://files.slack.com/");
            Token = token;
            FolderPath = folderPath;
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
        }

        public async Task GetfilesAsync()
        {
            StartJob("GetFiles");
            await DoGetfilesAsync();
            WaitForJob();
        }

        public async Task UpdateFilesAsync()
        {
            var latestFileDate = DateTime.MinValue;
            Directory.GetFiles(FolderPath, "*.json").Where(s => !s.Contains(Token)).ToList().ForEach(s =>
            {
                var file = JsonConvert.DeserializeObject<SlackAPI.File>(File.ReadAllText(s));
                if (file.created > latestFileDate)
                {
                    latestFileDate = file.created;
                }
            });
            latestFileDate = latestFileDate.AddSeconds(1);
            await FetchFilesAsync(pageNo: 0, fromDate: latestFileDate);
            WaitForJob();
        }

        private async Task DoGetfilesAsync()
        {
            var response = await Client.GetFilesAsync(count: 100);
            response.files.ToList().ForEach(SaveFile);
            int curr = response.paging.page;
            while (response.paging.pages >= curr)
            {
                await FetchFilesAsync(curr++);
            }
            PushFinishNotification();
        }

        protected virtual void SaveFile(SlackAPI.File responseFile)
        {
            Console.WriteLine($"Storing file: {responseFile.id}");
            var filePath = $@"{FolderPath}\{responseFile.id}.{responseFile.filetype}";
            var fileMetaPath = $@"{FolderPath}\{responseFile.id}.json";

            File.WriteAllBytes(filePath, DownloadFile(responseFile));
            File.WriteAllText(fileMetaPath, JsonConvert.SerializeObject(responseFile, Formatting.Indented));
        }

        protected async Task FetchFilesAsync(int pageNo, DateTime? fromDate = null)
        {
            StartJob("FetchFiles");
            var response = await Client.GetFilesAsync(count: 100, page: pageNo, from: fromDate);
            response.files.ToList().ForEach(SaveFile);
            PushFinishNotification();
            Console.WriteLine("FetchFiles-END");
        }

        protected virtual byte[] DownloadFile(SlackAPI.File responseFile)
        {
            var request = new RestRequest(responseFile.url_private);
            request.AddHeader("Authorization", $"Bearer {Token}");
            var executeAsGet = RestClient.ExecuteGet(request);
            return executeAsGet.RawBytes;
        }

        public async Task InitializeBackupAsync()
        {
            await DoInitializeBackupAsync();
            WaitForJob();
            var serializeObject = JsonConvert.SerializeObject(Bag, Formatting.Indented);
            File.WriteAllText(FilePath, serializeObject);
        }

        public async Task UpdateBackupAsync()
        {
            await DoUpdateBackupAsync();
            WaitForJob();
            foreach (var serializableBackup in Bag)
            {
                serializableBackup.Messages.Sort((message, message1) => message1.ts.CompareTo(message.ts));
            }
            var serializeObject = JsonConvert.SerializeObject(Bag, Formatting.Indented);
            File.WriteAllText(FilePath, serializeObject);
        }

        protected async Task DoInitializeBackupAsync()
        {
            StartJob("DoInitializeBackup");
            var r = await Client.GetConversationsListAsync();
            foreach (var channel in r.channels)
            {
                if (!Bag.Any(backup => backup.Channel.id.Equals(channel.id)))
                {
                    Bag.Add(new SerializableBackup(channel));
                }
                await FetchMessagesAsync(channel);
            }
            PushFinishNotification();

        }

        protected virtual SerializableBackup GetSerializableBackup(SlackAPI.Channel channel)
        {
            return Bag.First(b => b.Channel.id.Equals(channel.id));
        }

        protected async Task DoUpdateBackupAsync()
        {
            StartJob("DoUpdateBackup");
            var lines = File.ReadAllText(FilePath);
            var deSerializeObject = JsonConvert.DeserializeObject<List<SerializableBackup>>(lines);

            Bag = new ConcurrentBag<SerializableBackup>();
            deSerializeObject.Reverse();
            deSerializeObject.ForEach(backup => Bag.Add(backup));

            var r = await Client.GetConversationsListAsync();
            foreach (var channel in r.channels)
            {
                if (!Bag.Any(backup => backup.Channel.id.Equals(channel.id)))
                {
                    Bag.Add(new SerializableBackup(channel));
                    await FetchMessagesAsync(channel);
                }
                else
                {
                    var backup = GetSerializableBackup(channel);
                    var latestMessage = backup.GetLatestMessage();
                    await FetchMessagesAsync(channel, null, latestMessage?.ts);
                }
                PushFinishNotification();
            }
        }

        protected virtual void PushFinishNotification()
        {
            RunningTasks.TryPop(out _);
        }

        protected async Task FetchMessagesAsync(SlackAPI.Channel channel, DateTime? historyLatest = null, DateTime? oldest = null)
        {
            StartJob($"Channel '{channel.name}' sync");
            Console.WriteLine($"GetChannelHistory: {channel.name}");
            var history = await Client.GetChannelHistoryAsync(channel, latest: historyLatest, oldest: oldest, count: 1000);

            var backup = GetSerializableBackup(channel);
            Console.WriteLine(history.error);
            if (history.messages.Length > 0)
            {
                Console.WriteLine($"Adding new messsages [{history.messages[history.messages.Length - 1].ts.Date} - {history.messages[0].ts.Date}]");
                backup.Messages.AddRange(history.messages);
            }
            if (history.has_more)
            {
                if (history.messages.Last().ts > oldest)
                {
                    await FetchMessagesAsync(channel, null, history.messages.First().ts);
                }
                else
                {
                    await FetchMessagesAsync(channel, history.messages.Last().ts, oldest);
                }
            }
            PushFinishNotification();

        }

        protected void StartJob(string name)
        {
            Console.WriteLine($">> Job:{name}");
            RunningTasks.Push(name);
        }

        protected virtual void WaitForJob()
        {
            while (RunningTasks.Count > 0)
            {
                Console.WriteLine($"Running tasks: {RunningTasks.Count}");
                Thread.Sleep(100);
            }
        }
    }
}
