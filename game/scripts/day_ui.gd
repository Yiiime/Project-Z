extends RefCounted
## Daytime C.02 presentation. Actions still pass through main and DayState.
const Kit = preload("res://scripts/ui_kit.gd")
var app: Node
var choice_buttons: Array[Button] = []

func _init(owner: Node) -> void:
	app=owner

func text(value: String, size: int=20, color: Color=Kit.WHITE) -> Label:
	return app.label(value,size,color)

func single(value: String, size: int=20, color: Color=Kit.WHITE) -> Label:
	var l:=text(value,size,color); l.autowrap_mode=TextServer.AUTOWRAP_OFF
	return l

func column(separation: int=16) -> VBoxContainer:
	var v:=VBoxContainer.new(); v.add_theme_constant_override("separation",separation)
	v.size_flags_horizontal=Control.SIZE_EXPAND_FILL
	return v

func row(separation: int=16) -> HBoxContainer:
	var h:=HBoxContainer.new(); h.add_theme_constant_override("separation",separation)
	return h

func small_icon(name: String, size: float=22, color: Color=Kit.MUTED) -> TextureRect:
	var p:=Kit.picture(Kit.icon(name),Vector2(size,size)); p.modulate=color
	return p

func info_row(parent: VBoxContainer, name: String, value: String, symbol: String="clock", positive: bool=false) -> void:
	var h:=row(12); parent.add_child(h)
	h.add_child(small_icon(symbol,19))
	var l:=text(name,17,Kit.MUTED); l.size_flags_horizontal=Control.SIZE_EXPAND_FILL; h.add_child(l)
	var n:=single(value,20,Color("c9e1d4") if positive else Kit.WHITE)
	n.add_theme_font_override("font",Kit.font(500,true)); n.horizontal_alignment=HORIZONTAL_ALIGNMENT_RIGHT; h.add_child(n)

func footer(parent: VBoxContainer, value: String, symbol: String="moon") -> void:
	parent.add_child(Kit.spacer(8)); parent.add_child(Kit.divider())
	var h:=row(12); parent.add_child(h); h.add_child(small_icon(symbol,19))
	var caption:=single(value,16,Kit.MUTED); caption.size_flags_horizontal=Control.SIZE_EXPAND_FILL; h.add_child(caption)

func tabs(parent: VBoxContainer, entries: Array, active: String) -> void:
	var h:=row(10); parent.add_child(h)
	for entry in entries:
		var b: Button=app.button(entry[0],entry[1]); b.custom_minimum_size=Vector2(84,42)
		b.add_theme_font_size_override("font_size",17); b.alignment=HORIZONTAL_ALIGNMENT_CENTER
		Kit.quiet(b)
		if entry[0]==active:
			var s:=Kit.style(Color.TRANSPARENT,Color.TRANSPARENT,0)
			s.border_color=Kit.PALE; s.border_width_bottom=2
			b.add_theme_stylebox_override("normal",s)
		h.add_child(b)
	parent.add_child(Kit.divider())

func dialogue(name: String, role: String, speech: String, portrait_id: String) -> VBoxContainer:
	app.clear_modal(); app.modal.show(); app.modal_kind="dialogue"; choice_buttons.clear()
	app.modal.add_child(Kit.fade(Color.TRANSPARENT,Color(0.025,0.055,0.09,0.92),Rect2(0,290,1600,610)))
	var surface:=PanelContainer.new(); surface.name="DialogueSurface"
	surface.position=Vector2(210,405); surface.size=Vector2(1338,410)
	var s:=Kit.style(Color(0.063,0.11,0.175,0.98),Kit.LINE,8)
	s.content_margin_left=326; s.content_margin_right=32; s.content_margin_top=25; s.content_margin_bottom=24
	surface.add_theme_stylebox_override("panel",s); app.modal.add_child(surface)
	var scroll:=ScrollContainer.new(); scroll.horizontal_scroll_mode=ScrollContainer.SCROLL_MODE_DISABLED; surface.add_child(scroll)
	var v:=column(7); scroll.add_child(v)
	var h:=row(16); v.add_child(h)
	var name_label:=single(name,27); name_label.add_theme_font_override("font",Kit.font(550)); h.add_child(name_label)
	var identity:=single(role,16,Kit.MUTED); identity.size_flags_horizontal=Control.SIZE_EXPAND_FILL; identity.vertical_alignment=VERTICAL_ALIGNMENT_CENTER; h.add_child(identity)
	var close: Button=app.button("",app.close_modal); close.icon=Kit.icon("close"); close.custom_minimum_size=Vector2(36,36); close.tooltip_text="关闭对话 · Esc"; Kit.quiet(close); h.add_child(close); Kit.compact(close)
	var body:=text(speech,21); body.add_theme_constant_override("line_spacing",5); v.add_child(body)
	v.add_child(Kit.spacer(5))
	var path: String="res://assets/chen.png" if portrait_id=="chen" else "res://assets/ui/portraits/%s.png"%portrait_id
	if ResourceLoader.exists(path):
		var portrait:=Kit.picture(load(path),Vector2.ZERO); portrait.name="DialoguePortrait"
		portrait.position=Vector2(100,356); portrait.size=Vector2(459,459)
		app.modal.add_child(portrait)
	app.modal_footer("Esc  返回","交谈与阅读不推进时间")
	return v

