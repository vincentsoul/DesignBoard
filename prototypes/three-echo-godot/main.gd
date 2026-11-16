extends Node3D

const COLORS = {"blue": Color("3389e6"), "red": Color("e86c60"), "green": Color("4fbd91"), "none": Color("e0e9ed")}
const NAMES = {"red": "红色晶体", "green": "绿色晶体", "none": "消色晶体"}
const REACH = 2.6
var platforms: Array[AnimatableBody3D] = []
var sockets: Dictionary = {}
var crystals: Dictionary = {}
var locations = {"red": "A_red", "green": "B_green", "none": "stand"}
var colors: Array = ["green", "red", "blue"]
var heights: Array = [0, 0, 0]
var held := ""
var fired := false
var running := false
var completed := false
var chain_generation := 0
var player: CharacterBody3D
var camera: Camera3D
var highlighted: MeshInstance3D
var highlight_material: StandardMaterial3D
var hud: CanvasLayer
var prompt: Label
var status: Label
var metrics: Label
var carrying: Label
var menu: PanelContainer
var menu_title: Label
var menu_body: Label
var start_button: Button
var toast := "搬动晶体，安排一次连锁，搭出通往出口的楼梯。"
var pulse_tweens: Array[Tween] = []

func _ready() -> void:
	_build_world()
	_build_player()
	_build_ui()
	reset_level()
	show_menu("三级回声", "第一人称色域解谜 · 白模试玩\n\n地面每换色一次，平台升高 1 米。\n搬动绿色与消色晶体，再射击红色晶体，\n把 C / B / A 拼成通往四米高出口的楼梯。")

func mat(color: Color) -> StandardMaterial3D:
	var result := StandardMaterial3D.new()
	result.albedo_color = color
	result.roughness = 0.8
	return result

func mesh_box(parent: Node3D, size: Vector3, pos: Vector3, color: Color) -> MeshInstance3D:
	var mesh := MeshInstance3D.new()
	var shape := BoxMesh.new()
	shape.size = size
	mesh.mesh = shape
	mesh.position = pos
	mesh.material_override = mat(color)
	parent.add_child(mesh)
	return mesh

func collision_box(parent: Node3D, size: Vector3, pos: Vector3) -> void:
	var collision := CollisionShape3D.new()
	var shape := BoxShape3D.new()
	shape.size = size
	collision.shape = shape
	collision.position = pos
	parent.add_child(collision)

func static_box(pos: Vector3, size: Vector3, visible_mesh: bool = true) -> StaticBody3D:
	var body := StaticBody3D.new()
	add_child(body)
	body.position = pos
	collision_box(body, size, Vector3.ZERO)
	if visible_mesh:
		mesh_box(body, size, Vector3.ZERO, Color("cbd4db"))
	return body

func world_label(parent: Node3D, text: String, pos: Vector3, tint: Color = Color.WHITE) -> void:
	var label := Label3D.new()
	label.text = text
	label.position = pos
	label.font_size = 32
	label.pixel_size = 0.002
	label.modulate = tint
	label.outline_size = 3
	label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	parent.add_child(label)

