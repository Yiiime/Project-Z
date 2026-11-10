using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Seabreeze;

namespace ZLive {
    // Unity owns the checkpoint and sequence numbers; never send the private checkpoint to the room.
    internal sealed class LiveMemoryBridge {
        readonly GameSession game;
        readonly SaveData owner;
        readonly string run;
        string checkpoint;
        JObject projection;
        public int Ack { get; private set; }
        public bool Current => game != null && game.State == owner && game.Memory?.Client.RunId == run;
        public string Key => owner.memory.worldId + "." + run;

        LiveMemoryBridge(GameSession game) {
            this.game = game;
            owner = game.State;
            run = game.Memory.Client.RunId;
        }

        public static LiveMemoryBridge Open() {
            var game = GameSession.Instance;
            return game != null && game.State?.memory != null && game.Memory?.Client != null
                ? new LiveMemoryBridge(game) : null;
        }

        public async Task WaitForDaytimeSync() {
            // Give a just-finished daytime conversation time to reach its checkpoint.
            // Offline play still opens; the local outbox only supplies the fact of meeting.
            for (int i=0; i<20 && Current && !game.Memory.Client.Synchronized; i++) await Task.Delay(100);
        }
        string RecapFlag => "z_live_day_recap_" + owner.day;
        string TopicPrefix => RecapFlag + "_topic_";
        public JObject Binding() {
            if (!Current) return null;
            if (checkpoint != owner.memory.checkpoint || projection == null) {
                checkpoint = owner.memory.checkpoint;
                projection = string.IsNullOrEmpty(checkpoint) ? null : JObject.Parse(checkpoint)["broadcast"] as JObject;
            }
            var today=(int?)projection?["day"]==owner.day ? projection?["today"] as JArray : null;
            var localTalk=owner.memory.outbox.Any(e=>e.day==owner.day && e.kind=="utterance" && e.channel=="private_player_chat" &&
                ((e.speakerId=="player" && e.recipientId=="z") || (e.speakerId=="z" && e.recipientId=="player")));
            return new JObject {
                ["today"] = today?.Count>0 ? today.DeepClone() : localTalk ? new JArray("和岛上的朋友说了会儿话") : new JArray(),
                ["highlights"] = (int?)projection?["day"]==owner.day ? projection?["highlights"]?.DeepClone() ?? new JArray() : new JArray(),
                ["songs"] = (int?)projection?["day"]==owner.day ? projection?["songs"]?.DeepClone() ?? new JArray() : new JArray(),
                ["toldItems"] = new JArray(owner.flags.Where(f => f.StartsWith(TopicPrefix, StringComparison.Ordinal)).Select(f => f.Substring(TopicPrefix.Length))),
                ["recapSpoken"] = owner.flags.Contains(RecapFlag),
                ["worldId"] = owner.memory.worldId, ["runId"] = run, ["day"] = owner.day,
                ["stage"] = (string)projection?["stage"] ?? "island_daily",
                ["entries"] = projection?["entries"]?.DeepClone() ?? new JArray(),
                ["stories"] = projection?["stories"]?.DeepClone() ?? new JArray()
            };
        }

        public void Receive(string sessionId, JArray rows) {
            if (!Current || rows == null || rows.Count == 0) return;
            var state = owner.memory;
            string prefix = "live_mem_" + sessionId + "_";
            // Store the consumed cursor with the same atomic save as the outbox, including offline writes.
            string marker = owner.flags.Find(s => s.StartsWith(prefix, StringComparison.Ordinal));
            int cursor = marker != null && int.TryParse(marker.Substring(prefix.Length), out int saved) ? saved : 0;
            int before = cursor;
            foreach (var row in rows) {
                int seq = (int)row["seq"];
                if (seq <= cursor) continue;
                if (seq != cursor + 1) break;
                if ((int?)row["recapDay"] == owner.day && !owner.flags.Contains(RecapFlag)) owner.flags.Add(RecapFlag);
                if ((int?)row["recapDay"] == owner.day && row["item"]?.Type == JTokenType.String) {
                    string flag = TopicPrefix + (string)row["item"];
                    if (!owner.flags.Contains(flag)) owner.flags.Add(flag);
                }
                state.outbox.Add(new MemoryEvent {
                    id = prefix + seq, seq = ++state.nextSeq, day = owner.day, period = owner.period,
                    accepted = (bool?)row["shared"] == true, kind = "live_memory", channel = "livestream", speakerId = (string)row["speaker"],
                    conversationId = sessionId, text = (string)row["text"], stepId = (string)row["change"]?["id"],
                    receiptId = (string)row["change"]?["step"]
                });
                cursor = seq;
            }
            if (cursor != before) {
                if (marker != null) owner.flags.Remove(marker);
                owner.flags.Add(prefix + cursor);
            }
            if (cursor > Ack && game.Save()) Ack = cursor;
        }
    }
}

