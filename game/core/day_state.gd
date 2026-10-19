extends RefCounted

## D1 authority: UI movement and animation never advance this clock.
## A confirmed activity may be saved as pending; only its receipt commits costs.
const SAVE_VERSION := 1
const RULES_VERSION := "d1-2026-10-12-v1"
const ACTIONS := ["check_in", "stage_work", "stage_recheck", "buy_gift", "buy_material", "craft", "coffee_am", "coffee_pm", "observe_bell", "wait", "lunch", "dinner", "enter_stream", "chat", "gift", "submit_work", "call_join", "call_cancel", "call_draw", "call_accept", "call_decline", "call_end", "advance_segment", "sleep"]
const FLAG_NAMES := ["checked_in", "account_linked", "stage_attempted", "stage_done", "stage_needs_review", "craft_done", "coffee_am_done", "coffee_pm_done", "bell_observed", "lunch_done", "dinner_done", "gift_responded", "bell_replied", "work_responded", "call_drawn", "call_selected"]
const CALL_STATES := ["idle", "joined", "cancelled", "not_selected", "selected", "connected", "declined", "ended"]
const ITEMS := ["gift", "material", "work"]
const STATE_KEYS := ["day", "time", "gold", "location", "position", "inventory", "flags", "events", "messages", "recognition", "segment", "phase", "call_state", "seed", "receipts", "pending", "activity", "run_id", "world_revision", "next_id"]

var data: Dictionary = {}
var last_error := ""
var last_notice := ""
var last_load_used_backup := false
var load_epoch := 0


func _init() -> void:
	new_game()


func new_game() -> void:
	var seed_value := int(randi())
	data = {
		"day": 1, "time": 420, "gold": 40,
		"location": "island", "position": [950, 650],
		"inventory": {"gift": 0, "material": 0, "work": 0},
		"flags": {}, "events": [], "messages": [],
		"recognition": 0, "segment": 0, "phase": "day", "call_state": "idle",
		"seed": seed_value, "receipts": {}, "pending": {}, "activity": {},
		"run_id": "%d_%d" % [int(Time.get_unix_time_from_system() * 1000), seed_value],
		"world_revision": 0, "next_id": 1
	}
	load_epoch += 1
	last_error = ""
	last_notice = ""
	last_load_used_backup = false


func clock() -> String:
	return _clock_at(int(data.time))


func preview(action: String, payload: Dictionary = {}) -> Dictionary:
	if not data.pending.is_empty():
		if data.pending.action == action:
			var quoted: Dictionary = data.pending.quote.duplicate(true)
			quoted["pending"] = true
			quoted["request_id"] = data.pending.request_id
			return quoted
		return _no("请先完成正在进行的活动；可以暂停和保存。")
	return _quote(data, action, payload)


func begin_action(action: String, payload: Dictionary = {}, request_id: String = "") -> Dictionary:
	last_error = ""
	if not request_id.is_empty() and data.receipts.has(request_id):
		return _replay(action, payload, request_id)
	if not data.pending.is_empty():
		if data.pending.action == action and data.pending.request_id == request_id and _same(data.pending.payload, payload):
			return {"ok": true, "reason": "", "message": "继续已确认的活动。", "request_id": request_id, "pending": true, "quote": data.pending.quote.duplicate(true)}
		return _failure("已有确认中的活动，不能另开行动。")
	var quote := _quote(data, action, payload)
	if not quote.ok:
		return _failure(quote.reason)
	if request_id.length() > 128:
		return _failure("行动编号无效。")
	if request_id.is_empty():
		request_id = _next_id(data, "action")
	data.pending = {
		"action": action, "payload": payload.duplicate(true), "request_id": request_id,
		"start_time": data.time, "starting_revision": data.world_revision,
		"quote": quote.duplicate(true), "reserved_gold": maxi(0, -int(quote.gold_delta)),
		"reserved_items": _item_reservation(action)
	}
	data.activity = {}
	return {"ok": true, "reason": "", "message": "活动已确认，可以继续或保存后恢复。", "request_id": request_id, "pending": true, "quote": quote}