func _build_world() -> void:
	var whitebox = load("res://assets/whitebox.glb").instantiate()
	whitebox.name = "BlenderWhitebox"
	add_child(whitebox)
	for child in whitebox.get_children():
		if child.name.begins_with("Sign"):
			child.hide()
	# Imported Blender geometry is the visual shell. Physics is authored here.
	static_box(Vector3(0, -0.3, 2), Vector3(5, 0.6, 4), false)
	static_box(Vector3(0, 1.8, -11), Vector3(3.4, 4.4, 4), false)
	static_box(Vector3(0, -3, -4.5), Vector3(7, 0.3, 20), false)
	static_box(Vector3(-3.7, 2, -4.5), Vector3(0.3, 10, 20))
	static_box(Vector3(3.7, 2, -4.5), Vector3(0.3, 10, 20))
	static_box(Vector3(0, 2, -14), Vector3(7.5, 10, 0.3))
	static_box(Vector3(0, 1, 4.3), Vector3(7.5, 8, 0.3))
	for i in range(3):
		var platform := AnimatableBody3D.new()
		platform.name = "Platform" + "CBA"[i]
		platform.sync_to_physics = false
		add_child(platform)
		platform.position = Vector3(0, 0, -1.5 - i * 3)
		mesh_box(platform, Vector3(2.8, 0.44, 2.8), Vector3(0, -0.22, 0), COLORS[colors[i]])
		collision_box(platform, Vector3(2.8, 0.44, 2.8), Vector3(0, -0.22, 0))
		world_label(platform, "CBA"[i], Vector3(-1, 0.5, 0.8))
		platforms.append(platform)
	_make_socket("A_red", 2, -0.65, "red")
	_make_socket("A_green", 2, 0.65, "green")
	_make_socket("B_green", 1, 0.65, "green")
	_make_socket("B_none", 1, -0.65, "none")
	_make_socket("stand", -1, -1.8, "none")
	for key in ["red", "green", "none"]:
		var body := StaticBody3D.new()
		body.name = "Crystal_" + key
		body.collision_layer = 2
		body.collision_mask = 0
		body.set_meta("kind", "crystal")
		body.set_meta("key", key)
		add_child(body)
		var mesh := MeshInstance3D.new()
		var cone := CylinderMesh.new()
		cone.top_radius = 0
		cone.bottom_radius = 0.25
		cone.height = 0.7
		cone.radial_segments = 4
		mesh.mesh = cone
		mesh.material_override = mat(COLORS[key])
		body.add_child(mesh)
		var collision := CollisionShape3D.new()
		var shape := CylinderShape3D.new()
		shape.radius = 0.25
		shape.height = 0.7
		collision.shape = shape
		body.add_child(collision)
		crystals[key] = body
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-55, -25, 0)
	light.light_energy = 0.65
	light.shadow_enabled = true
	add_child(light)
	var world := WorldEnvironment.new()
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color("899fab")
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color("d9e5f1")
	environment.ambient_light_energy = 0.3
	world.environment = environment
	add_child(world)
	highlight_material = mat(Color("ffd879"))
	highlight_material.emission_enabled = true
	highlight_material.emission = Color("c99436")
	highlight_material.emission_energy_multiplier = 0.4

func _make_socket(key: String, index: int, x: float, accept: String) -> void:
	var body := StaticBody3D.new()
	body.name = "Socket_" + key
	body.collision_layer = 2
	body.collision_mask = 0
	body.set_meta("kind", "socket")
	body.set_meta("slot", key)
	body.set_meta("accept", accept)
	body.set_meta("index", index)
	if index >= 0:
		platforms[index].add_child(body)
		body.position = Vector3(x, 0.62 if accept == "red" else 0.12, 0)
	else:
		add_child(body)
		body.position = Vector3(x, 0.32, 2)
	mesh_box(body, Vector3(0.75, 0.22, 0.75), Vector3.ZERO, COLORS[accept])
	collision_box(body, Vector3(0.75, 0.22, 0.75), Vector3.ZERO)
	if accept == "red":
		mesh_box(body, Vector3(0.35, 0.5, 0.35), Vector3(0, -0.36, 0), Color("9faeb8"))
		collision_box(body, Vector3(0.35, 0.5, 0.35), Vector3(0, -0.36, 0))
	world_label(body, "固定起爆点" if accept == "red" else NAMES[accept] + "基座", Vector3(0, 0.22, -0.35), Color("283841"))
	sockets[key] = body

func _build_player() -> void:
	player = CharacterBody3D.new()
	player.name = "Player"
	player.collision_layer = 4
	player.collision_mask = 1
	player.floor_snap_length = 0.3
	add_child(player)
	var shape := CapsuleShape3D.new()
	shape.radius = 0.24
	shape.height = 1.65
	var collider := CollisionShape3D.new()
	collider.shape = shape
	collider.position.y = 0.825
	player.add_child(collider)
	camera = Camera3D.new()
	camera.position.y = 1.55
	camera.fov = 76
	player.add_child(camera)
	camera.current = true

func ui_label(parent: Node, text: String, size: int, tint: Color = Color.WHITE) -> Label:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", size)
	label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.7))
	label.add_theme_constant_override("shadow_offset_x", 1)
	label.add_theme_constant_override("shadow_offset_y", 2)
	label.add_theme_color_override("font_color", tint)
	parent.add_child(label)
	return label

func hud_style() -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.06, 0.11, 0.15, 0.85)
	style.set_corner_radius_all(8)
	style.content_margin_left = 14
	style.content_margin_right = 14
	style.content_margin_top = 10
	style.content_margin_bottom = 10
	return style

