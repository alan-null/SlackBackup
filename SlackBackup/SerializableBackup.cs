using System.Collections.Generic;
using System.Linq;

namespace SlackBackup
{
    public class SerializableBackup
    {
        public SlackAPI.Channel Channel { get; set; }
        public List<SlackAPI.Message> Messages { get; set; }

        public SerializableBackup()
        {
            Messages = new List<SlackAPI.Message>();
        }

        public SerializableBackup(SlackAPI.Channel channel) : this()
        {
            Channel = channel;
        }

        public SerializableBackup(SlackAPI.Channel channel, List<SlackAPI.Message> messages)
        {
            Channel = channel;
            Messages = messages;
        }

        public SlackAPI.Message GetLatestMessage()
        {
            return Messages.FirstOrDefault(message => message.ts.Equals(Messages.Max(n => n.ts)));
        }
    }
}
