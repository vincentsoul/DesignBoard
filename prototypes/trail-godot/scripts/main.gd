extends Node3D
const State = preload("res://scripts/state.gd")
const Landscape = preload("res://scripts/world.gd")
const HUD = preload("res://scripts/hud.gd")
var game
var world
var hud
var speed := 1
var focused := true
var last_travel := false
var tone_player: AudioStreamPlayer
var tone_clock := 0.0
var smoke_mode := false

func _ready() -> void:
	game = State.new()
	smoke_mode = "--smoke" in OS.get_cmdline_user_args()
	if smoke_mode:
		game.save_path = "user://smoke-only.json"
	elif "--qa-isolated" in OS.get_cmdline_user_args():
		game.save_path = "user://qa-isolated.json"
		DisplayServer.window_set_title("Windtrail · Interaction Check")
	elif "--qa" in OS.get_cmdline_user_args():
		game.save_path = "user://qa-interactive.json"
	elif "--qa-resume" in OS.get_cmdline_user_args():
		game.save_path = "user://qa-interactive.json"
		game.load_save()
	else:
		game.load_save()
	last_travel = game.s.travel
	world = Landscape.new()
	add_child(world)
	world.setup(game)
	hud = HUD.new()
	add_child(hud)
	hud.setup(game, self)
	game.changed.connect(on_change)
	get_tree().auto_accept_quit = false
	tone_player = AudioStreamPlayer.new()
	add_child(tone_player)
	game.message.connect(func(_text): play_note())
	if smoke_mode:
		smoke_test.call_deferred()

func paused() -> bool:
	return not focused or is_instance_valid(hud.modal)

func _process(delta: float) -> void:
	if game == null:
		return
	if not paused():
		game.advance(minf(delta, 0.1), speed)
	world.update_view(delta, game.s.travel and speed > 0 and not paused() and game.s.stamina > 0)
	hud.tick(delta)
	tone_clock = maxf(0.0, tone_clock - delta)

func _notification(what: int) -> void:
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT:
		focused = false
		if game != null:
			game.save()
	elif what == NOTIFICATION_APPLICATION_FOCUS_IN:
		focused = true
	elif what == NOTIFICATION_WM_CLOSE_REQUEST:
		if game != null and not game.save():
			hud.toast("存档失败。请检查磁盘空间后再关闭。")
			return
		get_tree().quit()

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_ESCAPE:
			hud.close_modal()
		elif not paused():
			match event.keycode:
				KEY_SPACE: set_speed(1 if speed == 0 else 0)
				KEY_1: set_speed(1)
				KEY_2: set_speed(2)
				KEY_F: collect_nearest()
				KEY_E: eat_best()
				KEY_J: hud.open_modal("journal")
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT and not paused():
		var key: String = world.pick(event.position)
		if not key.is_empty():
			game.collect(key)

func set_speed(value: int) -> void:
	speed = value
	hud.refresh()

func depart(route: int) -> void:
	if game.depart(route):
		speed = 1
		hud.close_modal()
		hud.refresh()
		world.update_view(0.01, false, true)

func rescue() -> void:
	if game.rescue():
		hud.close_modal()
		hud.refresh()

func collect_nearest() -> void:
	var best := ""
	var distance := 15.0
	for item in game.resources():
		var d := absf(item.distance - float(game.s.progress))
		if item.key not in game.s.collected and d < distance:
			best = item.key
			distance = d
	if best.is_empty() or not game.s.travel:
		hud.toast("再向前走走，附近还没有可拾取的资源。")
	else:
		game.collect(best)

func eat_best() -> void:
	if game.count("ration") > 0 and game.s.stamina <= 68:
		game.eat("ration")
	elif game.count("berry") > 0:
		game.eat("berry")
	elif game.count("ration") > 0:
		game.eat("ration")
	else:
		hud.toast("没有食物了。可以免费回营恢复体力。")

func on_change() -> void:
	world.sync()
	if last_travel != bool(game.s.travel):
		world.update_view(0.01, false, true)
	last_travel = game.s.travel

func play_note() -> void:
	if tone_clock > 0:
		return
	tone_clock = 0.12
	var stream := AudioStreamWAV.new()
	stream.format = AudioStreamWAV.FORMAT_16_BITS
	stream.mix_rate = 22050
	var bytes := PackedByteArray()
	bytes.resize(4400 * 2)
	for i in range(4400):
		var envelope := pow(1.0 - i / 4400.0, 3)
		var sample := int(sin(i * TAU * 660.0 / 22050.0) * envelope * 2500)
		bytes.encode_s16(i * 2, sample)
	stream.data = bytes
	tone_player.stream = stream
	tone_player.play()

func smoke_test() -> void:
	# Dedicated QA profile; never touches the user's normal journey.
	await get_tree().process_frame
	depart(0)
	set_speed(0)
	game.s.progress = 48.0
	game.s.stamina = 73.0
	world.update_view(0.01, false, true)
	game.collect(game.resources()[4].key)
	hud.toast("松风小径 · 在路上，收集属于你的故事。")
	await get_tree().create_timer(1.0).timeout
	get_viewport().get_texture().get_image().save_png("user://smoke-travel.png")
	hud.open_modal("journal")
	await get_tree().process_frame
	get_viewport().get_texture().get_image().save_png("user://smoke-journal.png")
	hud.close_modal()
	game.s.progress = game.ROUTES[0].length
	game.arrive()
	game.craft("rope")
	hud.open_modal("craft")
	await get_tree().create_timer(0.3).timeout
	get_viewport().get_texture().get_image().save_png("user://smoke-craft.png")
	hud.close_modal()
	await get_tree().process_frame
	get_viewport().get_texture().get_image().save_png("user://smoke-camp.png")
	print("SMOKE PASS: scene, travel, collection, arrival, crafting and modal rendering")
	get_tree().quit()