func choice(v: VBoxContainer, caption: String, callback: Callable, available: bool=true, reason: String="") -> Button:
	var b: Button=app.button(caption,callback); b.disabled=not available; b.custom_minimum_size.y=43
	b.add_theme_font_size_override("font_size",18); Kit.quiet(b)
	var s:=Kit.style(Color.TRANSPARENT,Color.TRANSPARENT,4); s.content_margin_left=48; s.content_margin_top=7; s.content_margin_bottom=7
	b.add_theme_stylebox_override("normal",s)
	for state in ["hover","pressed","disabled","focus"]:
		var base: StyleBoxFlat=app.theme_ui.get_stylebox(state,"Button").duplicate()
		base.content_margin_left=48; base.content_margin_top=7; base.content_margin_bottom=7; b.add_theme_stylebox_override(state,base)
	choice_buttons.append(b)
	var number:=single(str(choice_buttons.size()),12,Kit.MUTED)
	number.position=Vector2(15,12); number.size=Vector2(20,20); number.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER
	number.vertical_alignment=VERTICAL_ALIGNMENT_CENTER; b.add_child(number)
	v.add_child(b)
	if not reason.is_empty(): v.add_child(text(reason,15,Color("c7b9a6")))
	return b

func action_choice(v: VBoxContainer, action: String, caption: String="") -> void:
	var q: Dictionary=app.model.preview(action)
	choice(v,caption if caption!="" else q.get("label",action),func():app.confirm_action(action),q.ok,"" if q.ok else q.reason)

func talk_xu() -> void:
	var v:=dialogue("许舟","码头联络人","刚到岛上？今天的排班在这里。\n报到以后，陈叔那边正好需要人帮忙。","xu")
	choice(v,"查看今日排班 · 免费",app.show_schedule)
	choice(v,"今晚的节目是什么？",func():
		var detail:=dialogue("许舟","关于今晚的节目","20:00，Z 会聊「听见小岛」。你可以先沿着码头走一走。\n她读到弹幕，才有机会认识屏幕另一端的人。","xu")
		choice(detail,"明白了",app.close_modal))
	action_choice(v,"check_in")

func talk_chen() -> void:
	var v:=dialogue("陈叔","场务与道具师 · 公开工作区","这只箱子要交到黄色标记里。放好以后，再回来和我核对。\n后面的门先别进去。","chen")
	action_choice(v,"stage_work","协助场务 · 60 分钟 / +16 金币")
	if app.model.data.flags.get("stage_needs_review",false): action_choice(v,"stage_recheck","核对上次交接 · 15 分钟")
	choice(v,"关于工具工作台",app.talk_lu)

func talk_lu() -> void:
	var v:=dialogue("陆岑","工具与试做 · 08:30—09:40","带一份材料包来，我们可以做一个小小的节拍器。\n试做要留足一小时；工具只在这里使用，做完归还。","lu")
	action_choice(v,"craft","试做节目作品 · 60 分钟 / 材料 ×1")
	choice(v,"我晚一点再来。",app.close_modal)
	v.add_child(text("如果错过今天的工作台，码头的声音也能成为今晚的话题。",16,Kit.MUTED))

