using System;
using Newtonsoft.Json;

namespace SlackBackup.Model
{
    public class Message
    {
        [JsonProperty("text")]
        public string Text { get; set; }
        [JsonProperty("username")]
        public string Username { get; set; }
        [JsonProperty("bot_id")]
        public string BotId { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("subtype")]
        public string Subtype { get; set; }
        [JsonProperty("ts")]
        public double Ts { get; set; }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }
    }


}