func _build_ui() -> void:
	hud = CanvasLayer.new()
	add_child(hud)
	var root := Control.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var font := SystemFont.new()
	font.font_names = PackedStringArray(["PingFang SC", "Heiti SC", "Noto Sans CJK SC"])
	var theme := Theme.new()
	theme.default_font = font
	root.theme = theme
	hud.add_child(root)
	var header_panel := PanelContainer.new()
	header_panel.position = Vector2(18, 18)
	header_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	header_panel.add_theme_stylebox_override("panel", hud_style())
	root.add_child(header_panel)
	var header := VBoxContainer.new()
	header_panel.add_child(header)
	ui_label(header, "三级回声", 30)
	ui_label(header, "色域实验室  /  第一人称白模", 14, Color("b5d0da"))
	metrics = ui_label(header, "", 17)
	carrying = ui_label(header, "", 16, Color("ffdc90"))
	var cross := Control.new()
	root.add_child(cross)
	cross.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	cross.mouse_filter = Control.MOUSE_FILTER_IGNORE
	cross.draw.connect(func():
		var center := cross.size * 0.5
		cross.draw_line(center - Vector2(6, 0), center + Vector2(6, 0), Color("182a35"), 4)
		cross.draw_line(center - Vector2(0, 6), center + Vector2(0, 6), Color("182a35"), 4)
		cross.draw_line(center - Vector2(5, 0), center + Vector2(5, 0), Color.WHITE, 2)
		cross.draw_line(center - Vector2(0, 5), center + Vector2(0, 5), Color.WHITE, 2)
	)
	prompt = ui_label(root, "", 23, Color("ffdf9c"))
	prompt.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	prompt.offset_left = -350
	prompt.offset_right = 350
	prompt.offset_top = 34
	prompt.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	var footer_panel := PanelContainer.new()
	root.add_child(footer_panel)
	footer_panel.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
	footer_panel.offset_left = 18
	footer_panel.offset_right = -18
	footer_panel.offset_top = -100
	footer_panel.offset_bottom = -18
	footer_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	footer_panel.add_theme_stylebox_override("panel", hud_style())
	var footer := VBoxContainer.new()
	footer_panel.add_child(footer)
	status = ui_label(footer, "", 17)
	ui_label(footer, "WASD 移动   鼠标转头   E 拿起 / 放置   左键射击   空格跳跃   R 重置   Esc 暂停", 15, Color("c9d8df"))
	menu = PanelContainer.new()
	root.add_child(menu)
	menu.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	menu.offset_left = -290
	menu.offset_right = 290
	menu.offset_top = -245
	menu.offset_bottom = 245
	var style := StyleBoxFlat.new()
	style.bg_color = Color("182a35")
	style.set_corner_radius_all(16)
	style.content_margin_left = 36
	style.content_margin_right = 36
	style.content_margin_top = 30
	style.content_margin_bottom = 30
	menu.add_theme_stylebox_override("panel", style)
	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 18)
	menu.add_child(content)
	menu_title = ui_label(content, "", 34)
	menu_body = ui_label(content, "", 18, Color("c6d5dd"))
	start_button = Button.new()
	start_button.text = "开始试玩"
	start_button.custom_minimum_size.y = 48
	content.add_child(start_button)
	start_button.pressed.connect(resume_game)
	var reset_button := Button.new()
	reset_button.text = "重置关卡"
	content.add_child(reset_button)
	reset_button.pressed.connect(func(): reset_level(); resume_game())
	var quit_button := Button.new()
	quit_button.text = "退出 Demo"
	content.add_child(quit_button)
	quit_button.pressed.connect(func(): get_tree().quit())

func show_menu(title: String, body: String) -> void:
	running = false
	for tween in pulse_tweens:
		if tween.is_valid():
			tween.pause()
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_clear_highlight()
	prompt.text = ""
	menu_title.text = title
	menu_body.text = body
	menu.show()
	start_button.grab_focus()

func resume_game() -> void:
	menu.hide()
	running = true
	for tween in pulse_tweens:
		if tween.is_valid():
			tween.play()
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	start_button.text = "继续试玩"

func _notification(what: int) -> void:
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT and running:
		show_menu("已暂停", "点击继续试玩，重新接管鼠标与键盘。\n\n平台每次换色上升 1 米。\n可用 R 重置并尝试新的晶体布置。")

