extends CanvasLayer
const Slot = preload("res://scripts/slot.gd")
const INK = Color("294b43")
const MUTED = Color("788474")
const PAPER = Color("f6f1df")
const GREEN = Color("335e4c")
const GOLD = Color("ba793f")
var game
var main
var root: Control
var stats: Label
var stamina: ProgressBar
var progress: ProgressBar
var route_label: Label
var objective: Label
var bag_label: Label
var bag_grid: GridContainer
var nearby: VBoxContainer
var action_box: HBoxContainer
var toast_label: Label
var toast_panel: PanelContainer
var toast_time := 0.0
var modal: Control
var modal_body: VBoxContainer
var modal_title: Label
var selected_tab := ""
var save_label: Label
var speed_buttons := []
var route_choice := 0
var refresh_clock := 0.0
var nearby_signature := ""

func style(color: Color, radius := 14, border := Color.TRANSPARENT) -> StyleBoxFlat:
	var result := StyleBoxFlat.new()
	result.bg_color = color
	result.set_corner_radius_all(radius)
	result.set_content_margin_all(12)
	result.border_color = border
	result.set_border_width_all(1)
	return result

func label(text: String, size: int = 18, color: Color = INK) -> Label:
	var node := Label.new()
	node.text = text
	node.add_theme_font_size_override("font_size", size)
	node.add_theme_color_override("font_color", color)
	node.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return node

func button(text: String, callback: Callable, primary := false, width := 0.0) -> Button:
	var node := Button.new()
	node.text = text
	node.custom_minimum_size = Vector2(width, 48)
	node.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	node.focus_mode = Control.FOCUS_NONE
	node.add_theme_font_size_override("font_size", 17)
	node.add_theme_color_override("font_color", PAPER if primary else INK)
	node.add_theme_color_override("font_hover_color", PAPER if primary else INK)
	node.add_theme_color_override("font_pressed_color", PAPER if primary else INK)
	node.add_theme_stylebox_override("normal", style(GREEN if primary else Color("e9e6d5"), 9))
	node.add_theme_stylebox_override("hover", style(Color("46745d") if primary else Color("dddcc8"), 9))
	node.add_theme_stylebox_override("pressed", style(Color("244b3d") if primary else Color("cfceb7"), 9))
	node.add_theme_stylebox_override("focus", style(Color.TRANSPARENT, 9, Color("bd955a")))
	node.pressed.connect(callback)
	return node

func panel(at: Vector2, size: Vector2, parent: Node = root) -> PanelContainer:
	var node := PanelContainer.new()
	node.position = at
	node.size = size
	node.add_theme_stylebox_override("panel", style(Color(PAPER, 0.96), 16))
	parent.add_child(node)
	return node

func column(parent: Node, gap := 10) -> VBoxContainer:
	var node := VBoxContainer.new()
	node.add_theme_constant_override("separation", gap)
	parent.add_child(node)
	return node

func row(parent: Node, gap := 10) -> HBoxContainer:
	var node := HBoxContainer.new()
	node.add_theme_constant_override("separation", gap)
	parent.add_child(node)
	return node

