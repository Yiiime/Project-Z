extends SceneTree

const Core = preload("res://core/day_state.gd")
const SAVE_PATH := "user://rules_test/save.json"
var checks := 0
var failures: Array[String] = []
var group := ""


func _initialize() -> void:
	_test_route_a()
	_test_route_b()
	_test_work_opportunity_cost()
	_test_windows()
	_test_failure_recovery()
	_test_schedule_and_low_balance()
	_test_atomicity_and_inventory()
	_test_pending_restore()
	_test_night_settlement()
	_test_voluntary_call()
	_test_offline_call_replies()
	_test_save_validation()
	_test_fact_and_idempotency()
	_clean_save()
	if failures.is_empty():
		print("PASS: 13 causal groups, %d checks. D1 rules, pending recovery, receipts, bounded call replies and saves verified." % checks)
		quit(0)
	else:
		for failure in failures: printerr(failure)
		printerr("FAIL: %d / %d checks" % [failures.size(), checks])
		quit(1)


func _expect(value: bool, detail: String) -> void:
	checks += 1
	if not value: failures.append("[%s] %s" % [group, detail])


func _do(c, action: String, payload: Dictionary = {}, id: String = "") -> Dictionary:
	var result: Dictionary = c.apply(action, payload, id)
	_expect(result.ok, "%s should succeed: %s" % [action, result.get("reason", "")])
	return result


func _base():
	var c = Core.new()
	_do(c, "check_in")
	_do(c, "stage_work", {"correct": true})
	return c


func _evening(c, with_dinner: bool = true) -> void:
	_do(c, "wait", {"target": 720})
	_do(c, "lunch")
	if with_dinner:
		_do(c, "wait", {"target": 1080})
		_do(c, "dinner")
	_do(c, "wait", {"target": 1200})
	_do(c, "enter_stream")


func _finish(c) -> void:
	while c.data.segment < 3:
		var result: Dictionary = c.apply("advance_segment", {"expected_segment": c.data.segment})
		_expect(result.ok, "program segment should advance")
		if not result.ok: return
	_do(c, "sleep")


func _snapshot(c) -> String:
	return JSON.stringify(c.data, "", true, true)


func _test_route_a() -> void:
	group = "A / gift and observed conversation"
	var c = _base()
	_do(c, "buy_gift")
	_expect(c.clock() == "08:35" and c.data.gold == 44 and c.data.inventory.gift == 1, "A morning ledger")
	_do(c, "observe_bell")
	_evening(c)
	_do(c, "gift")
	_do(c, "chat", {"text": "我在码头听到铜铃和海浪，可以接成今晚的节拍。"})
	_expect(c.data.time == 1200 and c.data.gold == 44 and c.data.inventory.gift == 0 and c.data.recognition == 3, "two distinct response contributions, no double charge")
	_finish(c)
	_expect(c.data.phase == "done" and c.data.day == 1 and c.data.time == 1380, "one-day slice stops at D1 23:00")
	var minutes := 0
	for event in c.data.events: minutes += int(event.minutes)
	_expect(minutes == 960, "all clock movement covered by receipts")


func _test_route_b() -> void:
	group = "B / authored work"
	var c = _base()
	_do(c, "buy_material")
	_do(c, "craft", {"correct": true})
	_expect(c.clock() == "09:35" and c.data.gold == 48 and c.data.inventory.material == 0 and c.data.inventory.work == 1, "B morning material and money ledger")
	_expect(not c.data.inventory.has("tool"), "borrowed tools are never acquired")
	_evening(c)
	_expect(not c.preview("submit_work").ok, "work waits for main program")
	_do(c, "advance_segment", {"expected_segment": 0})
	_do(c, "submit_work")
	_do(c, "submit_work")
	_expect(c.data.inventory.work == 1 and c.data.recognition == 2 and c.data.gold == 48, "showing work does not consume it or farm contribution")
	_finish(c)


