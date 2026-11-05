#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Seabreeze.Editor
{
    public static class StoryContentBuilder
    {
        const string Root = "Assets/Seabreeze/Resources/Content";
        static readonly List<DialogueNode> Nodes = new List<DialogueNode>();
        [MenuItem("Seabreeze/Rebuild Game Content")]
        public static void Build()
        {
            Directory.CreateDirectory(Root);
            Nodes.Clear();
            Compose();
            ApplyPresentationMetadata();
            var database = Asset<StoryDatabase>("StoryDatabase");
            database.dialogues = Nodes.ToArray();
            database.npcs = new[] {
                Npc("npc_aya", "阿遥", "「潮汐」咖啡馆的店主，记得每一个常客喝咖啡的习惯。", new Color(.89f,.67f,.4f), new NpcSchedule { place = PlaceId.Cafe, from = TimePeriod.Morning, through = TimePeriod.Afternoon }, new NpcSchedule { place = PlaceId.Cafe, from = TimePeriod.Evening, through = TimePeriod.Night, forbiddenFlag = "choice_invite" }, new NpcSchedule { place = PlaceId.Lighthouse, from = TimePeriod.Evening, through = TimePeriod.Night, requiredFlag = "choice_invite" }),
                Npc("npc_chen", "陈伯", "退休的邮递员。自行车修了三十年，还是舍不得换。", new Color(.58f,.72f,.64f), new NpcSchedule { place = PlaceId.Street, from = TimePeriod.Morning, through = TimePeriod.Afternoon }, new NpcSchedule { place = PlaceId.Lighthouse, from = TimePeriod.Evening, through = TimePeriod.Night, requiredFlag = "choice_invite" }),
                Npc("npc_mio", "Z", "总带着一台旧相机。明天要去别的城市学摄影。", new Color(.73f,.64f,.88f), new NpcSchedule { place = PlaceId.Street, from = TimePeriod.Morning, through = TimePeriod.Afternoon }, new NpcSchedule { place = PlaceId.Lighthouse, from = TimePeriod.Evening, through = TimePeriod.Night })
            };
            database.items = new[] {
                Item("postcard", "旧明信片", "纸角泛黄。正面是灯塔下的几个人，背面写着：给下一位看到灯亮的人。"),
                Item("paper", "阿遥给的信纸", "从菜单背面裁下的纸。边缘留着淡淡的咖啡香。"),
                Item("photo", "一张照片的约定", "照片还在胶卷里，但这一次，大家都写下了地址。")
            };
            database.events = new[] {
                Event("afternoon_cloud", TimePeriod.Afternoon, PlaceId.Street, false, new[] { "postcard_found" }, Array.Empty<string>(), "云的影子慢慢越过街道。手机里有了新的消息。", F("afternoon_noticed", "一片云经过海面。今天好像不必着急。")),
                Event("late_chen", TimePeriod.Evening, PlaceId.Cafe, true, new[] { "cafe_story" }, new[] { "chen_story" }, "阿遥发来消息：陈伯的旧记录还在咖啡馆留言簿里。", F("late_hint_seen", "陈伯傍晚回家后，可以去咖啡馆翻留言簿；不必等到明天。")),
                Event("lighthouse_light", TimePeriod.Evening, PlaceId.Lighthouse, false, Array.Empty<string>(), Array.Empty<string>(), "灯塔亮了。有人在栏杆边等你。", F("light_seen", "灯塔的光落到水上。原来它不是一直亮着的，只是在有人需要的时候。")),
                Event("next_day", TimePeriod.Morning, PlaceId.Street, true, new[] { "postcard_found" }, new[] { "story_complete" }, "故事还没有结束。昨天的话，今天依然来得及。", F("return_day", "昨天没有说完的话，可以今天接着说。陈伯上午仍会在街上，Z傍晚还会去灯塔。"), 2)
            };
            database.InvalidateLookup();
            EditorUtility.SetDirty(database);
            Validate(database);
            AssetDatabase.SaveAssets();
            Debug.Log("Seabreeze story authored: " + database.dialogues.Length + " dialogue nodes, 3 NPCs, 3 items, 4 conditional events.");
        }
        static T Asset<T>(string name) where T : ScriptableObject
        {
            string path = Root + "/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); }
            return asset;
        }
        static NpcData Npc(string id, string name, string description, Color accent, params NpcSchedule[] schedule)
        {
            var result = Asset<NpcData>(id); result.id = id; result.displayName = name; result.description = description; result.accent = accent; result.schedule = schedule; EditorUtility.SetDirty(result); return result;
        }
        static ItemData Item(string id, string name, string description)
        {
            var result = Asset<ItemData>("item_" + id); result.id = id; result.displayName = name; result.description = description; EditorUtility.SetDirty(result); return result;
        }
        static StoryEventData Event(string id, TimePeriod period, PlaceId place, bool anywhere, string[] required, string[] forbidden, string notification, StoryEffect effect, int day = 1)
        {
            var result = Asset<StoryEventData>("event_" + id); result.id = id; result.minimumDay = day; result.earliestPeriod = period; result.place = place; result.anyPlace = anywhere; result.requiredFlags = required; result.forbiddenFlags = forbidden; result.notification = notification; result.effects = new[] { effect }; EditorUtility.SetDirty(result); return result;
        }
        static StoryEffect F(string flag = null, string journal = null, string item = null, string ending = null, string relation = null, int delta = 0)
            => new StoryEffect { flag = flag, journal = journal, item = item, ending = ending, relationship = relation, relationshipDelta = delta };
        static DialogueChoice C(string text, string next, params StoryEffect[] effects) => new DialogueChoice { text = text, next = next, effects = effects };
        static void N(string id, string speaker, string text, string next = null, StoryEffect[] effects = null, DialogueChoice[] choices = null)
            => Nodes.Add(new DialogueNode { id = id, speaker = speaker, text = text, next = next, effects = effects ?? Array.Empty<StoryEffect>(), choices = choices ?? Array.Empty<DialogueChoice>() });
        static void ApplyPresentationMetadata()
        {
            foreach (var node in Nodes)
            {
                // Resolve authored speakers once; narration, the player and quoted phone messages stay silent.
                node.speakerId = node.speaker == "Z" ? "npc_mio" : node.speaker == "阿遥" ? "npc_aya" : node.speaker == "陈伯" ? "npc_chen" : "";
                node.emotion = "neutral";
                node.motion = ""; // Let the selected presentation profile choose its default talking motion.
                if (node.speakerId != "npc_mio") continue;
                switch (node.id)
                {
                    case "mio_greeting":
                    case "ending_invite_4":
                        node.emotion = "happy_wave";
                        break;
                    case "mio_greeting_3":
                    case "mio_after":
                        node.emotion = "serious";
                        break;
                    case "mio_evening_deliver":
                    case "ending_deliver_3":
                    case "ending_keep_2":
                        node.emotion = "shy";
                        break;
                }
            }
        }
        static void Validate(StoryDatabase database)
        {
            var ids = new HashSet<string>();
            foreach (var node in database.dialogues) if (!ids.Add(node.id)) throw new InvalidOperationException("Duplicate dialogue " + node.id);
            foreach (var node in database.dialogues)
            {
                if (!string.IsNullOrEmpty(node.next) && !ids.Contains(node.next)) throw new InvalidOperationException("Missing next " + node.next);
                foreach (var choice in node.choices) if (!string.IsNullOrEmpty(choice.next) && !ids.Contains(choice.next)) throw new InvalidOperationException("Missing choice next " + choice.next);
            }
        }
        static void Compose()
        {
            N("intro", "海风捎来的信", "火车把你留在这座小镇，也把赶时间的习惯留在了身后。\n房东说，沿着海走，就不会迷路。", "intro_2");
            N("intro_2", "你", "手机上没有待办事项。难得。\n左边的咖啡馆刚开门，远处的灯塔还没亮。风把一张纸吹到长椅上。", "intro_3");
            N("intro_3", "汐风镇 · 第一天", "A / D 或方向键移动，空格跳跃。靠近人或物，按 E。\n时间会等你：只有坐下来休息、喝咖啡时，才会进入下一个时段。今天不必赶。", effects: new[] { F("intro_read") });

            N("postcard", "一张旧明信片", "纸被海风磨得很软。照片上，一座灯塔、四把椅子、几个人。\n边角有咖啡渍，背面却干干净净，没有邮票。", "postcard_2");
            N("postcard_2", "你", "「给下一位看到灯亮的人。」\n地址写得这么随意，难怪没有寄出去。照片里那家咖啡馆，好像就在街口。", "postcard_3");
            N("postcard_3", "你", "先替它避避风吧。\n把明信片夹进外套口袋时，你发现背面还有一句很淡的字：\n「今天发生了一件小事——」", effects: new[] { F("postcard_found", "在海边拾到一张旧明信片。上面写着「给下一位看到灯亮的人」。照片里的店，像街口的「潮汐」咖啡馆。", "旧明信片") });
            N("postcard_empty", "长椅边", "刚才压着明信片的地方，留下一小块没有褪色的木头。\n风继续吹着。长椅还留着一个位置，这里也适合坐一会儿。");

            N("aya_greeting", "阿遥", "欢迎。靠窗的位置有风，靠里有猫毛。\n猫今天没来，但它的毛很守时。你可以慢慢选。", "aya_greeting_2");
            N("aya_greeting_2", "阿遥", "第一次来汐风镇？沿海往右走就是灯塔。\n不过现在去有点晒。不如先在街上转转，长椅那边能听清海浪。", effects: new[] { F("aya_met", "「潮汐」的店主阿遥说，海边长椅能听清浪声。她把猫毛也当成店里的一位常客。", relation: "npc_aya", delta: 1) });
            N("aya_card", "阿遥", "咦，这张照片……我见过。\n这扇窗以前是绿色的，我爸每年都说要重漆，每年都说等海风小一点。", "aya_card_2");
            N("aya_card_2", "你", "所以一直没有重新漆过？", "aya_card_3");
            N("aya_card_3", "阿遥", "对。他说旧颜色看习惯了。其实是拖延。\n照片里最边上、只露出半只手的就是我。我那时负责端空杯子，总觉得自己也在工作。", "aya_card_4");
            N("aya_card_4", "阿遥", "他们以前会把一天里很小的事写在卡片上，放进灯塔旁的旧邮筒。\n不是寄给谁，就是给下一个偶然打开它的人。", "aya_card_5");
            N("aya_card_5", "阿遥", "后来大家忙起来，就忘了。柜子挪过一次，照片也散了。\n这张大概是今天晒旧菜单时被吹出去的。", "aya_card_6");
            N("aya_card_6", "阿遥", "陈伯知道得更多。他以前骑那辆蓝色自行车送信，上午、午后常在街上。\n他不在的话，翻翻店里的留言簿——他把旧邮路也写进去了。", "aya_card_7");
            N("aya_card_7", "阿遥", "给你两张纸。菜单背面，很干净。\n如果哪天你也遇到一件小事，刚好可以写下来。", effects: new[] { F("cafe_story", "阿遥认出了照片。小镇从前有个习惯：把一天里的小事写给「下一位看到灯亮的人」，放进灯塔的旧邮筒。陈伯知道来历；他不在时，可以翻咖啡馆的留言簿。", "阿遥给的信纸", relation: "npc_aya", delta: 1) });
            N("aya_hint", "阿遥", "陈伯的蓝色自行车停在海边，他应该没走远。\n如果已经傍晚，就看留言簿蓝色书签那一页。他每次讲完故事都会补上日期。");
            N("aya_repeat", "阿遥", "你拿着那张纸的样子，好像也开始认真过今天了。\n别急着找值得写的事。咖啡凉了，也算一件事。");
            N("aya_invited", "阿遥", "Z发消息了，傍晚去灯塔，对吧？\n我把最后一壶咖啡装进保温瓶。陈伯说要换件外套——明明照片上只会露出半只手。", effects: new[] { F("aya_invite_reply", "阿遥准备带一壶咖啡去灯塔。陈伯也答应了，正在找一件适合拍照的外套。") });
            N("aya_after", "阿遥", "刚刚关灯的时候，我忽然想起：今天没有等海风小一点。\n我们就那样出门了。好像也没有什么不行。\n再坐一会儿吧。这杯请你。");

            N("aya_gathering", "阿遥", "保温瓶里的咖啡还热着。我带了四个杯子，刚刚好。\n店关一会儿也没关系，门上已经贴了纸条：店主去过今天了，稍后回来。", effects: new[] { F("aya_gathered", "阿遥把店暂时关了，带着四个杯子来到灯塔。门上写着：店主去过今天了，稍后回来。") });
            N("chen_gathering", "陈伯", "我把自行车停在下面了，不占你们的镜头。\n这件外套怎么样？三十年前拍那张照片的时候，就是这一件。\n……不是舍不得扔，是真的还合身。", effects: new[] { F("chen_gathered", "陈伯穿着旧照片里的那件外套来到灯塔。他说，不是舍不得扔，是真的还合身。") });
            N("chen_greeting", "陈伯", "别踩那块翘起来的石板，水会从另一边溅上来。\n我年轻时送信，靠它记住了半条街的人家。", "chen_greeting_2");
            N("chen_greeting_2", "陈伯", "来散步就很好。这里的风有时会把东西送错地方。\n长椅那边刚飞过去一张纸，你要是顺路，帮它捡起来。", effects: new[] { F("chen_met", "陈伯提醒我别踩街上的松石板。他以前送信，现在仍然记得每一段路。", relation: "npc_chen", delta: 1) });
            N("chen_before_cafe", "陈伯", "这张卡片我认得，不过照片的故事，让阿遥先讲给你听吧。\n有些事我们大人记得不一样，小时候的人反而记得清楚。她就在街口的咖啡馆。");
            N("chen_card", "陈伯", "「给下一位看到灯亮的人」……这几个字，是我写的。\n那天一整船的信被雨泡湿了，我送到天黑，一句好话都没敢说。", "chen_card_2");
            N("chen_card_2", "陈伯", "阿遥她爸端了杯热咖啡出来，说：今天发生了一件小事，陈师傅把所有信都送到了。\n我说，这哪里是小事，明明是一场大祸。", "chen_card_3");
            N("chen_card_3", "陈伯", "他说，信还是送到了呀。\n后来我们就开始记这些：修好的门铃，开花的薄荷，错过船却看见的日落。", "chen_card_4");
            N("chen_card_4", "陈伯", "邮筒后来停用了，卡片却留下了。\n这张没写完，不是有什么遗憾——只是咖啡来了，我们就把笔放下了。", "chen_choice");
            N("guestbook_chen", "蓝色书签", "「邮路第十二页，陈记。」\n有一页字迹很慢。陈伯写下了那场大雨，泡湿的信，以及阿遥父亲递来的热咖啡。", "guestbook_chen_2");
            N("guestbook_chen_2", "陈伯的笔迹", "「他说，今天发生了一件小事，所有的信都送到了。\n后来我们便把这样的小事写给下一位路过的人。不是为了证明这天值得，只怕自己忘得太快。」", "guestbook_chen_3");
            N("guestbook_chen_3", "陈伯的笔迹", "「有张明信片没写完。别替我们难过。\n那天咖啡上来了，我们忙着喝，谁也没记得拿笔。\n如果它到了你手上，写不写、留不留，都随你。」", "chen_choice");
            N("chen_choice", "一张留白的明信片", "旧故事到这里就讲完了。那行未写完的字，还在等今天的事情。\n你想怎么对待这张明信片？", choices: new[] {
                C("把今天写进去，留给下一位旅人。", "choice_deliver", F("choice_deliver")),
                C("先留给自己。不是每件事都要分享。", "choice_keep", F("choice_keep")),
                C("把大家约到灯塔，再拍一张照片。", "choice_invite", F("choice_invite"))
            });
            N("choice_deliver", "你", "不知道谁会读到。也许是下个周末的人，也许是很多年以后的人。\n但「今天有一个人捡起了这张纸」，已经能写一行了。", "choice_deliver_2");
            N("choice_deliver_2", "你", "手机里跳出Z的消息：她傍晚会去灯塔。\n到时候再问问她，有没有一句想带给陌生人的话。", effects: new[] { F("chen_story", "我想把今天写进明信片，留给下一位旅人。Z傍晚会去灯塔；在那之前，可以继续散步，或坐下来等一会儿。", relation: "npc_chen", delta: 1) });
            N("choice_keep", "你", "这不是欠谁的一封信。\n也许先留在自己的口袋里，等某天想起今天，再慢慢把后半句写完。", "choice_keep_2");
            N("choice_keep_2", "你", "Z发来消息，说她傍晚在灯塔。\n不一定要交出什么，才能赴一个约。先去看看海也好。", effects: new[] { F("chen_story", "我决定先留住明信片。自己的小事，也可以只属于自己。Z傍晚会去灯塔，我答应过去坐坐。", relation: "npc_chen", delta: 1) });
            N("choice_invite", "你", "照片里的人，有的搬走了，有的还在这条街。\n不用等他们都回来。今天在这里的人，先坐到一起吧。", "choice_invite_2");
            N("choice_invite_2", "手机消息 · Z", "好啊！最后一格胶卷正好还没想好拍什么。\n我喊阿遥，她喊陈伯。傍晚灯塔下见。谁也不用摆姿势。", effects: new[] { F("chen_story", "我想请今天在这里的人重新拍一张照片。Z带相机，阿遥带咖啡，陈伯答应来。傍晚灯塔下见。", relation: "npc_chen", delta: 1) });
            N("chen_repeat", "陈伯", "送信送久了，会以为每张纸都得有个去处。\n退休才知道，夹在书里、放在抽屉里，也都是去处。\n你自己决定就好。傍晚去灯塔，风会比这里凉些。");

            N("mio_greeting", "Z", "等一下——别动。\n……好了。我在拍你后面的光，不是在拍你。\n不过你刚好挡住了那根难看的电线，也算帮忙。", "mio_greeting_2");
            N("mio_greeting_2", "你", "胶卷相机？", "mio_greeting_3");
            N("mio_greeting_3", "Z", "嗯。只剩最后一格。\n明天我要去外地上摄影课，一直想把最后一张留给特别的东西。结果什么都觉得差一点。", "mio_greeting_4");
            N("mio_greeting_4", "Z", "傍晚我会去灯塔。那里每一天都差不多，却每一天都有人拍。\n也许我只是把「特别」想得太难了。你有空可以过来，我给你留个靠海的位置。", effects: new[] { F("mio_met", "认识了Z。她的胶卷只剩最后一格，明天要出门学摄影。她傍晚会在灯塔，想把最后一张留给今天。", relation: "npc_mio", delta: 1) });
            N("mio_day_repeat", "Z", "我刚才拍了三次风……当然，一次快门也没按。\n对了，阿遥最喜欢认旧照片。你要是捡到什么，她大概都知道。\n我到傍晚再往灯塔走。");
            N("mio_day_plan", "Z", "听说你也和那张旧明信片熟起来了。\n那我们傍晚灯塔见。还早的话，坐长椅发会儿呆，或者去阿遥那里喝一杯。\n这镇上最不缺的就是等人的地方。");
            N("mio_wait", "Z", "你来啦。灯刚亮，刚好赶上。\n那张明信片的故事你听完了吗？阿遥知道照片的事，陈伯的记录在咖啡馆留言簿里。\n你慢慢去，我晚一点还在这里。");
            N("mio_evening_deliver", "Z", "阿遥说了。你想把卡片留给下一个人。\n我想了半天可以写什么，写「今天的海很好看」是不是太普通？", "mio_evening_deliver_2");
            N("mio_evening_deliver_2", "你", "如果读到的人那天没有看海，就不普通。", "mio_evening_deliver_3");
            N("mio_evening_deliver_3", "Z", "那就写这个。\n我也终于想到最后一张拍什么了——你低着头写字，我在旁边努力挡风。\n邮筒就在那边。我不催你。", effects: new[] { F("evening_mio", "灯塔亮起后，Z说她想写「今天的海很好看」。对没见到今天的海的人来说，这句话一点也不普通。现在去旧邮筒，把卡片留下。", relation: "npc_mio", delta: 2) });
            N("mio_evening_keep", "Z", "你要把卡片带走啊。挺好的。\n我以前总觉得拍到的照片必须发出去。后来发现，有些照片只要自己再看一眼就够了。", "mio_evening_keep_2");
            N("mio_evening_keep_2", "你", "会不会让下一个来的人扑个空？", "mio_evening_keep_3");
            N("mio_evening_keep_3", "Z", "他还会遇到海、灯和别的事情。你不用替每个陌生人准备好一天。\n邮筒里有空白卡片。留一句「今天的海很好看」也行，什么都不写也行。\n走吧，我陪你过去。", effects: new[] { F("evening_mio", "Z说，不必替每个陌生人准备好一天。旧明信片可以带走；我想在灯塔的邮筒里，留下自己愿意留下的一句话。", relation: "npc_mio", delta: 2) });
            N("mio_evening_invite", "Z", "阿遥把咖啡放在那边了。陈伯去给自行车找个不会挡镜头的位置。\n他说自己不怕拍照，已经整理了四次领子。", "mio_evening_invite_2");
            N("mio_evening_invite_2", "你", "大家真的不用坐得和旧照片一样吗？", "mio_evening_invite_3");
            N("mio_evening_invite_3", "Z", "不用。我们又不是回来替谁补拍的。\n今天坐在这里的人，就是今天的人。\n先把名字写在邮筒里的卡片背面吧。等照片冲出来，我就知道寄给谁了。", effects: new[] { F("evening_mio", "阿遥带来了咖啡，陈伯正在整理领子。Z说，不必和旧照片站得一样，今天的人就是今天的人。去旧邮筒写下名字和地址。", relation: "npc_mio", delta: 2) });
            N("mio_evening_repeat", "Z", "旧邮筒就在灯塔旁边。\n我帮你挡风。写多久都可以。");
            N("mio_after", "Z", "最后一格拍完了，反而轻松。\n明天去别的地方，不是因为这里不够好看。\n只是想多认识几种光，然后回来认一认这里的。谢谢你陪我用完今天。");

            N("ending_deliver", "旧邮筒", "木盖打开时，有一声很轻的响。\n里面没有山一样的信，只有几张卡片：\n「薄荷活了。」「今天没有错过船。」「听到了小时候的歌。」", "ending_deliver_2");
            N("ending_deliver_2", "你写下", "「今天，我在陌生的镇上捡到一张纸。\n有人认出了照片，有人记得那天的雨。\n我们没有做什么大事。傍晚的海很好看。」", "ending_deliver_3");
            N("ending_deliver_3", "Z", "好了。\n……不是说你的字。我是说，照片拍好了。\n刚才你笑了一下，自己大概没发现。", "ending_deliver_4");
            N("ending_deliver_4", "海风捎来的信", "卡片滑进邮筒。下一个读到它的人，不会知道你在这里站了多久。\n但他会知道，这一天有人好好看过海。\n\n今日记忆 · 写给下一位旅人", effects: new[] { F("story_complete", "我把今天写进了明信片。它没有收件人的名字，却已经有了去处。Z拍下了我投卡片的样子。今天的海很好看。", "一张照片的约定", "写给下一位旅人") , new StoryEffect { removeItem = "旧明信片" } });
            N("ending_keep", "旧邮筒", "邮筒里有几张空白卡片。\n你把旧明信片妥帖地夹回口袋，拿起另一张，写了一句：\n「今天，有个人提醒我，喜欢的东西可以先留给自己。」", "ending_keep_2");
            N("ending_keep_2", "Z", "可以让我也记住这句话吗？\n……不是要你送给我。我自己的那份，我会记着。", "ending_keep_3");
            N("ending_keep_3", "海风捎来的信", "快门轻响。你带走一张旧卡片，也留下了一句新的话。\n有些记忆不必交出去，依然能让另一个人靠近一点。\n\n今日记忆 · 口袋里的海", effects: new[] { F("story_complete", "旧明信片留在口袋里。我往邮筒投了一句新话。Z说她会记住自己的那份。分享和保留，原来可以同时发生。", "一张照片的约定", "口袋里的海") });
            N("ending_invite", "旧邮筒", "你从邮筒里拿出一张空白卡片，把旧照片放在旁边。\n阿遥的杯子还冒着热气，陈伯终于停下了对领子的检查。", "ending_invite_2");
            N("ending_invite_2", "阿遥", "这次我得完整地站进去，上次只有半只手。\n陈伯，往这边一点。不是那么多——好，现在谁都别动。", "ending_invite_3");
            N("ending_invite_3", "陈伯", "等一下，摄影师怎么办？\n相机放这儿，定时。我送过那么多相机说明书，总该会一个按钮。", "ending_invite_4");
            N("ending_invite_4", "Z", "那就十秒。\n九、八……算了，不用数。\n大家看着海就好。", "ending_invite_5");
            N("ending_invite_5", "海风捎来的信", "快门响的时候，没人站得像旧照片里那样。\n今天有今天的风，有人没来，也有人刚好来到这里。\n卡片背面写满了名字。\n\n今日记忆 · 今天的人，今天的合照", effects: new[] { F("story_complete", "我们拍了一张新的合照。阿遥终于不再只露出半只手，陈伯帮Z设好定时。旧明信片和写满名字的新卡片一起留在了邮筒里，等待照片寄回。", "一张照片的约定", "今天的人，今天的合照"), new StoryEffect { removeItem = "旧明信片" } });
            N("letterbox_after", "旧邮筒", "木盖被你重新扣好。\n有些事情已经结束，街道和海还在。\n你可以再去坐一会儿，也可以把今天留在这里。");
            N("letterbox_first", "旧邮筒", "它已经不收邮政信件，投信口却被擦得很干净。\n旁边小字写着：「给下一位看到灯亮的人。」\n这好像是一个还没有讲完的故事。");
            N("letterbox_wait", "旧邮筒", "和明信片上是同一句话。\n但在写下今天之前，你想先知道，昨天的人为什么把它留下。\n去咖啡馆问阿遥，再找陈伯，或读读店里的留言簿吧。");
            N("letterbox_mio", "旧邮筒", "信纸在风里翻了一面。\n你答应了Z，傍晚在这里碰面。先和她说几句话吧。\n如果还早，附近的长椅可以坐一坐。");

            N("noticeboard", "社区告示板", "「周六修补日：坏掉的台灯、书包、心情，都可以带来。\n心情不保证修好，但有热茶。」\n下面还有一张便条：寻找一只每周三来咖啡馆上班的橘猫。", effects: new[] { F("seen_noticeboard", "告示板上写着：坏心情不保证修好，但有热茶。镇上的修补日是周六。") });
            N("bicycle", "旧自行车", "车铃上有一道浅浅的凹痕，车筐用蓝绳补了三次。\n坐垫下面别着一张纸：\n「不用推，谢谢。我只是停下来看看海。——陈」", effects: new[] { F("seen_bicycle", "陈伯的自行车不是坏了，只是主人停下来看海。以后别把所有停下来的人，都当成需要帮忙的人。") });
            N("sea", "海面", "浪碰到岸边，又退回去。\n你试着分辨每一声的不同，听到第三次就忘了刚才想的事情。\n也挺好。", effects: new[] { F("heard_sea", "在海边站了一会儿。没有得到什么答案，但忘记了一件原本觉得非想明白不可的事。") });
            N("radio", "窗边的收音机", "「……今天傍晚，沿海地区微风。接下来这首歌，送给等船的人。」\n旋钮有点松，音乐带着细小的沙沙声。阿遥在柜台后跟着哼了半句。", effects: new[] { F("heard_radio", "咖啡馆的收音机把一首歌送给等船的人。阿遥只记得半句旋律，却哼得很笃定。") });
            N("guestbook", "咖啡馆留言簿", "「第一次来，不喜欢苦咖啡。」「第三次来，还是不喜欢。」\n同一种字迹，隔了三年。再往后翻：\n「今天带女儿来了。她也不喜欢。」\n阿遥在旁边画了三个小小的笑脸。", effects: new[] { F("read_guestbook", "留言簿里，一位客人用了三年确认自己不喜欢苦咖啡，却一直来这家店。大概有些地方不是因为东西好喝，才让人回来。") });
            N("telescope", "望远镜", "镜筒里看得到一条慢慢移动的船。\n船上的人，大概也正看着岸边一个小小的你。\n你试着挥手。没有人回应，但感觉并不坏。", effects: new[] { F("used_telescope", "从灯塔的望远镜里看见一条船。我朝它挥了挥手，不知道有没有人看见。") });
            N("telescope_evening", "望远镜", "海的颜色已经变了，远处的窗一盏盏亮起来。\n每一扇窗里，大概都有人正结束自己的今天。\n你把眼睛从镜筒上移开，海忽然显得更宽。", effects: new[] { F("used_telescope_evening", "黄昏的海上，灯一点点亮起来。每一盏后面都有人正结束自己的今天。") });

            N("rest", "海边长椅", "木头被晒得有一点暖。\n要坐到下一个时段吗？坐着休息会推进时间；其他探索不会。", choices: new[] { C("坐一会儿，等海风慢慢吹过。", "rest_pass"), C("现在还不累，再走走。", "rest_cancel") });
            N("rest_pass", "你", "有人推着空车经过，云的边缘换了一个形状。\n你什么都没做，今天却也没有因此少掉什么。", effects: new[] { new StoryEffect { advanceTime = true } });
            N("rest_cancel", "你", "再走一小段吧。长椅不会跑掉。");
            N("rest_night", "夜色里的长椅", "已经很晚了。房东还给你留着灯。\n回去休息，明早再来吗？没有说完的话和探索记录都会保留。", choices: new[] { C("回去睡一觉，明天继续。", "sleep_pass"), C("再看看夜里的海。", "rest_cancel") });
            N("sleep_pass", "翌日", "你把外套挂在窗边，口袋里的纸轻轻响了一声。\n一夜过去，海还在那里。", effects: new[] { new StoryEffect { advanceTime = true } });
            N("coffee", "一杯咖啡", "杯子旁边有半块饼干。阿遥说是「烤碎的」，碎得很整齐。\n慢慢喝完，会来到下一个时段。", choices: new[] { C("慢慢喝，不看手机。", "coffee_pass"), C("先留着，我一会儿回来。", "coffee_cancel") });
            N("coffee_pass", "阿遥", "喝完啦？刚刚你一直在看窗外。\n没关系。我们家的窗景，不另外收费。", effects: new[] { F("first_coffee", "在潮汐咖啡馆喝了一杯咖啡，没有看手机。饼干明明碎得很整齐。"), new StoryEffect { advanceTime = true } });
            N("coffee_cancel", "阿遥", "好。下次给你倒新的，这杯我替你喝。");
            N("coffee_self", "柜台上的纸条", "阿遥不在。柜台留着一小壶咖啡，旁边压着纸条：\n「杯子自己拿，饼干也可以。喝完放在水池边就好。」\n慢慢喝完，会来到下一个时段。", choices: new[] { C("给自己倒一杯，坐到窗边。", "coffee_self_pass"), C("先不喝了，再去走走。", "coffee_self_cancel") });
            N("coffee_self_pass", "你", "没有人隔着柜台问你好不好喝，窗边的位置也没有变。\n你喝完，把杯子洗净，倒扣在沥水架上。\n桌上的纸条被风吹起一角，又轻轻落下。", effects: new[] { F("first_coffee", "阿遥出门后，咖啡馆留了一小壶咖啡给客人自己倒。我喝完洗好了杯子，忽然觉得自己也稍微熟悉了这里。"), new StoryEffect { advanceTime = true } });
            N("coffee_self_cancel", "你", "把杯子放回原处。\n纸条上还有一行小字：「不想喝也没关系，坐一会儿就好。」");
            N("coffee_self_night", "你", "咖啡壶已经空了，热水壶旁还放着花草茶。\n阿遥留下的纸条写着：「晚上别再喝咖啡。照顾好明天的自己。」\n泡杯茶，喝完回去休息吗？会来到第二天上午。", choices: new[] { C("泡一杯，喝完回去睡觉。", "sleep_pass"), C("还想再看看夜里的海。", "coffee_self_cancel") });
            N("coffee_night", "阿遥", "这个点再喝咖啡，明天会恨我的。\n给你换一杯热牛奶？喝完就回去睡，明天还开门。", choices: new[] { C("好，喝完回去休息。", "sleep_pass"), C("还想再走一会儿。", "coffee_cancel") });
        }
    }
}
#endif