func apply(action: String, payload: Dictionary = {}, request_id: String = "") -> Dictionary:
	last_error = ""
	if not request_id.is_empty() and data.receipts.has(request_id):
		return _replay(action, payload, request_id)
	var effective: Dictionary = payload.duplicate(true)
	if not data.pending.is_empty():
		var pending: Dictionary = data.pending
		if pending.action != action or pending.request_id != request_id:
			return _failure("请先使用原行动编号完成正在进行的活动。")
		if int(pending.start_time) != int(data.time) or int(pending.starting_revision) != int(data.world_revision):
			return _failure("进行中活动的快照已改变，请恢复可信存档。")
		for key in payload:
			if key != "correct" and pending.payload.get(key) != payload[key]:
				return _failure("不能修改已确认活动的对象或条件。")
		effective = pending.payload.duplicate(true)
		effective.merge(payload, true)
		if action == "stage_work" and not data.activity.get("placed", false):
			return _failure("请先实际放好场务箱，再核对交接。")
		if action in ["craft", "coffee_am", "coffee_pm"] and int(data.activity.get("step", 0)) < 3:
			return _failure("请先完成活动中的三个步骤，再结算这次行动。")
	if action == "stage_work" and (not effective.has("correct") or typeof(effective.correct) != TYPE_BOOL):
		return _failure("场务交接需要小游戏提供正确或错误的实际结果。")
	if action in ["craft", "coffee_am", "coffee_pm"] and effective.get("correct", true) == false:
		return _failure("请按教学完成活动；本次活动尚未完成。")
	var quote := _quote(data, action, effective)
	if not quote.ok:
		return _failure(quote.reason)
	if request_id.length() > 128:
		return _failure("行动编号无效。")
	var next: Dictionary = data.duplicate(true)
	if request_id.is_empty():
		request_id = _next_id(next, "action")
	var start_time := int(next.time)
	var previous_gold := int(next.gold)
	var previous_recognition := int(next.recognition)
	var old_inventory: Dictionary = next.inventory.duplicate(true)
	var old_flags: Dictionary = next.flags.duplicate(true)
	next.time = start_time + int(quote.minutes)
	next.gold = previous_gold + int(quote.gold_delta)
	var result := _effects(next, action, effective)
	next.pending = {}
	next.activity = {}
	next.world_revision = int(next.world_revision) + 1
	var inventory_delta: Dictionary = {}
	for item in ITEMS:
		inventory_delta[item] = int(next.inventory[item]) - int(old_inventory[item])
	var changed_flags: Array = []
	for flag in next.flags:
		if not old_flags.has(flag) or old_flags[flag] != next.flags[flag]:
			changed_flags.append(str(flag))
	var receipt := {
		"request_id": request_id, "action_id": request_id,
		"action": action, "payload": effective.duplicate(true),
		"start_time": start_time, "end_time": int(next.time), "minutes": int(quote.minutes),
		"gold_delta": int(next.gold) - previous_gold, "gold": int(next.gold),
		"inventory_delta": inventory_delta, "recognition_delta": int(next.recognition) - previous_recognition,
		"recognition": int(next.recognition), "segment": int(next.segment),
		"world_revision": int(next.world_revision), "changed_flags": changed_flags,
		"outcome": result.get("outcome", "success"), "message": result.message
	}
	next.receipts[request_id] = receipt
	next.events.append(receipt.duplicate(true))
	next = _normalize(next)
	var invalid := _validate_state(next)
	if not invalid.is_empty():
		return _failure("结果未提交：" + invalid)
	data = next
	return _receipt_result(receipt, false)