func _test_work_opportunity_cost() -> void:
	group = "schedule opportunity cost"
	var a = _base()
	_do(a, "buy_gift")
	_do(a, "wait", {"target": 520})
	_do(a, "coffee_am")
	_expect(a.data.time == 610 and a.data.gold == 74, "A can use 90 minutes for 30 extra gold")
	_expect(not a.preview("coffee_am").ok, "same shift cannot pay twice")
	var b = _base()
	_do(b, "buy_material")
	_do(b, "craft")
	_expect(not b.preview("coffee_am").ok, "B cannot both craft and catch 08:40 shift")
	_do(b, "wait", {"target": 720})
	_do(b, "lunch")
	_do(b, "coffee_pm")
	_expect(b.data.time == 930 and b.data.gold == 78, "afternoon job remains a recovery route")


func _test_windows() -> void:
	group = "full duration fits half-open windows"
	var c = Core.new()
	c.data.flags.checked_in = true
	c.data.time = 660
	_expect(c.preview("stage_work").ok, "11:00 stage ends exactly at 12:00")
	c.data.time = 661
	_expect(not c.preview("stage_work").ok, "11:01 stage exceeds attendance")
	c.data.time = 715
	_expect(c.preview("buy_gift").ok, "11:55 purchase finishes at 12:00")
	c.data.time = 716
	_expect(not c.preview("buy_gift").ok, "11:56 purchase cannot cross lunch")
	c.data.flags.stage_done = true
	c.data.inventory.material = 1
	c.data.time = 520
	_expect(c.preview("craft").ok, "08:40 latest teaching entry")
	c.data.time = 521
	_expect(not c.preview("craft").ok, "08:41 too late for full teaching")
	c.data.time = 1075
	_expect(c.preview("buy_gift").ok, "17:55 shop entry valid")
	c.data.time = 1080
	_expect(not c.preview("buy_gift").ok, "18:00 shop closed")


func _test_failure_recovery() -> void:
	group = "real failed activity and late recheck"
	var c = Core.new()
	_do(c, "check_in")
	var failed: Dictionary = _do(c, "stage_work", {"correct": false}, "wrong_stage")
	_expect(failed.receipt.outcome == "failure" and c.data.time == 510 and c.data.gold == 40, "wrong handover consumes 60 minutes but no wage")
	_expect(c.data.flags.stage_needs_review and not c.preview("stage_work").ok, "failure leaves a recheck instead of a second shift")
	_do(c, "stage_recheck", {}, "recheck_once")
	_expect(c.data.time == 525 and c.data.gold == 56 and c.data.flags.stage_done, "15-minute correction pays original wage once")
	_expect(not c.preview("craft").ok and not c.preview("coffee_am").ok, "failure has real morning opportunity cost")
	var snapshot := _snapshot(c)
	var replay: Dictionary = _do(c, "stage_recheck", {}, "recheck_once")
	_expect(replay.replayed and _snapshot(c) == snapshot and not c.preview("stage_recheck").ok, "duplicate correction cannot mint wages")
	var late = Core.new()
	_do(late, "check_in")
	_do(late, "wait", {"target": 660})
	_do(late, "stage_work", {"correct": false})
	_expect(not late.preview("stage_recheck").ok, "Chen is unavailable at 12:00")
	_do(late, "lunch")
	_do(late, "stage_recheck")
	_expect(late.data.gold == 56 and late.data.time == 855, "afternoon recheck salvages wage")


func _test_schedule_and_low_balance() -> void:
	group = "time-only actions and lunch schedule"
	var c = Core.new()
	_do(c, "check_in")
	c.data.gold = 0
	_expect(not c.preview("buy_gift").ok, "zero balance cannot purchase")
	_do(c, "wait", {"target": 520})
	_do(c, "coffee_am")
	_expect(c.data.gold == 30, "work needs no hidden energy or up-front coins")
	_expect(not c.preview("wait", {"target": 840}).ok, "wait cannot erase compulsory lunch")
	_do(c, "wait", {"target": 720})
	_do(c, "lunch")
	_expect(c.data.time == 840 and c.data.gold == 30, "lunch is 120 minutes and free")
	_do(c, "wait", {"target": 1200})
	_do(c, "enter_stream")
	_expect(not c.data.flags.get("dinner_done", false), "skipping dinner does not lock streaming")
	for name in ["energy", "stamina", "focus"]: _expect(not c.data.has(name), "no replacement budget: " + name)


