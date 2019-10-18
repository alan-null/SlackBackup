using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using RestSharp;
using SlackAPI;
using File = System.IO.File;

namespace SlackBackup
{
    public class SlackBackup
    {
        protected ConcurrentStack<string> RunningTasks = new ConcurrentStack<string>();
        protected SlackClient Client { get; set; }
        protected RestClient RestClient { get; }
        protected ConcurrentBag<SerializableBackup> Bag { get; set; } = new ConcurrentBag<SerializableBackup>();
        protected string Token { get; }
        protected string FolderPath { get; set; }
        protected string FilePath => $"{FolderPath}/{Token}.json";

        public SlackBackup(string token, string folderPath)
        {
            Client = new SlackClient(token);
            RestClient = new RestClient("https://files.slack.com/");
            Token = token;
            FolderPath = folderPath;
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
        }

        public void Getfiles()
        {
            StartJob("GetFiles");
            DoGetfiles();
            WaitForJob();
        }

        public void UpdateFiles()
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
            FetchFiles(pageNo: 1, fromDate: latestFileDate);
            WaitForJob();
        }

        private void DoGetfiles()
        {
            Client.GetFiles(response =>
            {
                response.files.ToList().ForEach(SaveFile);
                int curr = response.paging.page;
                while (response.paging.pages >= curr)
                {
                    FetchFiles(curr++);
                }
                PushFinishNotification();
            }, count: 100);
        }

        protected virtual void SaveFile(SlackAPI.File responseFile)
        {
            Console.WriteLine($"Storing file: {responseFile.id}");
            var filePath = $@"{FolderPath}\{responseFile.id}.{responseFile.filetype}";
            var fileMetaPath = $@"{FolderPath}\{responseFile.id}.json";

            File.WriteAllBytes(filePath, DownloadFile(responseFile));
            File.WriteAllText(fileMetaPath, JsonConvert.SerializeObject(responseFile, Formatting.Indented));
        }

        protected void FetchFiles(int pageNo, DateTime? fromDate = null)
        {
            StartJob("FetchFiles");
            Client.GetFiles(response =>
            {
                response.files.ToList().ForEach(SaveFile);
                PushFinishNotification();
                Console.WriteLine("FetchFiles-END");
            }, count: 100, page: pageNo, from: fromDate);
        }

        protected virtual byte[] DownloadFile(SlackAPI.File responseFile)
        {
            var request = new RestRequest(responseFile.url_private);
            request.AddHeader("Authorization", $"Bearer {Token}");
            var executeAsGet = RestClient.ExecuteAsGet(request, "GET");
            return executeAsGet.RawBytes;
        }

        public void TestAuth(Action<AuthTestResponse> callback)
        {
            Client.APIRequestWithToken(callback, new Tuple<string, string>("exclude_archived", "0"));
        }

        public void InitializeBackup()
        {
            DoInitializeBackup();
            WaitForJob();
            var serializeObject = JsonConvert.SerializeObject(Bag, Formatting.Indented);
            File.WriteAllText(FilePath, serializeObject);
        }

        public void UpdateBackup()
        {
            DoUpdateBackup();
            WaitForJob();
            foreach (var serializableBackup in Bag)
            {
                serializableBackup.Messages.Sort((message, message1) => message1.ts.CompareTo(message.ts));
            }
            var serializeObject = JsonConvert.SerializeObject(Bag, Formatting.Indented);
            File.WriteAllText(FilePath, serializeObject);
        }

        protected void DoInitializeBackup()
        {
            StartJob("DoInitializeBackup");
            Client.GetChannelList(r =>
            {
                foreach (var channel in r.channels)
                {

                    if (!Bag.Any(backup => backup.Channel.id.Equals(channel.id)))
                    {
                        Bag.Add(new SerializableBackup(channel));
                    }
                    FetchMessages(channel);
                }
                PushFinishNotification();
            }, false);

        }

        protected virtual SerializableBackup GetSerializableBackup(SlackAPI.Channel channel)
        {
            return Bag.First(b => b.Channel.id.Equals(channel.id));
        }

        protected void DoUpdateBackup()
        {
            StartJob("DoUpdateBackup");
            var lines = File.ReadAllText(FilePath);
            var deSerializeObject = JsonConvert.DeserializeObject<List<SerializableBackup>>(lines);

            Bag = new ConcurrentBag<SerializableBackup>();
            deSerializeObject.Reverse();
            deSerializeObject.ForEach(backup => Bag.Add(backup));

            Client.GetChannelList(r =>
            {
                foreach (var channel in r.channels)
                {
                    if (!Bag.Any(backup => backup.Channel.id.Equals(channel.id)))
                    {
                        Bag.Add(new SerializableBackup(channel));
                        FetchMessages(channel);
                    }
                    else
                    {
                        var backup = GetSerializableBackup(channel);
                        var latestMessage = backup.GetLatestMessage();
                        FetchMessages(channel, null, latestMessage?.ts);
                    }
                }
                PushFinishNotification();
            }, false);

        }

        protected virtual void PushFinishNotification()
        {
            RunningTasks.TryPop(out _);
        }

        protected void FetchMessages(SlackAPI.Channel channel, DateTime? historyLatest = null, DateTime? oldest = null)
        {
            StartJob($"Channel '{channel.name}' sync");
            Console.WriteLine($"GetChannelHistory: {channel.name}");
            Client.GetChannelHistory(history =>
            {
                var backup = GetSerializableBackup(channel);
                if (history.messages.Length > 0)
                {
                    Console.WriteLine("Adding new messsages");
                    foreach (var m in history.messages)
                    {
                        Console.WriteLine($"{m.text} [{m.ts}]");
                    }
                    backup.Messages.AddRange(history.messages);
                }
                if (history.has_more)
                {
                    FetchMessages(channel, history.messages.Last().ts);
                }
                PushFinishNotification();
            }, channel, count: 1000, latest: historyLatest, oldest: oldest);
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