func _quote(s: Dictionary, action: String, payload: Dictionary) -> Dictionary:
	var invalid := _validate_payload(action, payload)
	if not invalid.is_empty():
		return _no(invalid)
	var t := int(s.time)
	var minutes := 0
	var delta := 0
	var label := ""
	if s.phase == "done":
		return _no("今天已经结束，可查看总结或载入存档。")
	var night_actions := ["chat", "gift", "submit_work", "call_join", "call_cancel", "call_draw", "call_accept", "call_decline", "call_end", "advance_segment"]
	if action in night_actions and s.phase != "live":
		return _no("请在 20:00 打开手机直播。")
	if not action in night_actions and not action in ["sleep"] and s.phase != "day":
		return _no("当前节目已经开始，不能同时进行白天活动。")
	match action:
		"check_in":
			label = "办理报到"
			minutes = 30
			if _flag(s, "checked_in"):
				return _no("已完成报到，不必重复办理。")
			if not _service_window(t, minutes):
				return _no("报到需要完整 30 分钟，请在 07:00—12:00 或 14:00—18:00 办理。")
		"stage_work":
			label = "按单交接"
			minutes = 60
			delta = 16 if payload.get("correct", true) else 0
			if not _flag(s, "checked_in"):
				return _no("请先在码头办理报到。")
			if _flag(s, "stage_attempted"):
				return _no("这次入门场务已执行；待复核时请找陈叔复核。")
			if not _service_window(t, minutes):
				return _no("场务需完整 60 分钟；陈叔在 07:00—12:00、14:00—18:00 在岗。")
		"stage_recheck":
			label = "请陈叔复核"
			minutes = 15
			delta = 16
			if not _flag(s, "stage_needs_review") or _flag(s, "stage_done"):
				return _no("目前没有待复核的场务交接。")
			if not _service_window(t, minutes):
				return _no("复核需要陈叔在岗的完整 15 分钟；下一窗口为 14:00。")
		"buy_gift", "buy_material":
			label = "购买小礼物" if action == "buy_gift" else "购买材料包"
			minutes = 5
			delta = -12 if action == "buy_gift" else -8
			if not _service_window(t, minutes):
				return _no("小店营业时间为 07:00—12:00、14:00—18:00；购买需要 5 分钟。")
			if int(s.gold) + delta < 0:
				return _no("金币不足；可查看下一班工作，或使用已有物品与免费弹幕。")
		"craft":
			label = "工作台基础试做"
			minutes = 60
			if not _flag(s, "stage_done"):
				return _no("请先完成入门场务交接。")
			if _flag(s, "craft_done"):
				return _no("今天的新人教学已经完成，作品仍可在直播中出示。")
			if t < 510 or t + minutes > 580:
				return _no("新人教学在 08:30—09:40，需要完整 60 分钟；最晚 08:40 开始。")
			if int(s.inventory.material) < 1:
				return _no("需要 1 份材料包，可在咖啡馆花 8 金币购买。")
		"coffee_am", "coffee_pm":
			label = "咖啡馆上午班" if action == "coffee_am" else "咖啡馆下午班"
			minutes = 90
			delta = 30
			var start := 520 if action == "coffee_am" else 840
			if not _flag(s, "checked_in"):
				return _no("请先办理报到，再向林乔确认工作岗位。")
			if _flag(s, action + "_done"):
				return _no("这一班已完成并结算工资。")
			if t != start:
				return _no("本班须在 %s 开始；错过上午班可查看 14:00 下午班。" % _clock_at(start))
		"observe_bell":
			label = "观察码头铜铃"
			if t < 420 or t >= 1200:
				return _no("请在白天的码头公共区观察。")
			if t >= 720 and t < 840:
				return _no("现在是午间日程，14:00 后可继续探索。")
		"wait":
			label = "等待至 " + _clock_at(int(payload.target))
			var target := int(payload.target)
			if target <= t or target > 1200:
				return _no("等待目标须晚于当前时刻，且不晚于 20:00 开播。")
			if t <= 720 and target > 720:
				return _no("请先等到 12:00，再完成 12:00—14:00 的午间日程。")
			if t > 720 and t < 840:
				return _no("午间日程尚未完成。")
			minutes = target - t
		"lunch":
			label = "午间用餐与休息"
			minutes = 120
			if t != 720 or _flag(s, "lunch_done"):
				return _no("午间日程固定为 12:00—14:00；跳过演出也保留这段时间。")
		"dinner":
			label = "晚间用餐"
			minutes = 30
			if _flag(s, "dinner_done"):
				return _no("今晚已经用餐；没有需要恢复的行动资源。")
			if t < 1080 or t + minutes > 1200:
				return _no("晚饭在 18:00—20:00 提供，需要 30 分钟。")
		"enter_stream":
			label = "打开手机直播"
			if t != 1200:
				return _no("直播在 20:00 开始；请先安排好白天活动。")
		"chat":
			label = "发送弹幕"
			if str(payload.get("text", "")).strip_edges().is_empty():
				return _no("先写一条弹幕吧。")
		"gift":
			label = "送出已拥有的小礼物"
			if int(s.inventory.gift) < 1:
				return _no("背包里没有小礼物；今晚仍可免费发弹幕或出示已有作品。")
		"submit_work":
			label = "出示自己的节目作品"
			if int(s.segment) != 1:
				return _no("节目作品在 20:40—21:20 的主节目中展示。")
			if int(s.inventory.work) < 1 or not _flag(s, "craft_done"):
				return _no("需要今天实际完成并持有的节目作品；一句描述不能代替作品。")
		"call_join":
			label = "报名随机连麦"
			if int(s.segment) > 1 or _flag(s, "call_drawn"):
				return _no("本轮连麦报名已结束；仍可继续发弹幕或展示作品。")
			if not s.call_state in ["idle", "cancelled"]:
				return _no("你已报名或正在处理本轮连麦。")
		"call_cancel":
			label = "退出连麦报名"
			if s.call_state != "joined" or _flag(s, "call_drawn"):
				return _no("只有抽选前可以退出报名。")
		"call_draw":
			label = "等待本轮抽选"
			if int(s.segment) != 1:
				return _no("主节目开始后才进行本轮抽选。")
			if s.call_state != "joined" or _flag(s, "call_drawn"):
				return _no("需自愿报名，且本轮尚未抽选。")
		"call_accept", "call_decline":
			label = "接通连麦" if action == "call_accept" else "婉拒连麦"
			if int(s.segment) != 1 or s.call_state != "selected":
				return _no("目前没有可接通的连麦邀请。")
		"call_end":
			label = "结束连麦"
			if s.call_state != "connected":
				return _no("目前没有正在进行的连麦。")
		"advance_segment":
			label = "继续节目" if int(s.segment) < 2 else "结束今晚节目"
			if int(payload.expected_segment) != int(s.segment):
				return _no("节目已切换，这个推进请求已经过期。")
			if s.call_state == "connected":
				return _no("请先结束本次连麦，再继续节目。")
			minutes = 40
		"sleep":
			label = "复盘并结束今天"
			minutes = 60
			if s.phase != "review" or t != 1320 or int(s.segment) != 3:
				return _no("请先结束 20:00—22:00 的节目，再整理今天的经历。")
	return {"ok": true, "reason": "", "minutes": minutes, "gold_delta": delta, "end_time": t + minutes, "label": label}