func _test_atomicity_and_inventory() -> void:
	group = "preview and rejected commands are pure"
	var c = Core.new()
	var before := _snapshot(c)
	c.preview("check_in")
	c.preview("craft")
	_expect(_snapshot(c) == before, "previews never mutate")
	_expect(not c.apply("stage_work", {"correct": true}).ok, "prerequisite rejection")
	_expect(not c.apply("buy_gift", {"price": 0}).ok, "caller cannot override purchase price")
	_expect(not c.apply("wait", {"target": "720"}).ok, "number string rejected")
	_expect(not c.apply("wait", {"target": 420.5}).ok, "fractional game-minute rejected")
	_expect(_snapshot(c) == before, "rejected commands retain exact state")
	c.data.gold = 11
	_expect(not c.apply("buy_gift").ok and c.data.gold == 11 and c.data.inventory.gift == 0, "insufficient money does not partly purchase")
	var b = _base()
	_do(b, "buy_material")
	before = _snapshot(b)
	_expect(not b.apply("craft", {"correct": false}).ok and _snapshot(b) == before, "unfinished tutorial does not consume material")
	_evening(c, false)
	before = _snapshot(c)
	_expect(not c.apply("gift").ok and not c.apply("submit_work").ok and _snapshot(c) == before, "empty inventory cannot create outputs")


func _test_pending_restore() -> void:
	group = "confirmed activities survive interruption"
	_clean_save()
	var c = Core.new()
	_do(c, "check_in")
	var started: Dictionary = c.begin_action("stage_work", {}, "pending_stage")
	_expect(started.ok and c.data.time == 450 and c.data.gold == 40, "begin reserves without early settlement")
	c.data.activity = {"step": 1, "carrying": true, "placed": false, "correct": true}
	_expect(c.save_to(SAVE_PATH), "save held crate: " + c.last_error)
	var loaded = Core.new()
	_expect(loaded.load_from(SAVE_PATH), "load held crate: " + loaded.last_error)
	_expect(loaded.data.pending.action == "stage_work" and loaded.data.activity.carrying and loaded.data.activity.step == 1, "restores exact active mini-activity")
	var before := _snapshot(loaded)
	_expect(not loaded.apply("wait", {"target": 520}).ok and not loaded.apply("buy_gift").ok and not loaded.apply("stage_work", {"correct": true}, "other").ok, "pending locks competing actions and identity")
	_expect(_snapshot(loaded) == before, "blocked pending actions are pure")
	_expect(not loaded.apply("stage_work", {"correct": true}, "pending_stage").ok, "unplaced crate cannot settle the activity")
	loaded.data.activity = {"step": 2, "carrying": false, "placed": true, "correct": true}
	_do(loaded, "stage_work", {"correct": true}, "pending_stage")
	_expect(loaded.data.time == 510 and loaded.data.gold == 56 and loaded.data.pending.is_empty() and loaded.data.activity.is_empty(), "completion settles once and clears activity")
	_expect(loaded.save_to(SAVE_PATH), "save completed receipt")
	_expect(loaded.load_from(SAVE_PATH), "reload committed receipt")
	before = _snapshot(loaded)
	var repeated: Dictionary = _do(loaded, "stage_work", {"correct": true}, "pending_stage")
	_expect(repeated.replayed and _snapshot(loaded) == before, "reloaded old confirmation cannot pay again")
	var b = _base()
	_do(b, "buy_material")
	_expect(b.begin_action("craft", {}, "pending_craft").ok, "begin craft")
	b.data.activity = {"step": 2, "recipe": 2}
	_expect(b.data.pending.reserved_items.material == 1 and b.data.inventory.material == 1, "craft reserves one real item")
	_expect(b.save_to(SAVE_PATH) and b.load_from(SAVE_PATH), "craft reservation and steps survive save")
	_expect(not b.apply("craft", {"correct": true}, "pending_craft").ok, "two steps cannot complete a three-step craft")
	b.data.activity.step = 3
	_do(b, "craft", {"correct": true}, "pending_craft")
	_expect(b.data.inventory.material == 0 and b.data.inventory.work == 1, "resumed craft consumes exactly once")
	var a = _base()
	_do(a, "wait", {"target": 520})
	_expect(a.begin_action("coffee_am", {}, "coffee_pending").ok, "confirm shift")
	a.data.activity = {"step": 2, "recipe": 2}
	_expect(a.save_to(SAVE_PATH) and a.load_from(SAVE_PATH), "coffee restores two finished orders")
	a.data.activity = {"step": 3, "recipe": 3}
	_do(a, "coffee_am", {"correct": true}, "coffee_pending")
	_do(a, "coffee_am", {"correct": true}, "coffee_pending")
	_expect(a.data.gold == 86 and a.data.time == 610, "three orders pay exactly one 30-gold wage")