func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		var key: int = event.physical_keycode if event.physical_keycode else event.keycode
		if key == KEY_ESCAPE and running:
			show_menu("已暂停", "点击继续试玩。\n\nE 拿取或放置晶体，左键射击固定红色晶体。")
		elif running and key == KEY_E:
			interact()
		elif running and key == KEY_R:
			reset_level()
		elif running and key == KEY_SPACE and player.is_on_floor():
			player.velocity.y = 5.5
	if not running:
		return
	if event is InputEventMouseMotion:
		player.rotate_y(-event.relative.x * 0.0025)
		camera.rotation.x = clampf(camera.rotation.x - event.relative.y * 0.0025, -1.35, 1.35)
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		shoot()

func _physics_process(delta: float) -> void:
	if not running:
		return
	var input := Vector2(float(Input.is_physical_key_pressed(KEY_D)) - float(Input.is_physical_key_pressed(KEY_A)), float(Input.is_physical_key_pressed(KEY_S)) - float(Input.is_physical_key_pressed(KEY_W)))
	var direction := player.basis * Vector3(input.x, 0, input.y).normalized()
	player.velocity.x = direction.x * 3.2
	player.velocity.z = direction.z * 3.2
	if not player.is_on_floor():
		player.velocity.y -= 12 * delta
	_step_up(direction, delta)
	player.move_and_slide()
	if player.position.y < -1.5:
		player.position = Vector3(0, 0.05, 3)
		player.velocity = Vector3.ZERO
		toast = "已回到入口，晶体布置保留。"
	if player.position.z < -10 and player.position.y > 3.8 and not completed:
		completed = true
		show_menu("通关！", "你用一次连锁搭出了三级楼梯。\n\nC 参与 1 轮，B 参与 2 轮，A 参与 3 轮。\n可以重置关卡，试试另一种布置。")
	_update_target()
	metrics.text = "平台高度   C %dm   /   B %dm   /   A %dm" % heights
	carrying.text = "手持  " + NAMES[held] if held else "手持  空"
	status.text = toast

func _step_up(direction: Vector3, delta: float) -> void:
	if direction.length_squared() < 0.1 or not player.is_on_floor() or player.velocity.y > 0.1:
		return
	var ahead := player.position + direction * 0.5
	var query := PhysicsRayQueryParameters3D.create(ahead + Vector3.UP * 1.1, ahead - Vector3.UP * 0.15, 1)
	var hit := get_world_3d().direct_space_state.intersect_ray(query)
	if hit.is_empty():
		return
	var rise: float = hit.position.y - player.position.y
	if rise > 0.05 and rise <= 1.02:
		var elevated := player.global_transform
		elevated.origin.y += rise + 0.02
		if not player.test_move(elevated, direction * 3.2 * delta):
			player.global_transform = elevated

func target_hit() -> Dictionary:
	var origin := camera.global_position
	var query := PhysicsRayQueryParameters3D.create(origin, origin - camera.global_basis.z * 25, 3)
	return get_world_3d().direct_space_state.intersect_ray(query)

func _clear_highlight() -> void:
	if is_instance_valid(highlighted):
		highlighted.material_overlay = null
	highlighted = null

func _update_target() -> void:
	_clear_highlight()
	prompt.text = ""
	if fired:
		return
	var hit := target_hit()
	if hit.is_empty():
		return
	var body: Node3D = hit.collider
	var kind: String = body.get_meta("kind", "")
	var close: bool = camera.global_position.distance_to(hit.position) <= REACH
	var eligible := false
	if kind == "crystal" and not held:
		if body.get_meta("key") == "red":
			prompt.text = "左键射击 · 触发连锁"
			eligible = true
		else:
			prompt.text = "按 E 拿起" if close else "走近晶体后按 E 拿起"
			eligible = close
	elif kind == "socket" and held:
		if body.get_meta("accept") != held:
			prompt.text = "基座不匹配 · 寻找同色基座"
		elif _occupied(body.get_meta("slot")):
			prompt.text = "基座已有晶体"
		else:
			prompt.text = "按 E 放置" if close else "靠近基座后按 E 放置"
			eligible = close
	if eligible:
		highlighted = body.get_child(0) as MeshInstance3D
		if highlighted:
			highlighted.material_overlay = highlight_material

func _occupied(slot: String) -> bool:
	return locations.values().has(slot)