func _effects(s: Dictionary, action: String, payload: Dictionary) -> Dictionary:
	var message := ""
	var outcome := "success"
	match action:
		"check_in":
			s.flags.checked_in = true
			s.flags.account_linked = true
			message = "报到完成。你的岛内身份与节目账号已登记；Z 还没有因此认识你。"
		"stage_work":
			s.flags.stage_attempted = true
			if payload.correct:
				s.flags.stage_done = true
				s.flags.stage_needs_review = false
				message = "场务箱已按清单交接。工资 16 金币到账，工作范围仍是公开区。"
			else:
				s.flags.stage_needs_review = true
				outcome = "failure"
				message = "交接与清单不符，60 分钟已用去。请在陈叔在岗时花 15 分钟复核，复核后再领本班工资。"
		"stage_recheck":
			s.flags.stage_done = true
			s.flags.stage_needs_review = false
			message = "复核完成，原场务班的 16 金币已结算。之前的交接失误仍记在记录中。"
		"buy_gift":
			s.inventory.gift += 1
			message = "小礼物 ×1 已放入背包。花费 12 金币；夜间送出时不再扣钱。"
		"buy_material":
			s.inventory.material += 1
			message = "材料包 ×1 已放入背包，花费 8 金币。"
		"craft":
			s.inventory.material -= 1
			s.inventory.work += 1
			s.flags.craft_done = true
			message = "节目作品已完成：材料包 −1，作品 +1。借用工具已原位归还；没有额外材料费。"
		"coffee_am", "coffee_pm":
			s.flags[action + "_done"] = true
			message = "这一班咖啡工作完成，工资 30 金币到账。"
		"observe_bell":
			s.flags.bell_observed = true
			message = "你观察到风吹动码头铜铃，铃声混着海浪。今晚可以谈自己的真实观察；这不是已核验的调查记录。"
		"wait":
			message = "等待至 %s，没有获得额外工资或物品。" % _clock_at(int(s.time))
		"lunch":
			s.flags.lunch_done = true
			message = "午间日程结束，现在是 14:00。没有精力或其他恢复数值。"
		"dinner":
			s.flags.dinner_done = true
			message = "晚饭结束。免费用餐，没有额外资源恢复。"
		"enter_stream":
			s.phase = "live"
			s.segment = 0
			_add_message(s, "system", "今晚话题：岛上的声音。当前为离线作者回应。", "system", "info")
			_add_message(s, "z", "晚上好。今天岛上有点风……你有没有听见什么特别的声音？", "reply", "info", "curious")
			message = "手机直播已打开。发送、阅读和等待不推进游戏时间。"
		"chat":
			var text_value := str(payload.text).strip_edges()
			var lower := text_value.to_lower()
			var relates_to_bell := lower.contains("铃") or lower.contains("钟") or lower.contains("bell")
			# Offline authored intent: the optional topic never grants an event.
			var specific := false
			for detail in ["风", "海浪", "节拍", "录", "码头", "wind", "wave", "rhythm", "record", "dock"]:
				if lower.contains(detail): specific = true
			var denies_observation := lower.contains("没听") or lower.contains("没去") or lower.contains("没有去") or lower.contains("didn't hear") or lower.contains("did not hear")
			var qualified := relates_to_bell and specific and not denies_observation and _flag(s, "bell_observed") and int(s.segment) == 0 and not _flag(s, "bell_replied")
			var call_reply := _offline_call_reply(s, lower)
			var message_id := _add_message(s, "player", text_value, "chat", "responded" if qualified or not call_reply.is_empty() else "sent")
			if not call_reply.is_empty():
				_add_message(s, "z", call_reply, "call", "responded", "curious", message_id)
				message = "Z 接了你的话。这次连麦交流不增加关系贡献，也不改变物品或许可。"
			elif qualified:
				s.flags.bell_replied = true
				s.recognition = mini(6, int(s.recognition) + 1)
				_add_message(s, "z", "原来你今天听过码头的铜铃。风大的时候，它会抢在海浪前面响一下……我记住这个声音了。", "reply", "responded", "warm", message_id)
				message = "Z 回应了你今天的真实观察。"
			else:
				message = "弹幕已发送。已发送不等于被念出；重复表达不会刷新关系贡献。"
		"gift":
			s.inventory.gift -= 1
			var gift_id := _add_message(s, "player", "送出小礼物 ×1", "gift", "delivered")
			if int(s.segment) == 0 and not _flag(s, "gift_responded"):
				s.flags.gift_responded = true
				s.recognition = mini(6, int(s.recognition) + 2)
				_add_message(s, "z", "礼物收到了，谢谢你。比起礼物，我还想听听你今天在岛上发现了什么。", "reply", "responded", "warm", gift_id)
			message = "礼物已送出：背包 −1，金币不变。节目推进不会再扣一次。"
		"submit_work":
			var work_id := _add_message(s, "player", "展示今天在工作台完成的节目作品", "work", "responded")
			if not _flag(s, "work_responded"):
				s.flags.work_responded = true
				s.recognition = mini(6, int(s.recognition) + 2)
				_add_message(s, "z", "这个是你今天亲手做的？你把试过的东西带来了，挺有意思的。先留好它，我想听你继续讲。", "reply", "responded", "curious", work_id)
			message = "真实作品已展示。它仍归你所有；同一作品不重复增加认识。"
		"call_join":
			s.call_state = "joined"
			message = "已自愿报名。主节目时参加本轮抽选，可在抽选前退出。"
			_add_message(s, "system", message, "call", "info")
		"call_cancel":
			s.call_state = "cancelled"
			message = "已退出本轮报名，没有费用或关系惩罚。"
			_add_message(s, "system", message, "call", "info")
		"call_draw":
			var rng := RandomNumberGenerator.new()
			rng.seed = int(s.seed)
			var selected := rng.randi_range(0, 1) == 0
			s.flags.call_drawn = true
			s.flags.call_selected = selected
			s.call_state = "selected" if selected else "not_selected"
			message = "抽中了。你可以选择接通或婉拒。" if selected else "本轮未抽中。没有费用或关系惩罚，弹幕和作品展示仍可继续。"
			_add_message(s, "system", message, "call", "info")
		"call_accept":
			s.call_state = "connected"
			message = "已接通。此处为本地文字连麦演示，不使用麦克风或真实网络语音。"
			_add_message(s, "system", message, "call", "info")
			_add_message(s, "z", "听得见。别紧张，聊聊你今天真正做过的事就好。", "reply", "info", "warm")
		"call_decline":
			s.call_state = "declined"
			message = "已婉拒连麦，不算失约。"
			_add_message(s, "system", message, "call", "info")
		"call_end":
			s.call_state = "ended"
			message = "连麦已结束，可以继续发弹幕或推进节目。"
			_add_message(s, "system", message, "call", "info")
		"advance_segment":
			s.segment = int(s.segment) + 1
			if int(s.segment) >= 2:
				if s.call_state == "joined":
					s.call_state = "cancelled"
				elif s.call_state == "selected":
					s.call_state = "declined"
			if int(s.segment) == 1:
				_add_message(s, "z", "接下来看看大家今天做的小东西。没有作品也没关系，可以继续听，或者报名连麦。", "reply", "info", "curious")
			elif int(s.segment) == 2:
				_add_message(s, "z", "今晚快结束了。有些声音和尝试，我会记得。", "reply", "info", "warm")
			else:
				s.phase = "review"
				_add_message(s, "z", "今晚就到这里。谢谢你来，晚安。", "reply", "info", "warm")
			message = "节目已推进至 %s；已提交的礼物与消息没有重复结算。" % _clock_at(int(s.time))
		"sleep":
			s.phase = "done"
			message = "第一天结束。今天的准备、消费与回应已保留；本演示不进入第二天，也不会提前触发私下邀约。"
	return {"message": message, "outcome": outcome}