func setup(state, owner_node) -> void:
	game = state
	main = owner_node
	root = Control.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(root)
	var theme := Theme.new()
	if ResourceLoader.exists("res://assets/fonts/TrailSans.ttf"):
		theme.default_font = load("res://assets/fonts/TrailSans.ttf")
	else:
		var font := SystemFont.new()
		font.font_names = PackedStringArray(["PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC"])
		theme.default_font = font
	theme.default_font_size = 18
	root.theme = theme
	# A quiet editorial masthead, with a compact journey/status strip.
	var brand := label("风途", 38, INK)
	brand.position = Vector2(32, 22)
	root.add_child(brand)
	var sub := label("营 火 之 间   /   WINDTRAIL", 13, INK)
	sub.position = Vector2(34, 72)
	root.add_child(sub)
	var status_panel := panel(Vector2(310, 24), Vector2(790, 72))
	var status_row := row(status_panel, 24)
	var energy_col := column(status_row, 4)
	energy_col.custom_minimum_size.x = 180
	stats = label("", 17)
	energy_col.add_child(stats)
	stamina = ProgressBar.new()
	stamina.custom_minimum_size = Vector2(175, 8)
	stamina.show_percentage = false
	stamina.add_theme_stylebox_override("background", style(Color("dedfca"), 4))
	stamina.add_theme_stylebox_override("fill", style(GREEN, 4))
	energy_col.add_child(stamina)
	var route_col := column(status_row, 4)
	route_col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	route_label = label("", 16)
	route_col.add_child(route_label)
	progress = ProgressBar.new()
	progress.custom_minimum_size = Vector2(340, 8)
	progress.show_percentage = false
	progress.add_theme_stylebox_override("background", style(Color("dedfca"), 4))
	progress.add_theme_stylebox_override("fill", style(GOLD, 4))
	route_col.add_child(progress)
	status_row.add_child(button("旅程手记", func(): open_modal("journal"), false, 118))
	var goal_panel := panel(Vector2(32, 120), Vector2(280, 126))
	var goal_col := column(goal_panel, 8)
	goal_col.add_child(label("此刻的小目标", 13, GOLD))
	objective = label("", 18)
	objective.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	objective.custom_minimum_size.x = 250
	goal_col.add_child(objective)
	var hint_panel := panel(Vector2(32, 266), Vector2(280, 0))
	var hint_col := column(hint_panel, 9)
	hint_col.add_child(label("路边的礼物", 13, GOLD))
	nearby = column(hint_col, 7)
	# Inventory remains visible while traveling, with drag-to-swap stacks.
	var bag_panel := panel(Vector2(-304, 120), Vector2(272, 546))
	bag_panel.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	bag_panel.offset_left = -304
	bag_panel.offset_top = 120
	bag_panel.offset_right = -32
	bag_panel.offset_bottom = 666
	var bag_col := column(bag_panel, 11)
	bag_col.add_child(label("随身行囊", 24))
	bag_label = label("", 13, MUTED)
	bag_col.add_child(bag_label)
	bag_grid = GridContainer.new()
	bag_grid.columns = 3
	bag_grid.add_theme_constant_override("h_separation", 7)
	bag_grid.add_theme_constant_override("v_separation", 7)
	bag_col.add_child(bag_grid)
	bag_col.add_child(label("点选查看 · 拖动交换位置", 12, MUTED))
	var bag_actions := row(bag_col, 7)
	bag_actions.add_child(button("整理", func(): game.sort_bag(), false, 78))
	bag_actions.add_child(button("吃点食物  E", func(): main.eat_best(), true, 151))
	# Bottom navigation stays outside the scenery's focal area.
	var bottom := panel(Vector2(32, -140), Vector2(1376, 108))
	bottom.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	bottom.offset_left = 32
	bottom.offset_top = -140
	bottom.offset_right = -32
	bottom.offset_bottom = -32
	var bottom_col := column(bottom, 9)
	action_box = row(bottom_col, 12)
	save_label = label("", 12, MUTED)
	bottom_col.add_child(save_label)
	toast_panel = panel(Vector2(-330, -238), Vector2(660, 56))
	toast_panel.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	toast_panel.offset_left = -330
	toast_panel.offset_top = -230
	toast_panel.offset_right = 330
	toast_panel.offset_bottom = -174
	toast_label = label("", 17)
	toast_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	toast_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	toast_label.custom_minimum_size.x = 620
	toast_panel.add_child(toast_label)
	toast_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	toast_panel.visible = false
	game.changed.connect(refresh)
	game.message.connect(toast)
	refresh()
	if not game.recovery_note.is_empty():
		toast(game.recovery_note)

func clear(node: Node) -> void:
	for child in node.get_children():
		node.remove_child(child)
		child.queue_free()