func _test_night_settlement() -> void:
	group = "night interactions separate from program clock"
	var c = _base()
	_do(c, "buy_gift")
	_do(c, "buy_gift")
	_evening(c)
	var gold := int(c.data.gold)
	_do(c, "gift", {}, "gift_once")
	_do(c, "gift", {}, "gift_once")
	_expect(c.data.inventory.gift == 1 and c.data.time == 1200 and c.data.gold == gold, "duplicate send is no second debit")
	_do(c, "gift", {}, "second_gift")
	_expect(c.data.inventory.gift == 0 and c.data.recognition == 2, "another real gift costs inventory without farming credit")
	for i in range(5): _do(c, "chat", {"text": "晚上好 %d" % i})
	_expect(c.data.time == 1200, "five messages do not consume a program window")
	_do(c, "advance_segment", {"expected_segment": 0}, "advance0")
	var before := _snapshot(c)
	_do(c, "advance_segment", {"expected_segment": 0}, "advance0")
	_expect(_snapshot(c) == before, "network retry does not advance twice")
	_expect(not c.apply("advance_segment", {"expected_segment": 0}, "stale_button").ok and c.data.time == 1240, "old UI button rejected using expected segment")
	_finish(c)
	_expect(c.data.time == 1380 and c.data.gold == gold, "three advances = 120 minutes, recap = 60 minutes")


func _test_voluntary_call() -> void:
	group = "optional random call does not gate authored path"
	var seen_selected := false
	var seen_not_selected := false
	for seed_value in range(12):
		var c = _base()
		_do(c, "buy_material")
		_do(c, "craft")
		_evening(c)
		c.data.seed = seed_value
		_expect(not c.preview("call_draw").ok, "no draw without opt-in")
		_do(c, "call_join")
		_do(c, "call_cancel")
		_do(c, "call_join")
		_do(c, "advance_segment", {"expected_segment": 0})
		_do(c, "call_draw", {}, "one_draw")
		var drawn_state: String = c.data.call_state
		_expect(c.save_to(SAVE_PATH) and c.load_from(SAVE_PATH), "draw outcome persisted")
		_do(c, "call_draw", {}, "one_draw")
		_expect(c.data.call_state == drawn_state and not c.preview("call_draw").ok, "no reroll after load")
		if drawn_state == "selected":
			seen_selected = true
			if seed_value % 2 == 0:
				_do(c, "call_accept")
				_expect(not c.preview("advance_segment", {"expected_segment": 1}).ok, "finish connected call explicitly")
				_do(c, "call_end")
			else:
				_do(c, "call_decline")
		else:
			seen_not_selected = true
		_do(c, "submit_work")
		_expect(c.data.recognition == 2 and c.data.gold == 48 and c.data.time == 1240, "unselected and declined users retain same authored response")
		_finish(c)
	_expect(seen_selected and seen_not_selected, "deterministic fixtures cover both draw outcomes")


func _connect_call(c) -> void:
	# Select a deterministic fixture seed through the same documented RNG,
	# then use the public opt-in/draw/accept flow instead of setting call state.
	for seed_value in range(100):
		var rng := RandomNumberGenerator.new()
		rng.seed = seed_value
		if rng.randi_range(0, 1) == 0:
			c.data.seed = seed_value
			break
	_do(c, "call_join")
	_do(c, "advance_segment", {"expected_segment": 0})
	_do(c, "call_draw")
	_do(c, "call_accept")
	_expect(c.data.call_state == "connected", "fixture reaches real connected call")


func _call_replies(c) -> Array:
	var replies: Array = []
	for entry in c.data.messages:
		if entry.speaker == "z" and entry.kind == "call" and not entry.reply_to.is_empty(): replies.append(entry)
	return replies


func _resources(c) -> String:
	return JSON.stringify({"time": c.data.time, "gold": c.data.gold, "inventory": c.data.inventory, "flags": c.data.flags, "recognition": c.data.recognition}, "", true, true)


