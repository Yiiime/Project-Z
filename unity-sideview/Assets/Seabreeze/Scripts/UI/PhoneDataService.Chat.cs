using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Seabreeze
{
    /// <summary>Implement this adapter with the team's AI service; there is no fallback reply.</summary>
    public interface IPhoneChatTransport
    {
        Task<PhoneChatReply> ReplyAsync(PhoneChatRequest request, CancellationToken cancellation);
    }

    public sealed class PhoneChatContact
    {
        public string ActorId { get; }
        public string Name { get; }
        internal PhoneChatContact(SevenCharacter actor) { ActorId = actor.id; Name = actor.displayName; }
    }

    public sealed class PhoneChatRequest
    {
        public string RequestId { get; }
        public string WorldId { get; }
        public string ThreadId { get; }
        public string ActorId { get; }
        public string Text { get; }
        public string Place { get; }
        public int Day { get; }
        public int Period { get; }
        public string SenderId => "player";
        public string Channel => "phone_private_chat";
        internal PhoneChatRequest(string requestId, string worldId, string threadId, string actorId, string text, SaveData state)
        {
            RequestId = requestId; WorldId = worldId; ThreadId = threadId; ActorId = actorId; Text = text;
            Day = state.day; Period = state.period; Place = state.place;
        }
    }

    public sealed class PhoneChatReply
    {
        public string RequestId { get; }
        public string WorldId { get; }
        public string ActorId { get; }
        public string Text { get; }
        public PhoneChatReply(string requestId, string worldId, string actorId, string text)
        { RequestId = requestId; WorldId = worldId; ActorId = actorId; Text = text; }
    }

    public enum PhoneChatStatus { Delivered, NotReady, NotMet, InvalidMessage, Unavailable, Busy, Cancelled, ContextChanged, TimedOut, Failed }
    public sealed class PhoneChatResult
    {
        public PhoneChatStatus Status { get; }
        public PhoneChatReply Reply { get; }
        public bool Persisted { get; }
        internal PhoneChatResult(PhoneChatStatus status, PhoneChatReply reply = null, bool persisted = false)
        { Status = status; Reply = reply; Persisted = persisted; }
    }

    public sealed partial class PhoneDataService
    {
        IPhoneChatTransport chatTransport;
        readonly Dictionary<string, CancellationTokenSource> pendingChats = new Dictionary<string, CancellationTokenSource>();
        int chatGeneration;
        public bool ChatConfigured => chatTransport != null;

        // Call from Unity's main thread. Registering an adapter never opens a UI or sends a message.
        public void SetChatTransport(IPhoneChatTransport transport)
        {
            ResetChatRequests();
            chatTransport = transport;
        }

        public IReadOnlyList<PhoneChatContact> ChatContacts
        {
            get
            {
                if (session == null || session.State == null) return Array.Empty<PhoneChatContact>();
                Refresh();
                return SevenCharacterCatalog.Actors.Where(a => HasMetForChat(a.id)).Select(a => new PhoneChatContact(a)).ToArray();
            }
        }

        public bool CanMessage(string actorId)
        {
            if (session == null || session.State == null || string.IsNullOrEmpty(actorId)) return false;
            Refresh();
            return SevenCharacterCatalog.Find(actorId) != null && HasMetForChat(actorId);
        }

        bool HasMetForChat(string actorId)
        {
            var current = session.State;
            if (current.phone?.metPeople?.Any(p => p.actorId == actorId) == true
                || current.firstDay?.greeted?.Contains(actorId) == true) return true;
            // The completed harbor conversation introduces Z before the square's roll call.
            return actorId == "z" && current.firstDay != null && current.firstDay.phase >= FirstDayPhase.WalkToSquare;
        }

        public void CancelMessage(string actorId)
        {
            if (actorId != null && pendingChats.TryGetValue(actorId, out var cancellation)) cancellation.Cancel();
        }

        void ResetChatRequests()
        {
            chatGeneration++;
            var previous = pendingChats.Values.ToArray();
            pendingChats.Clear();
            foreach (var pending in previous) pending.Cancel();
        }

        public async Task<PhoneChatResult> SendMessageAsync(string actorId, string message, CancellationToken cancellation = default)
        {
            if (this == null || session == null || session.IsAtTitle || !session.IsReady || session.State == null)
                return new PhoneChatResult(PhoneChatStatus.NotReady);
            if (!CanMessage(actorId)) return new PhoneChatResult(PhoneChatStatus.NotMet);
            if (string.IsNullOrWhiteSpace(message) || message.Trim().Length > 1000)
                return new PhoneChatResult(PhoneChatStatus.InvalidMessage);
            if (cancellation.IsCancellationRequested) return new PhoneChatResult(PhoneChatStatus.Cancelled);
            if (chatTransport == null || string.IsNullOrEmpty(state.memory?.worldId))
                return new PhoneChatResult(PhoneChatStatus.Unavailable);
            if (pendingChats.ContainsKey(actorId)) return new PhoneChatResult(PhoneChatStatus.Busy);

            var owner = state;
            var phone = owner.phone;
            string worldId = owner.memory.worldId;
            int generation = chatGeneration;
            var request = new PhoneChatRequest(Guid.NewGuid().ToString("N"), worldId,
                "phone_" + StableId(worldId + ":" + actorId), actorId, message.Trim(), owner);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                pendingChats[actorId] = linked;
                try
                {
                    var replyTask = chatTransport.ReplyAsync(request, linked.Token);
                    if (replyTask == null) return new PhoneChatResult(PhoneChatStatus.Failed);
                    var deadline = Task.Delay(TimeSpan.FromSeconds(45), linked.Token);
                    if (await Task.WhenAny(replyTask, deadline) != replyTask)
                    {
                        bool cancelled = linked.IsCancellationRequested;
                        linked.Cancel();
                        _ = ObserveCompletion(replyTask);
                        return new PhoneChatResult(!CurrentContext() ? PhoneChatStatus.ContextChanged
                            : cancelled ? PhoneChatStatus.Cancelled : PhoneChatStatus.TimedOut);
                    }
                    var reply = await replyTask;
                    if (!CurrentContext()) return new PhoneChatResult(PhoneChatStatus.ContextChanged);
                    if (linked.IsCancellationRequested) return new PhoneChatResult(PhoneChatStatus.Cancelled);
                    if (reply == null || reply.RequestId != request.RequestId || reply.WorldId != worldId || reply.ActorId != actorId
                        || string.IsNullOrWhiteSpace(reply.Text) || reply.Text.Length > 8000)
                        return new PhoneChatResult(PhoneChatStatus.Failed);

                    // Commit the pair atomically only after a correlated AI reply arrives.
                    phone.chatExchanges.Add(new PhoneChatExchange
                    {
                        requestId = request.RequestId, threadId = request.ThreadId, actorId = actorId,
                        message = request.Text, reply = reply.Text, day = request.Day, period = request.Period,
                        receivedUtc = DateTime.UtcNow.ToString("O")
                    });
                    return new PhoneChatResult(PhoneChatStatus.Delivered, reply, session.Save());
                }
                catch (OperationCanceledException)
                { return new PhoneChatResult(CurrentContext() ? PhoneChatStatus.Cancelled : PhoneChatStatus.ContextChanged); }
                catch (Exception)
                { return new PhoneChatResult(CurrentContext() ? PhoneChatStatus.Failed : PhoneChatStatus.ContextChanged); }
                finally
                {
                    if (pendingChats.TryGetValue(actorId, out var pending) && ReferenceEquals(pending, linked)) pendingChats.Remove(actorId);
                    linked.Cancel();
                }
            }

            bool CurrentContext() => this != null && session != null && !session.IsAtTitle && generation == chatGeneration
                && ReferenceEquals(session.State, owner) && ReferenceEquals(owner.phone, phone) && owner.memory?.worldId == worldId;
        }

        // Observe a transport that ignores cancellation without allowing a late reply into a new save.
        static async Task ObserveCompletion(Task<PhoneChatReply> task) { try { await task; } catch (Exception) { } }
    }
}