func refresh() -> void:
	bag_label.text = "%d / %d 格    %.1f / %.0f kg" % [game.s.bag.size(), game.slots(), game.weight(), game.max_weight()]
	clear(bag_grid)
	for i in range(game.slots()):
		var slot := Slot.new()
		slot.hud = self
		slot.focus_mode = Control.FOCUS_NONE
		slot.index = i
		slot.custom_minimum_size = Vector2(76, 66 if game.slots() == 12 else 52)
		slot.add_theme_font_size_override("font_size", 15)
		slot.add_theme_color_override("font_color", INK)
		slot.add_theme_stylebox_override("normal", style(Color("e8e5d3"), 9))
		slot.add_theme_stylebox_override("hover", style(Color("dcdcc4"), 9, Color("a7ad8c")))
		if i < game.s.bag.size():
			var stack: Dictionary = game.s.bag[i]
			var item: Dictionary = game.ITEMS[stack.id]
			slot.text = "%s\n× %d" % [item.name, stack.n]
			slot.tooltip_text = "%s\n%s\n单件 %.2f kg · 售价 %d 叶币" % [item.name, item.use, item.weight, item.price]
			slot.pressed.connect(func(): item_detail(stack.id))
			slot.add_theme_stylebox_override("normal", style(Color(item.color).lightened(0.66), 9))
		else:
			slot.text = "·"
			slot.add_theme_color_override("font_color", Color("b3b8a1"))
		bag_grid.add_child(slot)
	clear(action_box)
	speed_buttons.clear()
	if game.s.travel:
		action_box.add_child(label("顺着风，慢慢走。", 20))
		for i in range(3):
			var b := button(["停下  Space", "漫步  1", "快走  2"][i], func(): main.set_speed(i), i == main.speed, 140)
			action_box.add_child(b)
			speed_buttons.append(b)
		action_box.add_child(button("拾取最近资源  F", func(): main.collect_nearest(), true, 200))
		action_box.add_child(button("免费回营", func(): open_modal("rescue"), false, 140))
	else:
		action_box.add_child(label("营火已经升起。", 20))
		action_box.add_child(button("选择路线 · 出发", func(): open_modal("routes"), true, 212))
		action_box.add_child(button("手作工坊", func(): open_modal("craft"), false, 155))
		action_box.add_child(button("营地商人", func(): open_modal("trade"), false, 155))
		action_box.add_child(button("私人仓库", func(): open_modal("storage"), false, 155))
		action_box.add_child(button("背包与小屋", func(): open_modal("home"), false, 155))
	update_status()
	update_nearby()
	if is_instance_valid(modal) and selected_tab not in ["item", "rescue"]:
		fill_modal()

func update_status() -> void:
	stats.text = "体力 %d   ·   叶币 %d" % [game.s.stamina, game.s.coins]
	stamina.value = game.s.stamina
	if game.s.travel:
		var route: Dictionary = game.ROUTES[int(game.s.route)]
		route_label.text = "%s    %d / %d 米  ·  %s" % [route.name, game.s.progress, route.length, "已暂停" if main.speed == 0 or main.paused() else ("漫步中" if main.speed == 1 else "快走中")]
		progress.value = game.s.progress / route.length * 100
	else:
		route_label.text = "松风营地    ·    已完成 %d 段旅程" % game.s.stats.arrive
		progress.value = 100
	objective.text = "建一间小屋，让每次远行都有归处。"
	for i in range(game.QUESTS.size()):
		if i not in game.s.claimed:
			var quest: Dictionary = game.QUESTS[i]
			var now := mini(game.s.stats[quest.key], quest.goal)
			objective.text = "%s\n%d / %d%s" % [quest.desc, now, quest.goal, " · 手记里可领奖" if now == quest.goal else ""]
			break
	save_label.text = ("●  旅程自动保存" if game.save_ok else "！保存失败，请检查磁盘空间") + "    /    点击路边资源或按 F 拾取    ·    Space 暂停    ·    E 吃食物    ·    Esc 关闭窗口    ·    离开窗口自动暂停"

func update_nearby() -> void:
	var signature := str(game.s.travel)
	for item in game.resources():
		if item.key not in game.s.collected and absf(item.distance - float(game.s.progress)) <= 14:
			signature += item.key
	if signature == nearby_signature:
		return
	nearby_signature = signature
	clear(nearby)
	if not game.s.travel:
		nearby.add_child(label("先选一条路线出发。\n材料在沿途等着你。", 16, MUTED))
		return
	var found := 0
	for item in game.resources():
		if item.key not in game.s.collected and absf(item.distance - float(game.s.progress)) <= 14:
			var b := button("拾取 %s    +1" % game.ITEMS[item.id].name, func(): game.collect(item.key), false, 248)
			nearby.add_child(b)
			found += 1
			if found == 3:
				break
	if found == 0:
		nearby.add_child(label("向前走走，留意小径两侧。", 15, MUTED))

