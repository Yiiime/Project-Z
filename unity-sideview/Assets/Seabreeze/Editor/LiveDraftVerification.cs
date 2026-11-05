using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using ZLive;

namespace Seabreeze.Editor
{
    // Exercises real UI Toolkit change events and the same render path used by polling.
    public static class LiveDraftVerification
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Seabreeze/Verify Live Input Drafts")]
        public static void Run()
        {
            var root = new GameObject("Live draft verification") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var controller = root.AddComponent<LiveFeatureController>();
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                window.Show();
                var list = new ScrollView();
                var input = new TextField { maxLength = 360 };
                window.rootVisualElement.Add(list);
                window.rootVisualElement.Add(input);
                Field(controller, "songList", list);
                Field(controller, "input", input);
                var review = new JObject { ["id"] = "review", ["request"] = "审核唱段", ["status"] = "awaiting_review", ["assetId"] = "asset-one" };
                var other = new JObject { ["id"] = "other", ["request"] = "另一任务", ["status"] = "generating" };
                Render(controller, review, other);
                var card = Card(list, "审核唱段");
                card.Q<TextField>().value = "旋律和结尾待确认";
                card.Query<Toggle>().ToList()[0].value = true;
                other["status"] = "checking";
                Render(controller, review, other);
                card = Card(list, "审核唱段");
                Require(card.Q<TextField>().value == "旋律和结尾待确认", "Polling discarded the review note.");
                Require(card.Query<Toggle>().ToList()[0].value, "Polling discarded review checks.");
                Require(!card.Query<Toggle>().ToList()[1].value, "Polling approved an unchecked criterion.");
                review["assetId"] = "asset-two";
                Render(controller, review);
                card = Card(list, "审核唱段");
                Require(card.Q<TextField>().value == "" && !card.Query<Toggle>().ToList()[0].value, "Review leaked to a different audio asset.");
                review["status"] = "needs_input";
                Render(controller, review);
                Card(list, "审核唱段").Q<TextField>().value = "第一段副歌";
                Render(controller, review, other);
                Require(Card(list, "审核唱段").Q<TextField>().value == "第一段副歌", "Polling discarded clarification.");
                review["status"] = "failed";
                Render(controller, review);
                Card(list, "审核唱段").Q<Toggle>().value = true;
                Render(controller, review, other);
                Require(Card(list, "审核唱段").Q<Toggle>().value, "Polling discarded retry acknowledgement.");
                Render(controller);
                Render(controller, review);
                Require(!Card(list, "审核唱段").Q<Toggle>().value, "Removed task retained its acknowledgement.");
                foreach (int limit in new[] { 120, 360 })
                {
                    string transcript = new string('声', limit + 80);
                    Invoke(controller, "SetVoiceDraft", transcript, limit);
                    Require(input.value == transcript, "Long transcript was truncated before review.");
                    Invoke(controller, "SetVoiceDraft", "下一句", limit);
                    Require(input.maxLength == limit && input.value == "下一句", "Next draft did not restore its input limit.");
                }
                Debug.Log("LIVE_DRAFT_VERIFICATION_PASSED: review, clarification, retry, asset isolation, removal, and long transcripts");
            }
            finally
            {
                window.Close();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static VisualElement Card(ScrollView list, string title)
        {
            foreach (var card in list.Query<VisualElement>(className: "song-card").ToList())
                if (card.Q<Label>(className: "song-title").text == title) return card;
            throw new InvalidOperationException("Missing song card: " + title);
        }
        static void Render(LiveFeatureController controller, params JObject[] tasks)
            => Invoke(controller, "RenderSongs", new JArray(tasks));
        static void Field(object target, string name, object value)
            => target.GetType().GetField(name, Private).SetValue(target, value);
        static void Invoke(object target, string name, params object[] args)
            => target.GetType().GetMethod(name, Private).Invoke(target, args);
        static void Require(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
        }
    }
}