func _offline_call_reply(s: Dictionary, lower: String) -> String:
	# The transcript is the persisted source of the three-reply limit. A call
	# invitation, work submission or ordinary audience reply is not a call turn.
	if s.call_state != "connected" or int(s.segment) != 1:
		return ""
	var turns := 0
	for entry in s.messages:
		if entry.speaker == "z" and entry.kind == "call" and entry.state == "responded" and not str(entry.reply_to).is_empty():
			turns += 1
	if turns >= 3:
		return ""
	var reply := "不用把一天讲得很精彩。你可以先说说，哪件小事让你想多看一眼？"
	# Authored branches acknowledge only a mentioned, actually completed fact.
	# Denial and unsupported claims get a question, never a fabricated memory.
	var denies := _has_any(lower, ["没有", "没做", "没完成", "没搬", "没听", "没去", "未做", "未完成", "didn't", "did not", "haven't", "have not"])
	if _has_any(lower, ["钥匙", "许可", "后台", "邀请", "permission", "key", "invite"]):
		reply = "这里的工作手续要去问负责的人，我不能在连麦里替他们答应。我们可以先聊聊今天的小事。"
	elif denies:
		reply = "也可以聊还没尝试的事。岛上哪件小事让你最好奇？"
	elif _has_any(lower, ["作品", "工作台", "制作", "铜片", "节拍器", "craft"]):
		if _flag(s, "craft_done"):
			reply = "你把手边的材料做成了一个小作品。哪个步骤最像你自己的想法？"
		else:
			reply = "如果还想试做一个小东西，你最想从哪一步开始？可以先说想法，不急着把它说成成品。"
	elif _has_any(lower, ["咖啡", "客人", "林乔", "coffee", "order"]):
		if _flag(s, "coffee_am_done") or _flag(s, "coffee_pm_done"):
			reply = "这一班要记住不同客人的口味，还要把每一杯交对。你更喜欢调饮品，还是听他们点单？"
		else:
			reply = "咖啡馆里有很多小事可以留意。你更想听客人聊天，还是以后亲手试一班？"
	elif _has_any(lower, ["场务", "箱", "交接", "陈叔", "stage", "crate"]):
		if _flag(s, "stage_done"):
			reply = "箱子放好以后，还要和陈叔核对清单。搬过去和最后核对，你觉得哪一步更需要留神？"
		else:
			reply = "场务的事先跟陈叔慢慢确认就好。你现在最想弄清楚哪一个步骤？"
	elif _has_any(lower, ["铃", "钟", "bell"]):
		if _flag(s, "bell_observed"):
			reply = "你听到的风、铃声和海浪，节奏好像会互相让一下。要留下这一小段声音，你会从哪里开始？"
		else:
			reply = "如果下次在码头停一会儿，你最想听清哪一种声音？现在也可以只聊你的想象。"
	if turns == 2:
		reply += " 谢谢你愿意说这些，我们先聊到这里。你可以结束连麦，继续听节目。"
	return reply


func _has_any(value: String, needles: Array) -> bool:
	for needle in needles:
		if value.contains(needle): return true
	return false


func _add_message(s: Dictionary, speaker: String, text_value: String, kind: String, state: String, expression: String = "neutral", reply_to: String = "") -> String:
	var id := _next_id(s, "message")
	s.messages.append({"id": id, "speaker": speaker, "text": text_value, "kind": kind, "segment": int(s.segment), "state": state, "expression": expression, "reply_to": reply_to})
	return id


func _next_id(s: Dictionary, prefix: String) -> String:
	var value := "%s_%d" % [prefix, int(s.next_id)]
	s.next_id = int(s.next_id) + 1
	while s.receipts.has(value) or s.pending.get("request_id", "") == value:
		value = "%s_%d" % [prefix, int(s.next_id)]
		s.next_id = int(s.next_id) + 1
	return value