func toast(text: String) -> void:
	toast_label.text = text
	toast_time = 4.5
	toast_panel.visible = true
	root.move_child(toast_panel, -1)

func tick(delta: float) -> void:
	toast_time -= delta
	toast_panel.visible = toast_time > 0
	refresh_clock += delta
	if refresh_clock > 0.3:
		refresh_clock = 0.0
		update_status()
		if not is_instance_valid(modal):
			update_nearby()

func close_modal() -> void:
	if is_instance_valid(modal):
		root.remove_child(modal)
		modal.queue_free()
	modal = null
	selected_tab = ""

func open_modal(tab: String) -> void:
	close_modal()
	selected_tab = tab
	modal = Control.new()
	modal.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root.add_child(modal)
	var shade := ColorRect.new()
	shade.color = Color(0.10, 0.19, 0.16, 0.57)
	shade.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	modal.add_child(shade)
	var card := panel(Vector2(-410, -330), Vector2(820, 660), modal)
	card.set_anchors_preset(Control.PRESET_CENTER)
	card.offset_left = -410
	card.offset_top = -330
	card.offset_right = 410
	card.offset_bottom = 330
	var col := column(card, 16)
	var head := row(col)
	modal_title = label("", 28)
	modal_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(modal_title)
	head.add_child(button("关闭  Esc", close_modal, false, 112))
	var scroll := ScrollContainer.new()
	scroll.custom_minimum_size = Vector2(780, 545)
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	col.add_child(scroll)
	modal_body = column(scroll, 13)
	modal_body.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	fill_modal()

func description(text: String, color: Color = MUTED) -> void:
	var l := label(text, 16, color)
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	l.custom_minimum_size.x = 740
	modal_body.add_child(l)

func line_item(title: String, detail: String) -> HBoxContainer:
	var card := PanelContainer.new()
	card.add_theme_stylebox_override("panel", style(Color("e9e7d7"), 12))
	modal_body.add_child(card)
	var r := row(card, 12)
	var c := column(r, 5)
	c.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	c.add_child(label(title, 20))
	var sub := label(detail, 14, MUTED)
	sub.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	sub.custom_minimum_size.x = 320
	c.add_child(sub)
	return r

