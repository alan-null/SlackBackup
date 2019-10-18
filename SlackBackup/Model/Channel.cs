using System.Collections.Generic;
using Newtonsoft.Json;

namespace SlackBackup.Model
{
    public class Channel
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }
        [JsonProperty("latest")]
        public string Latest { get; set; }
        [JsonProperty("messages")]
        public List<Message> Messages { get; set; }
        [JsonProperty("has_more")]
        public bool HasMore { get; set; }
        [JsonProperty("is_limited")]
        public bool IsLimited { get; set; }

        public Channel()
        {
            Messages = new List<Message>();
        }
    }


}