func talk_lin() -> void:
	var v:=dialogue("林乔","咖啡馆 · 工作与小店","早班是 08:40，下午班是 14:00。\n礼物和做节目的材料，也可以来这里看看。","lin")
	choice(v,"看看小店",app.show_shop)
	action_choice(v,"coffee_am","上午咖啡班 · 08:40—10:10")
	action_choice(v,"coffee_pm","下午咖啡班 · 14:00—15:30")
	if app.model.data.time<520: choice(v,"等到上午班开始 · 08:40",func():app.confirm_action("wait",{"target":520}))
	if app.model.data.time>=840 and app.model.data.time<1080: choice(v,"今天还可以做什么？",app.show_schedule)

func shop(selected: String="gift") -> void:
	var v: VBoxContainer=app.panel("林乔的小店","咖啡馆 · 为今晚准备一点心意")
	app.modal_kind="shop"
	tabs(v,[["工作",app.talk_lin],["小店",func():shop(selected)]],"小店")
	for key in ["gift","material"]:
		var h:=row(14)
		var entry:=PanelContainer.new(); var st:=Kit.style(Color(0.77,0.85,0.93,0.08) if key==selected else Color.TRANSPARENT,Kit.PALE*Color(1,1,1,0.45) if key==selected else Kit.LINE,5)
		st.content_margin_left=12; st.content_margin_right=12; st.content_margin_top=6; st.content_margin_bottom=6
		entry.add_theme_stylebox_override("panel",st); v.add_child(entry); entry.add_child(h)
		h.add_child(Kit.picture(load("res://assets/gift.png") if key=="gift" else Kit.icon("box"),Vector2(58,66)))
		var b: Button=app.button("小礼物" if key=="gift" else "材料包",func():shop(key)); b.size_flags_horizontal=Control.SIZE_EXPAND_FILL; Kit.quiet(b); h.add_child(b)
		var price:=single("12" if key=="gift" else "8",19,Color("e0d2b7")); price.vertical_alignment=VERTICAL_ALIGNMENT_CENTER; h.add_child(small_icon("coin",18,Color("e0d2b7"))); h.add_child(price)
	var action: String="buy_gift" if selected=="gift" else "buy_material"
	var q: Dictionary=app.model.preview(action)
	v.add_child(text("把白天挑好的心意留给 Z。\n购买后保存在背包，今晚可以赠送。" if selected=="gift" else "带到陆岑的工作台，试做一份节目作品。\n教学最晚 08:40 开始，需要留足一小时。",18,Kit.MUTED))
	var dock:=column(8); dock.name="ShopPurchaseDock"
	v.get_parent().get_parent().add_child(dock)
	dock.add_child(Kit.divider())
	info_row(dock,"耗时 / 结束于","5 分钟 · "+clock(int(app.model.data.time)+5))
	info_row(dock,"购买后",str(int(app.model.data.gold)-(12 if selected=="gift" else 8))+" 金币","coin")
	var caption: String="购买小礼物 · 12 金币" if selected=="gift" else "购买材料包 · 8 金币"
	var buy: Button=app.button(caption,func():app.confirm_action(action),true); buy.disabled=not q.ok; dock.add_child(buy)
	if not q.ok: dock.add_child(text(q.reason,16,Color("c7b9a6")))
	dock.add_child(text("当前持有 %d 份" % app.model.data.inventory.get(selected,0),15,Kit.MUTED))

func confirmation(action: String, payload: Dictionary) -> void:
	var q: Dictionary=app.model.preview(action,payload)
	var v: VBoxContainer=app.panel(q.get("label","确认行动"),"行动预览")
	app.modal_kind="confirmation"
	var symbol: String="cup" if action.begins_with("coffee") else ("gift" if action=="buy_gift" else ("tool" if action=="craft" else "clock"))
	var emblem:=small_icon(symbol,58,Kit.PALE); emblem.custom_minimum_size.y=88; v.add_child(emblem)
	if not q.ok:
		v.add_child(text(q.reason,21)); v.add_child(app.button("返回",app.close_modal)); return
	var times:=row(14); v.add_child(times)
	var start:=single(app.model.clock(),33); start.add_theme_font_override("font",Kit.font(500,true)); times.add_child(start)
	times.add_child(small_icon("arrow",28))
	var end:=single(clock(q.end_time),33); end.add_theme_font_override("font",Kit.font(500,true)); times.add_child(end)
	v.add_child(Kit.spacer(10)); v.add_child(Kit.divider())
	info_row(v,"本次耗时","%d 分钟"%q.minutes)
	if q.gold_delta!=0:
		info_row(v,"完成后报酬" if q.gold_delta>0 else "本次花费",("+" if q.gold_delta>0 else "")+str(q.gold_delta)+" 金币","coin",q.gold_delta>0)
		info_row(v,"届时持有",str(app.model.data.gold+q.gold_delta)+" 金币","bag")
	if action=="craft": info_row(v,"需要材料","材料包 ×1","box")
	v.add_child(Kit.spacer(12))
	if action in ["coffee_am","coffee_pm","stage_work","craft"]:
		v.add_child(text("完成操作后结算。开始后可以保存，下次继续手头的工作。",17,Kit.MUTED))
		v.add_child(app.button("开始",func():app.begin_activity(action,payload),true))
	else:
		if action=="wait": v.add_child(text("等待会经过中间的工作窗口，请确认已经完成想做的事。",17,Kit.MUTED))
		v.add_child(app.button("确认",func():app.do_action(action,payload),true))
	v.add_child(app.button("先不做",app.close_modal))