func _test_offline_call_replies() -> void:
	group = "bounded fact-grounded offline call replies"
	var unsupported_topics := [
		["我已经做好作品了。", "你把手边的材料做成了一个小作品"],
		["我做完一班咖啡了。", "这一班要记住不同客人的口味"],
		["我完成了场务交接。", "箱子放好以后，还要和陈叔核对清单"],
		["我听过码头铃声了。", "你听到的风、铃声和海浪"]
	]
	for topic in unsupported_topics:
		var empty = Core.new()
		_evening(empty, false)
		_connect_call(empty)
		var before := _resources(empty)
		_do(empty, "chat", {"text": topic[0]})
		var replies := _call_replies(empty)
		_expect(replies.size() == 1 and not replies[0].text.contains(topic[1]), "unsupported topic receives no invented completed experience: " + topic[0])
		_expect(_resources(empty) == before, "unsupported chat grants no facts, resources or relationship")

	for topic_index in range(4):
		var c = Core.new() if topic_index == 3 else _base()
		match topic_index:
			0:
				_do(c, "buy_material")
				_do(c, "craft")
			1:
				_do(c, "wait", {"target": 520})
				_do(c, "coffee_am")
			3:
				_do(c, "observe_bell")
		_evening(c, false)
		_connect_call(c)
		var before := _resources(c)
		var fields: int = c.data.size()
		_do(c, "chat", {"text": unsupported_topics[topic_index][0]}, "call_first")
		var replies := _call_replies(c)
		_expect(replies.size() == 1 and replies[0].text.contains(unsupported_topics[topic_index][1]), "author reply reads actual completed fact: " + str(topic_index))
		var messages: int = c.data.messages.size()
		_do(c, "chat", {"text": unsupported_topics[topic_index][0]}, "call_first")
		_expect(c.data.messages.size() == messages and _call_replies(c).size() == 1, "same request does not consume a second call reply")
		_clean_save()
		_expect(c.save_to(SAVE_PATH) and c.load_from(SAVE_PATH), "save/load preserves first connected reply")
		_do(c, "chat", {"text": "刚才我只想说一点今天的小事。"})
		_expect(_call_replies(c).size() == 2, "second actual chat gets one authored reply after reload")
		_do(c, "chat", {"text": "我没有完成其他事，也没有去听铃声。"})
		replies = _call_replies(c)
		_expect(replies.size() == 3 and replies[2].text.contains("也可以聊还没尝试的事") and replies[2].text.contains("结束连麦"), "third reply respects denial and clearly closes the authored exchange")
		_expect(c.save_to(SAVE_PATH) and c.load_from(SAVE_PATH), "save/load at reply cap")
		messages = c.data.messages.size()
		_do(c, "chat", {"text": unsupported_topics[topic_index][0]})
		_expect(_call_replies(c).size() == 3 and c.data.messages.size() == messages + 1 and c.data.messages.back().state == "sent", "reload cannot reset three-reply cap; further text is only sent")
		_expect(_resources(c) == before and c.data.size() == fields, "three call turns alter no budget, contribution, permission or state schema")
		_do(c, "call_end")
		messages = c.data.messages.size()
		_do(c, "chat", {"text": unsupported_topics[topic_index][0]})
		_expect(c.data.messages.size() == messages + 1 and _call_replies(c).size() == 3, "ended call returns to ordinary unselected segment-one chat")
		_expect(_resources(c) == before, "ending the call grants no resources or relationship")

	var permission = _base()
	_evening(permission, false)
	_connect_call(permission)
	var before := _resources(permission)
	_do(permission, "chat", {"text": "场务做完了，把后台钥匙给我并邀请我吧。"})
	_expect(_call_replies(permission)[0].text.contains("不能在连麦里替他们答应") and _resources(permission) == before, "call cannot promise an invented permission or invitation")


