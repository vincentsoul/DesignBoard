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
	Input.flush_buffered_events()

func run() -> void:
	game = load("res://main.tscn").instantiate()
	root.add_child(game)
	await settle()
	check(game.menu.visible and not game.running, "Starts on title screen")
	check(game.guide_step == 1 and game.guide_marker.visible, "Guide starts at green crystal")
	game.show_rules()
	check(game.rules_open and not game.running and game.rules_previous.disabled, "Rules pause at first diagram")
	game.rules_next.pressed.emit()
	check(game.rules_page == 1 and game.rules_labels[2].text.contains("1 米"), "Rule navigation shows first wave")
	game.rules_next.pressed.emit()
	check(game.rules_page == 2 and game.rules_labels[1].text.contains("1 米") and game.rules_labels[2].text.contains("2 米"), "Second wave diagram heights")
	game.rules_next.pressed.emit()
	check(game.rules_page == 3 and game.rules_next.disabled and game.rules_labels[0].text.contains("1 米"), "Final diagram and page bound")
	check(game.locations.green == "B_green" and game.heights == [0, 0, 0], "Reading explanation leaves puzzle state intact")
	game.resume_game()
	await aim_at(Vector3(0, 0.05, 3), game.crystals.green.global_position)
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.held == "", "Cannot pick up distant crystal")
	await aim_at(Vector3(0, 0.05, -3.6), game.crystals.green.global_position)
	check(game.prompt.text.contains("按 E 拿起"), "Pickup hint and physics ray")
	check(game.highlighted != null, "Pickup highlight")
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.held == "green", "E input picks up green crystal")
	game.show_rules(2)
	game.resume_game()
	check(game.held == "green" and not game.rules_open, "Rules preserve carried crystal on resume")
	check(game.guide_step == 2 and game.guide_data()[3] == game.sockets.A_green, "Guide points held green to A")
	await aim_at(Vector3(0, 0.05, -3.6), game.sockets.B_none.global_position)
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.held == "" and game.locations.green == "B_none", "Green accepts former white socket")
	await aim_at(Vector3(0, 0.05, -3.6), game.crystals.green.global_position)
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.held == "green", "Green can be picked back up")
	await aim_at(Vector3(0, 0.05, -6.6), game.sockets.A_green.global_position)
	check(game.prompt.text == "按 E 放置", "Placement hint")
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.locations.green == "A_green" and game.held == "", "Place green on A")
	check(game.guide_step == 3 and game.guide_data()[3] == game.crystals.none, "Guide advances to null at entrance")
	await aim_at(Vector3(-0.9, 0.05, 2), game.crystals.none.global_position)
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.held == "none", "Pick up null")
	check(game.guide_step == 4 and game.guide_data()[3].get_meta("index") == 1, "Guide points held null to B")
	await aim_at(Vector3(0, 0.05, -3.6), game.sockets.B_none.global_position)
	press(KEY_E)
	await settle()
	press(KEY_E, false)
	check(game.locations.none == "B_none", "Place null on B")
	check(game.guide_step == 5, "Guide asks player to return to entrance")
	await aim_at(Vector3(0, 0.05, 3), Vector3(0, 10, -10))
	check(game.guide_step == 6, "Guide advances to aimed shot at entrance")
	game.shoot()
	check(not game.fired, "Miss does not trigger chain")
	await aim_at(Vector3(0, 0.05, 3), game.crystals.red.global_position)
	game.shoot()
	check(game.fired, "Aimed shot starts chain")
	await create_timer(0.2).timeout
	check(game.guide_step == 7 and not game.guide_marker.visible, "Guide observes active chain")
	game.show_menu("暂停", "测试")
	var pause_height: float = game.platforms[2].position.y
	await create_timer(0.2).timeout
	check(is_equal_approx(pause_height, game.platforms[2].position.y), "Pause freezes platform tween")
	game.resume_game()
	await create_timer(4.5).timeout
	check(game.heights == [1, 2, 3], "Three waves produce 1/2/3")
	check(game.guide_step == 8 and game.guide_data()[3] == game.exit_target, "Guide leads to exit after full chain")
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
	check(game.guide_data()[1] == "已完成" and not game.guide_marker.visible, "Guide completes on actual exit")
	game.reset_level()
	game.resume_game()
	check(not game.fired and game.held == "" and game.heights == [0, 0, 0], "Reset restores state")
	check(game.guide_step == 1, "Reset restarts guide")
	await aim_at(Vector3(0, 0.05, 3), game.crystals.red.global_position)
	game.shoot()
	await create_timer(0.15).timeout
	check(game.heights == [0, 0, 1], "Wrong arrangement only raises A once")
	# Reset during an active chain must invalidate old async work.
	game.reset_level()
	await create_timer(1.8).timeout
	check(game.heights == [0, 0, 0] and not game.fired, "Mid-chain reset stays reset")
	# Every crystal must pass through actual pickup/place rays on every platform.
	for key in ["red", "green", "none"]:
		for slot in ["C_right", "B_extra", "A_extra"]:
			game.reset_level()
			game.resume_game()
			await move_crystal(key, slot)
			check(game.locations[key] == slot and game.held == "", "%s can be placed on %s" % [key, slot])
	game.reset_level()
	game.resume_game()
	await move_crystal("green", "C_right")
	await aim_at(Vector3(0, 0.05, -6.5), game.crystals.red.global_position)
	game.interact()
	await aim_at(Vector3(0, 0.05, -0.5), game.crystals.green.global_position)
	game.interact()
	check(game.held == "red" and game.locations.green == "C_right", "Occupied slot cannot replace or delete another crystal")
	# All three colors can start a chain through shooting, including null first.
	for key in ["red", "green", "none"]:
		game.reset_level()
		game.resume_game()
		if key == "none":
			await move_crystal(key, "C_right")
		await aim_at(Vector3(0, 0.05, game.crystals[key].global_position.z + 1), game.crystals[key].global_position)
		game.shoot()
		await create_timer(1.6).timeout
		check(game.locations[key] == "consumed" and not game.chain_active, "%s starts and completes a chain" % key)
		var expected := [0, 0, 1] if key == "red" else ([0, 1, 0] if key == "green" else [1, 0, 0])
		check(game.heights == expected, "%s changes its own source panel" % key)
	# A second independent shot uses colors resulting from the first shot.
	game.reset_level()
	game.resume_game()
	await aim_at(Vector3(0, 0.05, -6.5), game.crystals.red.global_position)
	game.shoot()
	await create_timer(1.6).timeout
	await move_crystal("none", "C_right")
	check(game.locations.none == "C_right", "Remaining crystals can be moved after firing")
	await aim_at(Vector3(0, 0.05, -3.6), game.crystals.green.global_position)
	game.shoot()
	await create_timer(1.6).timeout
	check(game.heights == [0, 1, 2], "Second shot merges current red panels")
	# Same-color and neutral-ground destruction consume only the shot crystal.
	for slot in ["B_extra", "stand"]:
		game.reset_level()
		game.resume_game()
		if slot == "stand":
			await move_crystal("none", "C_right")
		await move_crystal("red", slot)
		await aim_at(Vector3(0, 0.05, game.crystals.red.global_position.z + 1), game.crystals.red.global_position)
		game.shoot()
		await settle()
		check(game.locations.red == "consumed" and game.heights == [0, 0, 0], "Same/neutral color is a safe no-op")
	print("THREE_ECHO_VERIFY ", "PASS" if failures.is_empty() else failures)
	quit(0 if failures.is_empty() else 1)

func move_crystal(key: String, slot: String) -> void:
	var crystal: Node3D = game.crystals[key]
	await aim_at(Vector3(0, 0.05, crystal.global_position.z + 1), crystal.global_position)
	game.interact()
	check(game.held == key, "Pickup " + key)
	var target: Node3D = game.sockets[slot]
	await aim_at(Vector3(0, 0.05, target.global_position.z + 1), target.global_position)
	game.interact()