func inventory(tab: String="物品", selected: String="") -> void:
	var v: VBoxContainer=app.panel("背包","随身物品",true); app.modal_kind="inventory"
	var outer:=row(28); v.add_child(outer)
	var left:=column(14); left.custom_minimum_size=Vector2(330,440); left.size_flags_horizontal=Control.SIZE_FILL; outer.add_child(left)
	tabs(left,[["物品",func():inventory("物品")],["记录",func():inventory("记录")],["许可",func():inventory("许可")]],tab)
	outer.add_child(Kit.divider(true))
	var detail:=column(18); outer.add_child(detail)
	var inv: Dictionary=app.model.data.inventory
	if tab=="物品":
		var owned: Array=[]
		for key in ["gift","material","work"]:
			if int(inv.get(key,0))>0: owned.append(key)
		if not owned.has(selected): selected=owned[0] if not owned.is_empty() else ""
		for key in owned:
			var b: Button=app.button(item_name(key)+"   ×"+str(inv[key]),func():inventory("物品",key))
			b.icon=load("res://assets/gift.png") if key=="gift" else Kit.icon("box" if key=="material" else "wave")
			b.add_theme_constant_override("icon_max_width",48); b.custom_minimum_size.y=84
			if key==selected: b.add_theme_stylebox_override("normal",Kit.style(Color(0.77,0.85,0.93,0.09),Color(0.77,0.85,0.93,0.45)))
			left.add_child(b)
		left.add_child(text("只显示当前持有的物品",15,Kit.MUTED))
		if selected=="":
			detail.add_child(Kit.spacer(40)); detail.add_child(small_icon("bag",92,Color(0.77,0.85,0.93,0.45)))
			var empty:=text("背包还是空的",28); empty.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER; detail.add_child(empty)
			var note:=text("到咖啡馆看看，或者先在岛上认识一些人。\n今天的准备，会成为今晚可以带给 Z 的心意。",19,Kit.MUTED); note.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER; detail.add_child(note)
		else:
			var content:=row(24); detail.add_child(content)
			var art:=Kit.picture(load("res://assets/gift.png") if selected=="gift" else Kit.icon("box" if selected=="material" else "wave"),Vector2(245,365))
			if selected!="gift": art.modulate=Kit.PALE
			content.add_child(art)
			var copy:=column(12); content.add_child(copy); copy.add_child(Kit.spacer(12))
			copy.add_child(text(item_name(selected),32)); copy.add_child(text("已拥有 · ×%d"%inv[selected],16,Kit.PALE))
			copy.add_child(text({"gift":"白天挑好的心意，\n留给今晚的相遇。","material":"一份可以带到工作台的材料。\n亲手试做，让今晚多一点不同。","work":"你亲手做好的节拍器。\n把今天的声音带上舞台。"}[selected],20,Kit.MUTED))
			copy.add_child(Kit.divider()); copy.add_child(text("来源",15,Kit.MUTED)); copy.add_child(text(item_source(selected),18))
			copy.add_child(Kit.divider()); copy.add_child(text("用途",15,Kit.MUTED))
			copy.add_child(text({"gift":"在今晚手机直播中送出。\n使用库存，不再扣金币。","material":"工作台教学消耗一份材料，\n完成后获得节目作品。","work":"在主节目中展示。\n展示后仍归你所有。"}[selected],18))
	elif tab=="记录":
		left.add_child(text("今天留意到的事",20))
		detail.add_child(text("来自码头的声音",29)); detail.add_child(Kit.divider())
		var heard: bool=app.model.data.flags.get("bell_observed",false)
		detail.add_child(text("已记下 · 海风、铜铃与船靠岸的水声" if heard else "还没有留下这段观察",20,Kit.PALE))
		detail.add_child(text("今晚开场可以聊起它。\n这是你今天的观察，不代表已经获得后台证据或 Z 的信任。" if heard else "在码头的铃声附近停一停，按 E 留意周围的声音。",19,Kit.MUTED))
	else:
		left.add_child(text("你能进入哪些地方",20)); detail.add_child(text("工作与通行",29)); detail.add_child(Kit.divider())
		info_row(detail,"公开场务", "已报名" if app.model.data.flags.get("checked_in",false) else "尚未报到","check")
		info_row(detail,"灯塔维护区","未取得许可","lock")
		info_row(detail,"私人后台","暂不开放","lock")
		detail.add_child(text("场务报名资格只覆盖公开工作区。\n其他区域需要单独取得许可。",19,Kit.MUTED))
	footer(v,"今晚 20:00 · Z 的直播")