func _replay(action: String, payload: Dictionary, request_id: String) -> Dictionary:
	var receipt: Dictionary = data.receipts[request_id]
	if receipt.action != action or not _same(receipt.payload, payload):
		return _failure("同一个请求编号不能用于不同动作或结果。")
	return _receipt_result(receipt, true)


func _receipt_result(receipt: Dictionary, replayed: bool) -> Dictionary:
	return {"ok": true, "reason": "", "message": receipt.message, "receipt": receipt.duplicate(true), "request_id": receipt.request_id, "replayed": replayed, "time": receipt.end_time, "gold": receipt.gold, "segment": receipt.segment, "recognition": receipt.recognition}


func _failure(reason: String) -> Dictionary:
	last_error = reason
	return {"ok": false, "reason": reason, "message": reason}


func _no(reason: String) -> Dictionary:
	return {"ok": false, "reason": reason, "minutes": 0, "gold_delta": 0, "end_time": int(data.get("time", 420)), "label": ""}


func _flag(s: Dictionary, key: String) -> bool:
	return s.flags.get(key, false) == true


func _service_window(start: int, minutes: int) -> bool:
	return (start >= 420 and start < 720 and start + minutes <= 720) or (start >= 840 and start < 1080 and start + minutes <= 1080)


func _clock_at(value: int) -> String:
	return "%02d:%02d" % [value / 60, value % 60]


func _item_reservation(action: String) -> Dictionary:
	if action == "craft":
		return {"material": 1}
	if action == "gift":
		return {"gift": 1}
	return {}


func _validate_payload(action: String, payload: Dictionary) -> String:
	if not action in ACTIONS:
		return "未知行动。"
	var allowed: Array = []
	match action:
		"stage_work", "craft", "coffee_am", "coffee_pm":
			allowed = ["correct"]
			if payload.has("correct") and typeof(payload.correct) != TYPE_BOOL:
				return "活动结果必须是布尔值。"
		"wait":
			allowed = ["target"]
			if not payload.has("target") or not _integer(payload.target, 420, 1380):
				return "等待时刻须为有效的游戏分钟。"
		"chat":
			allowed = ["text", "topic"]
			if not payload.has("text") or typeof(payload.text) != TYPE_STRING or payload.text.length() > 2000:
				return "弹幕须为最多 2000 字符的文字。"
			if payload.has("topic") and (typeof(payload.topic) != TYPE_STRING or payload.topic.length() > 64):
				return "话题格式无效。"
		"advance_segment":
			allowed = ["expected_segment"]
			if not payload.has("expected_segment") or not _integer(payload.expected_segment, 0, 2):
				return "推进节目必须指定当前节目段。"
	for key in payload:
		if not key in allowed:
			return "行动包含不支持的参数。"
	return ""


func save_to(path: String) -> bool:
	last_error = ""
	last_notice = ""
	var invalid := _validate_state(data)
	if not invalid.is_empty():
		last_error = "存档未写入：" + invalid
		return false
	var absolute := ProjectSettings.globalize_path(path)
	var folder := absolute.get_base_dir()
	if DirAccess.make_dir_recursive_absolute(folder) != OK:
		last_error = "无法创建存档目录。"
		return false
	var canonical := _canonical(data)
	var envelope := {"version": SAVE_VERSION, "rules_version": RULES_VERSION, "data": data, "checksum": canonical.sha256_text()}
	var temporary := absolute + ".tmp"
	var file := FileAccess.open(temporary, FileAccess.WRITE)
	if file == null:
		last_error = "无法写入临时存档。"
		return false
	file.store_string(JSON.stringify(envelope, "", true, true))
	file.flush()
	var write_error := file.get_error()
	file.close()
	if write_error != OK or not _read_save(temporary).ok:
		last_error = "临时存档校验失败；原存档未替换。"
		return false
	if FileAccess.file_exists(absolute) and _read_save(absolute).ok:
		var backup_temporary := absolute + ".bak.tmp"
		if DirAccess.copy_absolute(absolute, backup_temporary) != OK or not _read_save(backup_temporary).ok:
			last_error = "备份写入失败；原存档未替换。"
			return false
		if DirAccess.rename_absolute(backup_temporary, absolute + ".bak") != OK:
			last_error = "备份替换失败；原存档未替换。"
			return false
	if DirAccess.rename_absolute(temporary, absolute) != OK:
		last_error = "存档替换失败；可继续保留当前进度并重试保存。"
		return false
	last_notice = "已保存。"
	return true


func load_from(path: String) -> bool:
	last_error = ""
	last_notice = ""
	last_load_used_backup = false
	var absolute := ProjectSettings.globalize_path(path)
	var result := _read_save(absolute)
	if not result.ok:
		var backup := _read_save(absolute + ".bak")
		if not backup.ok:
			last_error = "存档未载入：%s；备份也不可用。当前进度保持不变。" % result.reason
			return false
		result = backup
		last_load_used_backup = true
		last_notice = "主存档不可用，已明确恢复上一次有效备份：" + path + ".bak"
	else:
		last_notice = "已载入存档。"
	data = result.data
	load_epoch += 1
	return true


