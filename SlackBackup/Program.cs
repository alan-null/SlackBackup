using System;
using System.IO;
using Newtonsoft.Json;
using File = System.IO.File;

namespace SlackBackup
{

    class Program
    {
        static void Main(string[] args)
        {
            var filePath = args[0];
            if (File.Exists(filePath))
            {
                var configuration = JsonConvert.DeserializeObject<BackupConfiguration>(File.ReadAllText(filePath));
                Console.WriteLine(configuration.Folder);
                Console.WriteLine(configuration.Token);

                string token = configuration.Token;
                var backup = new SlackBackup(token, configuration.Folder);

                if (Directory.GetFiles(configuration.Folder).Length == 0)
                {
                    backup.InitializeBackup();
                    backup.Getfiles();
                }
                else
                {
                    backup.UpdateBackup();
                    backup.UpdateFiles();
                }
                Console.WriteLine("Job done");
            }
            else
            {
                Console.WriteLine("Could not locate configuration file");
            }
        }
    }
}