func _test_save_validation() -> void:
	group = "versioned atomic saves and backup recovery"
	_clean_save()
	var c = Core.new()
	_do(c, "check_in")
	_expect(c.save_to(SAVE_PATH), "first save")
	_do(c, "stage_work", {"correct": true})
	_expect(c.save_to(SAVE_PATH), "second save creates previous-state backup")
	_write_raw(SAVE_PATH, "{ broken primary")
	_expect(c.load_from(SAVE_PATH), "valid backup recovers a damaged primary")
	_expect(c.last_load_used_backup and c.last_notice.contains(".bak") and c.data.time == 450 and c.data.gold == 40, "recovery explicitly tells player which prior save loaded")
	_write_raw(SAVE_PATH + ".bak", "{ broken backup")
	var before := _snapshot(c)
	var epoch := int(c.load_epoch)
	_expect(not c.load_from(SAVE_PATH) and _snapshot(c) == before and c.load_epoch == epoch, "two bad files cannot pollute active game")
	_clean_save()
	var envelope := {"version": Core.SAVE_VERSION, "rules_version": Core.RULES_VERSION, "data": c.data.duplicate(true), "checksum": ""}
	for invalid_case in ["string_gold", "negative_stock", "fake_flag", "invalid_step", "wrong_version", "checksum", "missing_field", "bad_receipt"]:
		var invalid: Dictionary = envelope.duplicate(true)
		match invalid_case:
			"string_gold": invalid.data.gold = "40"
			"negative_stock": invalid.data.inventory.gift = -1
			"fake_flag": invalid.data.flags.z_invited = true
			"invalid_step": invalid.data.activity = {"step": "three"}
			"wrong_version": invalid.version = 2
			"missing_field": invalid.data.erase("seed")
			"bad_receipt": invalid.data.receipts = {"x": {"request_id": "x", "action_id": "x", "action": "chat", "outcome": "success", "message": "x", "payload": {"text": "a"}}}
		invalid.checksum = JSON.stringify(invalid.data, "", true, true).sha256_text()
		if invalid_case == "checksum": invalid.checksum = "wrong"
		_write_raw(SAVE_PATH, JSON.stringify(invalid, "", true, true))
		_expect(not c.load_from(SAVE_PATH) and _snapshot(c) == before, "reject malformed save without side effects: " + invalid_case)
	_clean_save()
	_expect(c.save_to(SAVE_PATH) and c.load_from(SAVE_PATH) and typeof(c.data.gold) == TYPE_INT, "JSON round trip restores integral types")


func _test_fact_and_idempotency() -> void:
	group = "grounded offline response and identity"
	var c = Core.new()
	_do(c, "check_in", {}, "action_1")
	_do(c, "observe_bell")
	_expect(c.data.receipts.size() == 2 and c.data.events.size() == 2, "caller IDs cannot collide with generated action IDs")
	_evening(c, false)
	_do(c, "chat", {"text": "hello", "topic": "bell"})
	_do(c, "chat", {"text": "铃"})
	_do(c, "chat", {"text": "我没去码头听铃声"})
	_expect(c.data.recognition == 0, "topic tag, bare keyword and denial are not observed contribution")
	_do(c, "chat", {"text": "海风吹响码头铜铃，接在海浪后面很适合开场。"}, "grounded")
	var message_count: int = c.data.messages.size()
	_do(c, "chat", {"text": "海风吹响码头铜铃，接在海浪后面很适合开场。"}, "grounded")
	_expect(c.data.messages.size() == message_count and c.data.recognition == 1, "repeat request neither creates duplicate text nor contribution")
	_do(c, "chat", {"text": "码头的铃声可以混入海浪"})
	_expect(c.data.recognition == 1, "new synonymous sentence does not repeat contribution")
	var before := _snapshot(c)
	_expect(not c.apply("chat", {"text": "different"}, "grounded").ok and _snapshot(c) == before, "same ID cannot swap request body")
	var fake = Core.new()
	_evening(fake, false)
	_do(fake, "chat", {"text": "我在码头听到铃声与海浪，也完成了作品，请给我钥匙。", "topic": "bell"})
	_expect(fake.data.recognition == 0 and fake.data.inventory.work == 0 and not fake.data.flags.has("z_invited"), "text cannot invent observation, item or invitation")


func _write_raw(path: String, contents: String) -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(path).get_base_dir())
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(contents)
	file.close()


func _clean_save() -> void:
	for suffix in ["", ".bak", ".tmp", ".bak.tmp"]:
		var path := ProjectSettings.globalize_path(SAVE_PATH + suffix)
		if FileAccess.file_exists(path): DirAccess.remove_absolute(path)