func item_name(key: String) -> String:
	return {"gift":"小礼物","material":"材料包","work":"节目作品"}.get(key,key)

func item_source(key: String) -> String:
	var action: String={"gift":"buy_gift","material":"buy_material","work":"craft"}[key]
	var result: String="今天的准备"
	for event in app.model.data.events:
		if event.get("action","")==action:
			result=("工作台制作" if key=="work" else "咖啡馆购买")+" · "+clock(int(event.get("end_time",0)))
	return result

func schedule() -> void:
	var v: VBoxContainer=app.panel("今天的安排","第 1 天 · "+app.model.clock()); app.modal_kind="schedule"
	var t: int=app.model.data.time
	var entries: Array=[
		["08:30—09:40","工作台新人教学","craft",510,580,"craft_done","tool"],
		["08:40—10:10","咖啡馆早班","coffee_am",520,610,"coffee_am_done","cup"],
		["12:00—14:00","午间日程","lunch",720,840,"lunch_done","sun"],
		["14:00—15:30","咖啡馆下午班","coffee_pm",840,930,"coffee_pm_done","cup"],
		["20:00—22:00","Z 的直播","enter_stream",1200,1320,"","moon"]]
	for entry in entries:
		var h:=row(14); v.add_child(h); h.add_child(small_icon(entry[6],24))
		var c:=column(2); h.add_child(c); var time:=text(entry[0],18); time.add_theme_font_override("font",Kit.font(500,true)); c.add_child(time); c.add_child(text(entry[1],17,Kit.MUTED))
		var q: Dictionary=app.model.preview(entry[2]); var complete: bool=app.model.data.flags.get(entry[5],false)
		var status: String="已完成" if complete else ("可进行" if q.ok else ("已错过" if t>=entry[4] or (entry[2]=="craft" and t>520) or (entry[2] in ["coffee_am","coffee_pm"] and t>entry[3]) else "待开始"))
		if not complete and not q.ok and status=="待开始" and t>=entry[3]: status="条件不足"
		var status_label:=single(status,15,Kit.PALE if q.ok else Kit.MUTED); status_label.vertical_alignment=VERTICAL_ALIGNMENT_CENTER; h.add_child(status_label)
		if not q.ok: status_label.tooltip_text=q.reason; status_label.mouse_filter=Control.MOUSE_FILTER_PASS
		v.add_child(Kit.divider())
	if t<720: v.add_child(app.button("等到午间 · 12:00",func():app.confirm_action("wait",{"target":720}),true))
	elif t==720: app.action_button(v,"lunch",{},"午间用餐与休息 · 到 14:00")
	elif t<1080: v.add_child(app.button("等到傍晚 · 18:00",func():app.confirm_action("wait",{"target":1080}),true))
	elif t<1200:
		app.action_button(v,"dinner",{},"晚间用餐与休息 · 30 分钟")
		v.add_child(app.button("等到 Z 开播 · 20:00",func():app.confirm_action("wait",{"target":1200}),true))
	elif t==1200: app.action_button(v,"enter_stream",{},"打开手机 · 进入 Z 的直播间")
	v.add_child(text("等待会经过中间的工作窗口。\n查看日程本身不消耗时间。",16,Kit.MUTED))