func fill_modal() -> void:
	clear(modal_body)
	match selected_tab:
		"routes":
			modal_title.text = "下一段路，去哪里？"
			description("营地休息后满体力出发。路上随时可以停下整理背包；免费回营会保留全部物品。")
			for i in range(game.ROUTES.size()):
				var route: Dictionary = game.ROUTES[i]
				var locked: bool = i == 1 and game.s.stats.arrive < 1
				var r := line_item("0%d   %s · %d m" % [i + 1, route.name, route.length], route.subtitle + "\n" + route.desc + "\n到达奖励：%d 叶币" % route.reward)
				var b := button("完成首程后开放" if locked else "踏上旅途 →", func(): main.depart(i), not locked, 188)
				b.disabled = locked
				r.add_child(b)
			description("出发前的小建议：野莓可直接吃；把两份野莓做成口粮，恢复更多体力。")
		"craft":
			modal_title.text = "手作工坊"
			description("把沿途的收获变成有用的物品。只使用背包中的材料，制作后自动收好。")
			for id in game.RECIPES:
				var needs := ""
				for input in game.RECIPES[id]:
					needs += "%s %d / %d    " % [game.ITEMS[input].name, game.count(input), game.RECIPES[id][input]]
				var r := line_item(game.ITEMS[id].name + " ×1", needs + "\n" + game.ITEMS[id].use)
				r.add_child(button("制作", func(): game.craft(id), true, 118))
			description("木板：2 松木 → 1 木板\n麻绳：3 亚麻 → 1 麻绳\n莓果口粮：2 野莓 → 1 口粮（恢复 32 体力）")
		"trade":
			modal_title.text = "营地商人  ·  NPC"
			description("叶币 %d  /  固定价格即时成交。买卖按钮每次交易 1 件；无其他玩家参与。" % game.s.coins)
			for id in game.ITEMS:
				var item: Dictionary = game.ITEMS[id]
				var r := line_item(item.name + "    持有 %d" % game.count(id), item.use)
				r.add_child(button("卖 +%d" % item.price, func(): game.trade(id, false), false, 105))
				r.add_child(button("买 −%d" % (item.price * 2), func(): game.trade(id, true), true, 105))
		"storage":
			modal_title.text = "私人仓库"
			description("留一些材料在营地，下一段路走得更轻松。每次存取 1 件；仓库不计入旅行负重。")
			for id in game.ITEMS:
				var r := line_item(game.ITEMS[id].name, "背包 %d    /    仓库 %d" % [game.count(id), game.count(id, "storage")])
				r.add_child(button("存入 →", func(): game.transfer(id, true), false, 105))
				r.add_child(button("← 取出", func(): game.transfer(id, false), true, 105))
		"home":
			modal_title.text = "让旅途有个归处"
			description("先扩充行囊，再为自己搭一间小屋。升级消耗背包内材料；价格与收益都在这里。")
			var r := line_item("旅行背包 · %s" % ("已升级" if game.s.bag_level else "初始款"), "需要：2 木板 + 3 麻绳 + 30 叶币\n收益：12 → 16 格，12 → 18 kg")
			var b := button("已完成" if game.s.bag_level else "升级背包", func(): game.upgrade(), true, 154)
			b.disabled = game.s.bag_level > 0
			r.add_child(b)
			var home_row := line_item("河畔小屋", "需要：4 木板 + 2 麻绳 + 40 叶币\n收益：营地出现你的木屋与暖色灯光")
			var home_button := button("已建成" if game.s.home else "建造小屋", func(): game.upgrade(true), true, 154)
			home_button.disabled = game.s.home
			home_row.add_child(home_button)
			description("已有：木板 %d · 麻绳 %d · 叶币 %d\n小屋是这段旅程的纪念；当前 Demo 暂不开放室内与家具布置。" % [game.count("board"), game.count("rope"), game.s.coins])
		"journal":
			modal_title.text = "远行者的手记"
			description("已经走完 %d 段路 · 成功采集 %d 次 · 制作 %d 件物品" % [game.s.stats.arrive, game.s.stats.gather, game.s.stats.craft])
			for i in range(game.QUESTS.size()):
				var q: Dictionary = game.QUESTS[i]
				var ready: bool = game.s.stats[q.key] >= q.goal
				var claimed: bool = i in game.s.claimed
				var r := line_item(q.title, "%s  %d / %d\n奖励：%d 叶币" % [q.desc, mini(game.s.stats[q.key], q.goal), q.goal, q.reward])
				var b := button("已领取" if claimed else ("领取奖励" if ready else "进行中"), func(): game.claim(i), ready and not claimed, 130)
				b.disabled = claimed or not ready
				r.add_child(b)
			description("旅程保存在这台设备。重新打开游戏即可接着走。")
		"rescue":
			modal_title.text = "回营歇一会儿？"
			description("这次旅途提前结束，全部物品都会保留。营地会恢复你的体力；本次不发放走完全程的叶币奖励。")
			modal_body.add_child(button("免费返回营地", func(): main.rescue(), true))
			modal_body.add_child(button("继续这段旅途", close_modal))

func item_detail(id: String) -> void:
	open_modal("item")
	var item: Dictionary = game.ITEMS[id]
	modal_title.text = item.name
	description(item.use, INK)
	description("背包数量：%d\n单件重量：%.2f kg\n营地售价：%d 叶币" % [game.count(id), item.weight, item.price])
	if item.has("food"):
		modal_body.add_child(button("吃一份 · 恢复 %d 体力" % item.food, func(): game.eat(id); close_modal(), true))
	if not game.s.travel:
		modal_body.add_child(button("存一份到仓库", func(): game.transfer(id, true); close_modal()))