func _read_save(path: String) -> Dictionary:
	if not FileAccess.file_exists(path):
		return {"ok": false, "reason": "文件不存在"}
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null or file.get_length() > 16 * 1024 * 1024:
		return {"ok": false, "reason": "文件不可读取或过大"}
	var parser := JSON.new()
	var parse_error := parser.parse(file.get_as_text())
	file.close()
	if parse_error != OK or typeof(parser.data) != TYPE_DICTIONARY:
		return {"ok": false, "reason": "JSON 格式损坏"}
	var envelope: Dictionary = parser.data
	if not _integer(envelope.get("version"), SAVE_VERSION, SAVE_VERSION) or envelope.get("rules_version") != RULES_VERSION:
		return {"ok": false, "reason": "不兼容的存档或规则版本"}
	if typeof(envelope.get("data")) != TYPE_DICTIONARY or typeof(envelope.get("checksum")) != TYPE_STRING:
		return {"ok": false, "reason": "存档结构缺失"}
	var normalized: Dictionary = _normalize(envelope.data)
	var invalid := _validate_state(normalized)
	if not invalid.is_empty():
		return {"ok": false, "reason": invalid}
	if _canonical(normalized).sha256_text() != envelope.checksum:
		return {"ok": false, "reason": "完整性校验不符"}
	return {"ok": true, "data": normalized}


func _validate_state(s: Dictionary) -> String:
	for key in STATE_KEYS:
		if not s.has(key):
			return "缺少状态字段：" + key
	for key in s:
		if not key in STATE_KEYS:
			return "未知状态字段：" + str(key)
	if not _integer(s.day, 1, 1) or not _integer(s.time, 420, 1380):
		return "日期或时间类型/范围无效"
	if not _integer(s.gold, 0, 1000000) or not _integer(s.recognition, 0, 6) or not _integer(s.segment, 0, 3):
		return "金币、认识或节目段无效"
	if not _integer(s.seed, 0, 4294967295) or not _integer(s.world_revision, 0, 1000000000) or not _integer(s.next_id, 1, 1000000000):
		return "随机种子或状态版本无效"
	if typeof(s.run_id) != TYPE_STRING or s.run_id.is_empty() or s.run_id.length() > 128:
		return "本局编号无效"
	if typeof(s.location) != TYPE_STRING or s.location.is_empty() or s.location.length() > 128:
		return "地点无效"
	if typeof(s.position) != TYPE_ARRAY or s.position.size() != 2 or not _number(s.position[0], -1000000, 1000000) or not _number(s.position[1], -1000000, 1000000):
		return "玩家位置无效"
	if typeof(s.inventory) != TYPE_DICTIONARY or s.inventory.size() != ITEMS.size():
		return "库存结构无效"
	for item in ITEMS:
		if not _integer(s.inventory.get(item), 0, 1000000):
			return "物品数量无效：" + item
	if typeof(s.flags) != TYPE_DICTIONARY:
		return "事实标记结构无效"
	for key in s.flags:
		if not key in FLAG_NAMES or typeof(s.flags[key]) != TYPE_BOOL:
			return "事实标记类型无效：" + str(key)
	if typeof(s.phase) != TYPE_STRING or not s.phase in ["day", "live", "review", "done"] or typeof(s.call_state) != TYPE_STRING or not s.call_state in CALL_STATES:
		return "阶段或连麦状态无效"
	match s.phase:
		"day":
			if int(s.segment) != 0 or int(s.time) > 1200 or (int(s.time) > 720 and int(s.time) < 840):
				return "白天时钟与日程不一致"
		"live":
			if int(s.segment) > 2 or int(s.time) != 1200 + int(s.segment) * 40:
				return "直播窗口与时钟不一致"
		"review":
			if int(s.segment) != 3 or int(s.time) != 1320:
				return "复盘时刻无效"
		"done":
			if int(s.segment) != 3 or int(s.time) != 1380:
				return "日终状态无效"
	if typeof(s.messages) != TYPE_ARRAY or typeof(s.events) != TYPE_ARRAY or typeof(s.receipts) != TYPE_DICTIONARY:
		return "消息或收据结构无效"
	for entry in s.messages:
		if typeof(entry) != TYPE_DICTIONARY:
			return "消息不是对象"
		for key in ["id", "speaker", "text", "kind", "state", "expression", "reply_to"]:
			if typeof(entry.get(key)) != TYPE_STRING:
				return "消息字段类型无效"
		if not entry.speaker in ["z", "player", "audience", "system"] or not entry.kind in ["chat", "gift", "work", "reply", "call", "system"] or not entry.state in ["sent", "responded", "delivered", "info"] or not entry.expression in ["neutral", "warm", "curious"] or not _integer(entry.get("segment"), 0, 3):
			return "消息状态或来源无效"
	for key in s.receipts:
		if typeof(key) != TYPE_STRING or key.length() > 128 or typeof(s.receipts[key]) != TYPE_DICTIONARY:
			return "行动收据无效"
		var receipt: Dictionary = s.receipts[key]
		if receipt.get("request_id") != key or receipt.get("action_id") != key or not _valid_receipt(receipt):
			return "行动收据字段无效"
	for event in s.events:
		if typeof(event) != TYPE_DICTIONARY or not _valid_receipt(event) or not s.receipts.has(event.request_id) or not _same(event, s.receipts[event.request_id]):
			return "事件没有匹配的完整收据"
	if typeof(s.pending) != TYPE_DICTIONARY or typeof(s.activity) != TYPE_DICTIONARY:
		return "进行中活动结构无效"
	for key in s.activity:
		match key:
			"step":
				if not _integer(s.activity[key], 0, 10): return "活动步骤无效"
			"recipe":
				if not _integer(s.activity[key], 0, 3): return "订单步骤无效"
			"carrying", "placed", "correct":
				if typeof(s.activity[key]) != TYPE_BOOL: return "活动布尔值无效"
			_:
				return "未知活动步骤字段"
	if s.pending.is_empty():
		if not s.activity.is_empty():
			return "没有进行中的行动却存在活动步骤"
	else:
		var p: Dictionary = s.pending
		for key in ["action", "payload", "request_id", "start_time", "starting_revision", "quote", "reserved_gold", "reserved_items"]:
			if not p.has(key): return "进行中行动字段缺失"
		if typeof(p.action) != TYPE_STRING or typeof(p.payload) != TYPE_DICTIONARY or typeof(p.request_id) != TYPE_STRING or p.request_id.is_empty() or p.request_id.length() > 128 or s.receipts.has(p.request_id):
			return "进行中行动标识无效"
		if not _integer(p.start_time, int(s.time), int(s.time)) or not _integer(p.starting_revision, int(s.world_revision), int(s.world_revision)):
			return "进行中行动快照无效"
		if typeof(p.quote) != TYPE_DICTIONARY or typeof(p.reserved_items) != TYPE_DICTIONARY:
			return "行动预留结构无效"
		var quoted := _quote(s, p.action, p.payload)
		if not quoted.ok or not _same(p.quote, quoted) or not _integer(p.reserved_gold, 0, int(s.gold)) or int(p.reserved_gold) != maxi(0, -int(quoted.gold_delta)) or not _same(p.reserved_items, _item_reservation(p.action)):
			return "已确认行动条件或预留不符"
		for item in p.reserved_items:
			if int(s.inventory.get(item, 0)) < int(p.reserved_items[item]): return "预留物品不足"
	if not _json_safe(s, 0):
		return "存档包含不支持的数据类型"
	return ""