func journal() -> void:
	var v: VBoxContainer=app.panel("今天的记录","已经发生的事，留在这里。",true); app.modal_kind="journal"
	var content:=row(40); v.add_child(content)
	var lead:=column(20); lead.custom_minimum_size=Vector2(310,460); lead.size_flags_horizontal=Control.SIZE_FILL; content.add_child(lead)
	lead.add_child(small_icon("journal",56,Kit.PALE)); lead.add_child(text("第一天\n初来乍到",30))
	lead.add_child(text("21 天后，Z 将被替换。\n今天，先让她认识你。",20,Kit.MUTED)); lead.add_child(Kit.divider()); lead.add_child(text(app.objective.text,18))
	content.add_child(Kit.divider(true))
	var history:=column(16); content.add_child(history)
	if app.model.data.events.is_empty(): history.add_child(text("这一天还未开始。\n先去找许舟报到。",22,Kit.MUTED))
	for event in app.model.data.events:
		var h:=row(20); history.add_child(h)
		var when:=single(clock(int(event.get("end_time",420))),17,Kit.PALE); when.custom_minimum_size.x=64; when.add_theme_font_override("font",Kit.font(500,true)); h.add_child(when)
		var entry:=text(str(event.get("message","已记录的行动")),19); entry.size_flags_horizontal=Control.SIZE_EXPAND_FILL; h.add_child(entry)
		history.add_child(Kit.divider())

func activity() -> void:
	if app.model.data.pending.is_empty(): return
	app.activity_open=true
	var action: String=app.model.data.pending.action; var step: int=app.model.data.activity.get("step",0)
	var coffee: bool=action.begins_with("coffee")
	var v: VBoxContainer=app.panel("咖啡馆当班" if coffee else "做一份小节目","进行中 · 完成后结算")
	app.modal_kind="activity"
	var steps:=row(8); v.add_child(steps)
	for index in range(3):
		var mark:=PanelContainer.new(); mark.size_flags_horizontal=Control.SIZE_EXPAND_FILL; mark.custom_minimum_size.y=4
		var bar:=Kit.style(Kit.PALE if index<=step else Color(0.77,0.85,0.93,0.15),Color.TRANSPARENT,0)
		bar.content_margin_left=0; bar.content_margin_right=0; bar.content_margin_top=0; bar.content_margin_bottom=0
		mark.add_theme_stylebox_override("panel",bar); steps.add_child(mark)
	v.add_child(Kit.spacer(6)); v.add_child(small_icon("cup" if coffee else "tool",64,Kit.PALE))
	if coffee:
		var orders: Array=[{"who":"窗边的客人","request":"一杯不加奶的热咖啡。","answer":"黑咖啡"},{"who":"赶船的客人","request":"奶多一些，今天想喝温柔一点的。","answer":"拿铁"},{"who":"林乔","request":"最后，替我给那位客人一杯热茶。","answer":"热茶"}]
		var order: Dictionary=orders[mini(step,2)]
		v.add_child(text("第 %d / 3 位 · %s"%[step+1,order.who],17,Kit.MUTED)); v.add_child(text(order.request,25)); v.add_child(Kit.spacer(8)); v.add_child(Kit.divider())
		for drink in ["黑咖啡","拿铁","热茶"]:
			var b: Button=app.button(drink,func():
				if drink==order.answer: app.activity_step()
				else: app.toast("林乔轻声提醒：再看看客人要的是什么。"))
			b.icon=Kit.icon("cup"); v.add_child(b)
	else:
		var actions: Array=["把底座固定在工作台夹具上","把铜片装好，轻轻拨动一次","核对节拍，归还借用工具"]
		v.add_child(text("第 %d / 3 步"%[step+1],17,Kit.MUTED)); v.add_child(text(actions[mini(step,2)],25))
		v.add_child(text("陆岑：先慢一点，亲手试过的声音才有你的样子。",19,Kit.MUTED)); v.add_child(app.button("完成这一步",app.activity_step,true))
	footer(v,"中途可保存，下次继续这一份工作。","save")

