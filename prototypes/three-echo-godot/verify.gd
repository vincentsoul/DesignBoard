extends SceneTree

var game: Node3D
var failures: Array[String] = []

func _initialize() -> void:
	call_deferred("run")

func check(value: bool, message: String) -> void:
	if not value:
		failures.append(message)
		push_error(message)

func settle(frames: int = 3) -> void:
	for i in range(frames):
		await physics_frame

func aim_at(position: Vector3, target: Vector3) -> void:
	game.player.position = position
	game.player.velocity = Vector3.ZERO
	game.camera.look_at(target, Vector3.UP)
	await settle()

func press(key: int, down: bool = true) -> void:
	var event := InputEventKey.new()
	event.keycode = key
	event.physical_keycode = key
	event.pressed = down
	Input.parse_input_event(event)

func run() -> void:
	game = load("res://main.tscn").instantiate()
	root.add_child(game)
	await settle()
	check(game.menu.visible and not game.running, "Starts on title screen")
	game.resume_game()
	await aim_at(Vector3(0, 0.05, 3), game.crystals.green.global_position)
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.held == "", "Cannot pick up distant crystal")
	await aim_at(Vector3(0, 0.05, -3.6), game.crystals.green.global_position)
	check(game.prompt.text == "按 E 拿起", "Pickup hint and physics ray")
	check(game.highlighted != null, "Pickup highlight")
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.held == "green", "E input picks up green crystal")
	await aim_at(Vector3(0, 0.05, -3.6), game.sockets.B_none.global_position)
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.held == "green", "Incompatible socket rejects placement")
	await aim_at(Vector3(0, 0.05, -6.6), game.sockets.A_green.global_position)
	check(game.prompt.text == "按 E 放置", "Placement hint")
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.locations.green == "A_green" and game.held == "", "Place green on A")
	await aim_at(Vector3(-0.9, 0.05, 2), game.crystals.none.global_position)
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.held == "none", "Pick up null")
	await aim_at(Vector3(0, 0.05, -3.6), game.sockets.B_none.global_position)
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.locations.none == "B_none", "Place null on B")
	await aim_at(Vector3(0, 0.05, 3), Vector3(0, 10, -10))
	game.shoot()
	check(not game.fired, "Miss does not trigger chain")
	await aim_at(Vector3(0, 0.05, 3), game.crystals.red.global_position)
	game.shoot()
	check(game.fired, "Aimed shot starts chain")
	await create_timer(0.2).timeout
	game.show_menu("暂停", "测试")
	var pause_height: float = game.platforms[2].position.y
	await create_timer(0.2).timeout
	check(is_equal_approx(pause_height, game.platforms[2].position.y), "Pause freezes platform tween")
	game.resume_game()
	await create_timer(4.5).timeout
	check(game.heights == [1, 2, 3], "Three waves produce 1/2/3")
	check(game.colors == ["none", "none", "none"], "All panels clear")
	check(not game.crystals.none.visible, "Null consumed")
	# Walk the actual physical route, including all three one-meter steps.
	game.player.position = Vector3(0, 0.05, 3)
	game.player.rotation = Vector3.ZERO
	game.camera.rotation = Vector3.ZERO
	press(KEY_W)
	await create_timer(4.8).timeout
	press(KEY_W, false)
	check(game.completed, "Physical player walks up all steps and reaches exit")
	game.reset_level()
	game.resume_game()
	check(not game.fired and game.held == "" and game.heights == [0, 0, 0], "Reset restores state")
	await aim_at(Vector3(0, 0.05, 3), game.crystals.red.global_position)
	game.shoot()
	await create_timer(0.15).timeout
	check(game.heights == [0, 0, 1], "Wrong arrangement only raises A once")
	# Reset during an active chain must invalidate old async work.
	game.reset_level()
	await create_timer(1.8).timeout
	check(game.heights == [0, 0, 0] and not game.fired, "Mid-chain reset stays reset")
	print("THREE_ECHO_VERIFY ", "PASS" if failures.is_empty() else failures)
	quit(0 if failures.is_empty() else 1)