func interact() -> void:
	if fired:
		toast = "连锁已经触发，按 R 重置后重新布置。"
		return
	var hit := target_hit()
	if hit.is_empty() or camera.global_position.distance_to(hit.position) > REACH:
		return
	var body: Node3D = hit.collider
	if not held and body.get_meta("kind", "") == "crystal":
		var key: String = body.get_meta("key")
		if key == "red":
			toast = "红色晶体固定在 A，瞄准后左键射击。"
			return
		_clear_highlight()
		held = key
		locations[key] = "held"
		body.collision_layer = 0
		body.reparent(camera)
		body.position = Vector3(0.43, -0.34, -0.85)
		body.rotation = Vector3.ZERO
		toast = "拿起了" + NAMES[key] + "，寻找匹配的基座。"
	elif held and body.get_meta("kind", "") == "socket":
		var slot: String = body.get_meta("slot")
		if body.get_meta("accept") != held or _occupied(slot):
			return
		_clear_highlight()
		var crystal: StaticBody3D = crystals[held]
		crystal.reparent(body)
		crystal.position = Vector3(0, 0.5, 0)
		crystal.rotation = Vector3.ZERO
		crystal.collision_layer = 2
		locations[held] = slot
		toast = "放好了" + NAMES[held] + "。"
		held = ""

func shoot() -> void:
	if fired:
		return
	if held:
		toast = "先把手中的晶体放到基座上。"
		return
	var hit := target_hit()
	if hit.is_empty() or hit.collider.get_meta("key", "") != "red":
		toast = "未命中起爆点，瞄准 A 上的红色晶体。"
		return
	fired = true
	_clear_highlight()
	_run_chain(chain_generation)

func _run_chain(generation: int) -> void:
	var queue: Array[String] = ["red"]
	var scheduled: Array[String] = ["red"]
	var wave := 0
	while not queue.is_empty():
		if generation != chain_generation:
			return
		if not running:
			await get_tree().physics_frame
			continue
		var key: String = queue.pop_front()
		var slot: String = locations[key]
		var index: int = sockets[slot].get_meta("index")
		var crystal: StaticBody3D = crystals[key]
		crystal.hide()
		crystal.collision_layer = 0
		locations[key] = "consumed"
		if index < 0:
			continue
		var source: String = colors[index]
		if source == "none" or source == key:
			continue
		var affected: Array[int] = []
		for i in range(3):
			if colors[i] == source:
				affected.append(i)
				colors[i] = key
				heights[i] += 1
				var mesh: MeshInstance3D = platforms[i].get_child(0)
				mesh.material_override = mat(COLORS[key])
				var tween := create_tween().set_process_mode(Tween.TWEEN_PROCESS_PHYSICS)
				tween.tween_property(platforms[i], "position:y", float(heights[i]), 0.7).set_trans(Tween.TRANS_SINE)
				pulse_tweens.append(tween)
		for other in locations:
			if scheduled.has(other) or locations[other] in ["held", "consumed"]:
				continue
			var other_index: int = sockets[locations[other]].get_meta("index")
			if affected.has(other_index):
				queue.append(other)
				scheduled.append(other)
		wave += 1
		toast = "第 %d 轮 · %s → %s · 平台上升 1 米" % [wave, source.to_upper(), key.to_upper()]
		var elapsed := 0.0
		while elapsed < 1.4:
			await get_tree().physics_frame
			if generation != chain_generation:
				return
			if running:
				elapsed += get_physics_process_delta_time()
	if generation == chain_generation:
		toast = "连锁完成！沿 C → B → A 前往出口。" if heights == [1, 2, 3] else "楼梯还不够高，按 R 重置，试试调整晶体位置。"

func reset_level() -> void:
	chain_generation += 1
	for tween in pulse_tweens:
		if tween.is_valid():
			tween.kill()
	pulse_tweens.clear()
	_clear_highlight()
	held = ""
	fired = false
	completed = false
	colors = ["green", "red", "blue"]
	heights = [0, 0, 0]
	locations = {"red": "A_red", "green": "B_green", "none": "stand"}
	for i in range(3):
		platforms[i].position.y = 0
		var mesh: MeshInstance3D = platforms[i].get_child(0)
		mesh.material_override = mat(COLORS[colors[i]])
	for key in crystals:
		var crystal: StaticBody3D = crystals[key]
		if crystal.get_parent() != sockets[locations[key]]:
			crystal.reparent(sockets[locations[key]])
		crystal.position = Vector3(0, 0.5, 0)
		crystal.rotation = Vector3.ZERO
		crystal.show()
		crystal.collision_layer = 2
	player.position = Vector3(0, 0.05, 3)
	player.rotation = Vector3.ZERO
	player.velocity = Vector3.ZERO
	camera.rotation = Vector3(-0.08, 0, 0)
	toast = "搬动晶体，安排一次连锁，搭出通往出口的楼梯。"