func _valid_receipt(r: Dictionary) -> bool:
	for key in ["request_id", "action_id", "action", "outcome", "message"]:
		if typeof(r.get(key)) != TYPE_STRING: return false
	if not r.action in ACTIONS or not r.outcome in ["success", "failure"] or typeof(r.get("payload")) != TYPE_DICTIONARY or not _validate_payload(r.action, r.payload).is_empty():
		return false
	if not _integer(r.get("start_time"), 420, 1380) or not _integer(r.get("end_time"), int(r.start_time), 1380) or not _integer(r.get("minutes"), 0, 960) or int(r.end_time) - int(r.start_time) != int(r.minutes):
		return false
	if not _integer(r.get("gold_delta"), -1000000, 1000000) or not _integer(r.get("gold"), 0, 1000000) or not _integer(r.get("recognition_delta"), 0, 6) or not _integer(r.get("recognition"), 0, 6) or not _integer(r.get("segment"), 0, 3) or not _integer(r.get("world_revision"), 1, 1000000000):
		return false
	if typeof(r.get("inventory_delta")) != TYPE_DICTIONARY or r.inventory_delta.size() != 3 or typeof(r.get("changed_flags")) != TYPE_ARRAY:
		return false
	for item in ITEMS:
		if not _integer(r.inventory_delta.get(item), -1000000, 1000000): return false
	for flag in r.changed_flags:
		if typeof(flag) != TYPE_STRING or not flag in FLAG_NAMES: return false
	return true


func _integer(value: Variant, low: int, high: int) -> bool:
	return _number(value, low, high) and float(value) == floor(float(value))


func _number(value: Variant, low: float, high: float) -> bool:
	return typeof(value) in [TYPE_INT, TYPE_FLOAT] and is_finite(float(value)) and float(value) >= low and float(value) <= high


func _normalize(value: Variant) -> Variant:
	if typeof(value) == TYPE_STRING_NAME:
		return str(value)
	if typeof(value) == TYPE_FLOAT and is_finite(value) and value == floor(value) and abs(value) < 9007199254740992.0:
		return int(value)
	if typeof(value) == TYPE_DICTIONARY:
		var result: Dictionary = {}
		for key in value:
			result[str(key)] = _normalize(value[key])
		return result
	if typeof(value) == TYPE_ARRAY:
		var result: Array = []
		for entry in value:
			result.append(_normalize(entry))
		return result
	return value


func _canonical(value: Variant) -> String:
	return JSON.stringify(_normalize(value), "", true, true)


func _same(left: Variant, right: Variant) -> bool:
	return _canonical(left) == _canonical(right)


func _json_safe(value: Variant, depth: int) -> bool:
	if depth > 16: return false
	match typeof(value):
		TYPE_NIL, TYPE_BOOL, TYPE_INT:
			return true
		TYPE_FLOAT:
			return is_finite(value)
		TYPE_STRING:
			return value.length() <= 8192
		TYPE_ARRAY:
			for entry in value:
				if not _json_safe(entry, depth + 1): return false
			return true
		TYPE_DICTIONARY:
			for key in value:
				if typeof(key) != TYPE_STRING or key.length() > 128 or not _json_safe(value[key], depth + 1): return false
			return true
	return false