func summary() -> void:
	if is_instance_valid(app.night): app.night.hide()
	var v: VBoxContainer=app.panel("第一天，留在她的记忆里","23:00 · 今日结束",true); app.modal_kind="summary"
	var h:=row(42); v.add_child(h)
	var art:=column(20); art.custom_minimum_size=Vector2(280,455); art.size_flags_horizontal=Control.SIZE_FILL; h.add_child(art)
	art.add_child(Kit.spacer(25)); art.add_child(small_icon("moon",94,Kit.PALE))
	var day:=text("DAY 01",28,Kit.PALE); day.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER; day.add_theme_font_override("font",Kit.font(450,true)); art.add_child(day)
	var quote:=text("留下一点心意，\n等待下一次相遇。",20,Kit.MUTED); quote.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER; art.add_child(quote)
	h.add_child(Kit.divider(true)); var copy:=column(18); h.add_child(copy)
	copy.add_child(text("她记住了「听潮」这个名字。" if app.model.data.recognition>0 else "今天，她还没来得及记住你的名字。",30))
	copy.add_child(text("%d 金币 · 小礼物 %d · 节目作品 %d"%[app.model.data.gold,app.model.data.inventory.gift,app.model.data.inventory.work],20,Kit.PALE)); copy.add_child(Kit.divider())
	var reasons: Array=[]; var f: Dictionary=app.model.data.flags
	if f.get("stage_done",false): reasons.append("你完成了陈叔的场务交接。")
	if f.get("coffee_am_done",false) or f.get("coffee_pm_done",false): reasons.append("你在咖啡馆度过了一班，赚到了自己的金币。")
	if f.get("gift_responded",false): reasons.append("白天挑的礼物，已经在直播中送给了 Z。")
	if f.get("bell_replied",false): reasons.append("她听见了你从码头带来的那个声音。")
	if f.get("work_responded",false): reasons.append("她回应了你亲手做的节目作品。")
	copy.add_child(text("\n".join(reasons) if not reasons.is_empty() else "你在小岛上走了一圈，也听完了她的节目。\n下一次，可以带一点自己的经历去。",20))
	copy.add_child(text("一天 Demo 到此结束，后续相识与邀约尚未开放。\n可以保存这一天，或重新开始尝试另一种准备。",16,Kit.MUTED))
	copy.add_child(app.button("保存这一天",func():app.save_game(app.MANUAL),true))
	copy.add_child(app.button("重新体验第一天",func():
		if is_instance_valid(app.night): app.night.queue_free(); app.night=null
		app.new_game()))

func clock(minutes: int) -> String:
	return "%02d:%02d"%[minutes/60,minutes%60]

func title() -> void:
	app.started=false; app.hud.hide(); app.world.frozen=true
	var v: VBoxContainer=app.panel("来自月亮的 Z","第一章 · 一天的相识",true)
	app.modal_kind="title"
	var h:=row(48); v.add_child(h)
	var copy:=column(16); h.add_child(copy)
	copy.add_child(text("先认识这座岛，\n再让她认识你。",34))
	copy.add_child(text("公司宣布，21 天后，Z 将被新一代直播 AI 替换。\n今天，你来到了岛上。",21,Kit.MUTED))
	copy.add_child(Kit.divider()); copy.add_child(text("07:00 — 23:00",23,Kit.PALE))
	copy.add_child(text("白天自由探索、工作与准备。\n20:00，用手机进入她的直播间。",19,Kit.MUTED))
	var actions:=row(16); copy.add_child(actions)
	var saved: bool=FileAccess.file_exists(app.AUTO)
	if saved:
		var resume: Button=app.button("继续上次的一天",func():app.load_game(app.AUTO),true); resume.custom_minimum_size.x=260; actions.add_child(resume)
	var begin: Button=app.button("开始新的一天",app.new_game,not saved); begin.custom_minimum_size.x=220; actions.add_child(begin)
	copy.add_child(text("键鼠游玩 · 完全离线\nZ 使用本局事实驱动的作者回应，尚未接入联网模型与语音。",15,Kit.MUTED))
	var art:=Control.new(); art.custom_minimum_size=Vector2(360,470); h.add_child(art)
	var moon:=small_icon("moon",58,Kit.PALE); moon.position=Vector2(22,15); art.add_child(moon)
	var sheet: Texture2D=load("res://assets/z-expressions.png")
	var atlas:=AtlasTexture.new(); atlas.atlas=sheet; atlas.region=Rect2(0,0,sheet.get_width()/2,sheet.get_height()/2)
	var portrait:=Kit.picture(atlas,Vector2.ZERO); portrait.position=Vector2(-15,55); portrait.size=Vector2(375,410); art.add_child(portrait)
