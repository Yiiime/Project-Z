using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ProjectZ.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static int passed;
    private static int failed;
    private static EpisodeCatalog Catalog() => JsonSerializer.Deserialize<EpisodeCatalog>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "episodes.json")), Json);
    private static GameSession Session() => new GameSession(Catalog());
    private static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Throws(Action operation) { try { operation(); } catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; } catch (InvalidDataException) { return; }
        throw new Exception("Expected rejection"); }
    private static void Test(string name, Action operation)
    {
        try { operation(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }

    private static int Main()
    {
        Test("Real catalog has two independently playable episodes", () => {
            var session = Session();
            foreach (var e in Catalog().episodes) session.Choose(e.id, e.choices[0].id);
            Check(session.Snapshot().events.Count == 2);
        });
        Test("A third data-only episode uses the same game rules", () => {
            var catalog = Catalog();
            var third = JsonSerializer.Deserialize<EpisodeDefinition>(JsonSerializer.Serialize(catalog.episodes[0], Json), Json);
            third.id = "new_event";
            catalog.episodes.Add(third);
            var session = new GameSession(catalog);
            session.Choose(third.id, third.choices[0].id);
            session.NextDay();
            var request = session.BeginFollowup(third.id);
            Check(session.Accept(request, ResponseRules.Authored(request), out _));
        });
        Test("Different choices produce different next-day continuations", () => {
            var a = Session(); var b = Session();
            a.Choose("map_check", "verify_notice"); b.Choose("map_check", "decide_for_z");
            a.NextDay(); b.NextDay();
            Check(a.BeginFollowup("map_check").authoredText != b.BeginFollowup("map_check").authoredText);
            Check(a.Snapshot().respect == 1 && b.Snapshot().respect == -1);
        });
        Test("New game contains no previous observations", () => {
            var old = Session(); old.Choose("tide_watch", "wait_together");
            var fresh = Session(); var request = fresh.Conversation("我们昨天在哪里？");
            Check(fresh.Snapshot().slotId != old.Snapshot().slotId && request.observations.Count == 0);
        });
        Test("Claiming an event in chat does not create a world fact", () => {
            var session = Session(); var request = session.Conversation("我们昨天去了灯塔。");
            Check(session.Accept(request, ResponseRules.Authored(request), out _));
            Check(session.Snapshot().events.Count == 0);
        });
        Test("Unknown and duplicate choices leave state intact", () => {
            var session = Session(); Throws(() => session.Choose("missing", "x"));
            Throws(() => session.Choose("map_check", "missing"));
            Check(session.Snapshot().revision == 0);
            session.Choose("map_check", "verify_notice");
            Throws(() => session.Choose("map_check", "decide_for_z"));
            Check(session.Snapshot().events.Count == 1);
        });
        Test("Continuation needs next day and is consumed once", () => {
            var session = Session(); session.Choose("tide_watch", "wait_together");
            Throws(() => session.BeginFollowup("tide_watch")); session.NextDay();
            var request = session.BeginFollowup("tide_watch");
            Check(session.CanFollowup("tide_watch"));
            Check(session.Accept(request, ResponseRules.Authored(request), out _));
            Check(!session.CanFollowup("tide_watch")); Throws(() => session.BeginFollowup("tide_watch"));
        });
        Test("Rejected or cancelled continuation can be retried", () => {
            var session = Session(); session.Choose("tide_watch", "wait_together"); session.NextDay();
            var request = session.BeginFollowup("tide_watch"); var response = ResponseRules.Authored(request);
            response.sourceEventIds.Clear(); Check(!session.Accept(request, response, out _));
            Check(session.CanFollowup("tide_watch"));
            var retry = session.BeginFollowup("tide_watch"); Check(session.Accept(retry, ResponseRules.Authored(retry), out _));
        });
        Test("Late response is rejected after advancing time", () => {
            var session = Session(); var request = session.Choose("map_check", "verify_notice");
            session.NextDay(); Check(!session.Accept(request, ResponseRules.Authored(request), out _));
        });
        Test("Response from another save is rejected", () => {
            var a = Session(); var request = a.Choose("tide_watch", "wait_together");
            var b = Session(); Check(!b.Accept(request, ResponseRules.Authored(request), out _));
        });
        Test("Duplicate valid response is accepted only once", () => {
            var session = Session(); var request = session.Choose("map_check", "verify_notice");
            var response = ResponseRules.Authored(request);
            Check(session.Accept(request, response, out _)); Check(!session.Accept(request, response, out _));
        });
        Test("Nonexistent evidence and arbitrary actions are rejected", () => {
            var session = Session(); var request = session.Choose("map_check", "verify_notice");
            var response = ResponseRules.Authored(request); response.sourceEventIds.Add(Guid.NewGuid().ToString("N"));
            Check(!session.Accept(request, response, out _)); response = ResponseRules.Authored(request); response.action = "delete_save";
            Check(!session.Accept(request, response, out _));
        });
        Test("Null, too-long and unlabelled responses are rejected", () => {
            var session = Session(); var request = session.Conversation("你好");
            Check(!session.Accept(request, null, out _)); var r = ResponseRules.Authored(request); r.text = new string('a', 241);
            Check(!session.Accept(request, r, out _)); r = ResponseRules.Authored(request); r.mode = "unknown";
            Check(!session.Accept(request, r, out _));
        });
        Test("Snapshot edits do not mutate live state", () => {
            var session = Session(); session.Choose("map_check", "verify_notice");
            var snapshot = session.Snapshot(); snapshot.events[0].fact = "changed"; snapshot.episodes.Clear();
            Check(session.Snapshot().episodes.Count == 1 && session.Snapshot().events[0].fact != "changed");
        });
        Test("Invalid catalog cannot start a game", () => {
            var catalog = Catalog(); catalog.episodes.Add(catalog.episodes[0]); Throws(() => new GameSession(catalog));
            catalog = Catalog(); catalog.episodes[0].choices[0].respectDelta = int.MinValue; Throws(() => new GameSession(catalog));
        });
        Test("Corrupt state is rejected before cloning", () => {
            var state = Session().Snapshot(); state.events.Add(null); Throws(() => new GameSession(Catalog(), state));
        });
        Test("Forged relationship, revision and future facts are rejected", () => {
            var session = Session(); session.Choose("map_check", "verify_notice");
            var state = session.Snapshot(); state.respect = 3; Throws(() => new GameSession(Catalog(), state));
            state = session.Snapshot(); state.revision++; Throws(() => new GameSession(Catalog(), state));
            state = session.Snapshot(); state.events[0].day = 2; Throws(() => new GameSession(Catalog(), state));
        });
        Test("Restore preserves next-day state and exact evidence", () => InTemp(dir => {
            var session = Session(); session.Choose("tide_watch", "set_pace"); session.NextDay();
            var store = new SaveStore(dir, new Codec(), Catalog()); store.Save(session.Snapshot());
            var restored = new GameSession(Catalog(), store.Load(out var recovered));
            Check(!recovered && restored.CanFollowup("tide_watch"));
            Check(restored.BeginFollowup("tide_watch").observations[0].id == session.Snapshot().events[0].id);
        }));
        Test("Corrupt primary recovers backup and preserves it on next save", () => InTemp(dir => {
            var session = Session(); var store = new SaveStore(dir, new Codec(), Catalog());
            store.Save(session.Snapshot()); session.Choose("map_check", "verify_notice"); store.Save(session.Snapshot());
            File.WriteAllText(store.Path, "{incomplete");
            var recovered = store.Load(out var fromBackup); Check(fromBackup && recovered.events.Count == 0);
            store.Save(recovered); Check(File.ReadAllText(store.Path + ".bak").Contains("schemaVersion"));
            Check(!Directory.GetFiles(dir).Any(f => f.EndsWith(".tmp")));
        }));
        Test("Failure during encoding keeps prior save unchanged", () => InTemp(dir => {
            var good = new SaveStore(dir, new Codec(), Catalog()); var session = Session(); good.Save(session.Snapshot());
            var before = File.ReadAllText(good.Path);
            var broken = new SaveStore(dir, new FailingCodec(), Catalog());
            Throws(() => broken.Save(session.Snapshot())); Check(File.ReadAllText(good.Path) == before);
        }));
        Test("Unsupported save versions do not silently start a new game", () => InTemp(dir => {
            var store = new SaveStore(dir, new Codec(), Catalog()); var state = Session().Snapshot();
            state.schemaVersion = 2; File.WriteAllText(store.Path, JsonSerializer.Serialize(state, Json));
            Throws(() => store.Load(out _)); Check(File.Exists(store.Path));
        }));
        Console.WriteLine($"Core specs: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static void InTemp(Action<string> operation)
    {
        var dir = Path.Combine(Path.GetTempPath(), "project-z-specs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { operation(dir); } finally { Directory.Delete(dir, true); }
    }
    private sealed class Codec : IStateCodec
    {
        public string Encode(WorldState state) => JsonSerializer.Serialize(state, Json);
        public WorldState Decode(string value)
        {
            try { return JsonSerializer.Deserialize<WorldState>(value, Json); }
            catch (JsonException ex) { throw new InvalidDataException("Invalid JSON", ex); }
        }
    }
    private sealed class FailingCodec : IStateCodec
    {
        public string Encode(WorldState state) => throw new InvalidDataException("Simulated write preparation failure");
        public WorldState Decode(string value) => throw new InvalidDataException();
    }
}
