extends SceneTree
const State = preload("res://scripts/state.gd")
var passed := 0
var failed := 0
var game
var path := "user://qa-state.json"

func check(condition: bool, title: String) -> void:
	if condition:
		passed += 1
		print("PASS: ", title)
	else:
		failed += 1
		push_error("FAIL: " + title)

func reset() -> void:
	game = State.new(path)

func _initialize() -> void:
	reset()
	check(game.valid(game.s), "New player has a valid, bounded inventory")
	var original: String = JSON.stringify(game.s)
	check(not game.eat("berry") and JSON.stringify(game.s) == original, "Full stamina does not consume food")
	check(not game.depart(1) and not game.s.travel, "Second route is locked before first arrival")
	check(game.depart(0), "Start first journey")
	var resource: Dictionary = game.resources()[0]
	check(game.collect(resource.key), "Collect reachable resource")
	var after: String = JSON.stringify(game.s)
	check(not game.collect(resource.key) and JSON.stringify(game.s) == after, "Resource cannot be collected twice")
	check(not game.collect(game.resources()[10].key), "Distant resource cannot be collected")
	var progress_before: float = game.s.progress
	var energy_before: float = game.s.stamina
	game.advance(5, 0)
	check(game.s.progress == progress_before and game.s.stamina == energy_before, "Paused walking consumes no distance or stamina")
	game.advance(5, 1)
	check(game.s.progress > progress_before and game.s.stamina < energy_before, "Walking advances and consumes stamina")
	game.s.stamina = 95.0
	var berries: int = game.count("berry")
	check(game.eat("berry") and game.s.stamina == 100.0 and game.count("berry") == berries - 1, "Food consumes one and caps stamina at 100")
	game.s.stamina = 0.01
	game.advance(1000, 2)
	check(game.s.stamina == 0.0 and game.s.travel, "Exhaustion stops instead of completing route")
	var inventory: String = JSON.stringify(game.s.bag)
	var money: int = game.s.coins
	check(game.rescue() and JSON.stringify(game.s.bag) == inventory and game.s.coins == money, "Free rescue retains inventory with no arrival reward")
	check(game.s.stamina == 100.0 and game.s.stats.arrive == 0, "Rescue restores stamina without counting arrival")
	reset()
	game.s.bag = []
	game.add_to(game.s, "bag", "wood", 17)
	game.depart(0)
	original = JSON.stringify(game.s)
	check(not game.collect(game.resources()[0].key) and JSON.stringify(game.s) == original, "Overweight pickup leaves resource and inventory unchanged")
	reset()
	original = JSON.stringify(game.s)
	check(game.craft("ration"), "Valid food recipe is available")
	check(game.count("berry") == 1 and game.count("ration") == 1 and game.s.stats.craft == 1, "Craft consumes exact inputs and creates one output")
	original = JSON.stringify(game.s)
	check(not game.craft("ration") and JSON.stringify(game.s) == original, "Insufficient recipe inputs cause no partial deduction")
	# 12 occupied stacks with room in weight, none freed by the input deduction.
	game.s.bag = []
	for i in range(10):
		game.s.bag.append({"id": "fiber", "n": 1, "uid": 100 + i})
	game.s.bag.append({"id": "wood", "n": 3, "uid": 111})
	game.s.bag.append({"id": "berry", "n": 1, "uid": 112})
	game.s.uid = 113
	original = JSON.stringify(game.s)
	check(not game.craft("board") and JSON.stringify(game.s) == original, "No output slot means crafting leaves materials untouched")
	reset()
	game.s.coins = 0
	original = JSON.stringify(game.s)
	check(not game.trade("rope", true) and JSON.stringify(game.s) == original, "Unaffordable purchase changes nothing")
	game.s.coins = 100
	check(game.trade("board", true) and game.count("board") == 1 and game.s.coins == 82, "NPC purchase uses displayed double buy price")
	check(game.trade("board", false) and game.count("board") == 0 and game.s.coins == 91, "NPC sale pays one fixed price")
	check(not game.trade("board", false) and game.s.coins == 91, "Cannot sell an item twice")
	var total: int = game.count("wood")
	check(game.transfer("wood", true) and game.count("wood") + game.count("wood", "storage") == total, "Warehouse deposit conserves quantity")
	check(game.transfer("wood", false) and game.count("wood") == total, "Warehouse withdrawal conserves quantity")
	game.s.bag = []
	game.add_to(game.s, "bag", "wood", 17)
	game.add_to(game.s, "storage", "board", 1)
	original = JSON.stringify(game.s)
	check(not game.transfer("board", false) and JSON.stringify(game.s) == original, "Full bag withdrawal preserves both containers")
	check(not game.trade("board", true) and JSON.stringify(game.s) == original, "Full bag purchase preserves money")
	reset()
	var uid_a: int = game.s.bag[0].uid
	var uid_b: int = game.s.bag[1].uid
	check(game.swap_slots(0, 1) and game.s.bag[0].uid == uid_b and game.s.bag[1].uid == uid_a, "Drag swap preserves stack identities")
	reset()
	game.depart(0)
	game.s.progress = game.ROUTES[0].length
	money = game.s.coins
	check(game.arrive() and game.s.coins == money + 18 and game.s.stats.arrive == 1, "Arrival pays reward and unlocks next route")
	check(not game.arrive() and game.s.coins == money + 18, "Arrival cannot pay twice")
	check(game.claim(2) and game.s.coins == money + 38, "Completed quest pays exactly one reward")
	check(not game.claim(2) and game.s.coins == money + 38, "Quest reward cannot be reclaimed")
	var claimed_reload = State.new(path)
	check(claimed_reload.load_save() and not claimed_reload.claim(2) and claimed_reload.s.coins == game.s.coins, "Quest remains claimed after JSON reload")
	check(game.depart(1), "Completed first journey unlocks river route")
	reset()
	game.s.bag = []
	game.s.coins = 120
	game.add_to(game.s, "bag", "board", 6)
	game.add_to(game.s, "bag", "rope", 5)
	check(game.upgrade() and game.slots() == 16 and game.max_weight() == 18 and game.s.coins == 90, "Bag upgrade changes capacity and charges once")
	check(not game.upgrade() and game.s.coins == 90, "Bag upgrade cannot charge twice")
	check(game.upgrade(true) and game.s.home and game.s.coins == 50, "Home construction consumes remaining resources")
	check(not game.upgrade(true) and game.s.coins == 50, "Home construction cannot charge twice")
	reset()
	game.depart(0)
	game.collect(game.resources()[0].key)
	game.advance(5, 1)
	game.save()
	var reloaded = State.new(path)
	check(reloaded.load_save() and JSON.stringify(reloaded.s) == JSON.stringify(game.s), "Save/reload retains identity, journey, stamina and collected set")
	check(not reloaded.collect(reloaded.resources()[0].key), "Reload does not respawn a collected resource")
	game.save()
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string("{broken")
	file.close()
	var recovered = State.new(path)
	check(recovered.load_save() and recovered.s.trip_id == game.s.trip_id and not recovered.recovery_note.is_empty(), "Corrupt primary save recovers validated backup")
	var invalid: Dictionary = recovered.s.duplicate(true)
	invalid.bag[0].n = -1
	check(not recovered.valid(invalid), "Malformed negative quantities are rejected")
	invalid = recovered.s.duplicate(true)
	invalid.bag[1].uid = invalid.bag[0].uid
	check(not recovered.valid(invalid), "Duplicate inventory UIDs are rejected")
	reset()
	game.save_path = "user://missing-qa-directory/never-written.json"
	original = JSON.stringify(game.s)
	check(not game.craft("rope") and JSON.stringify(game.s) == original, "Disk-write failure rolls back the whole transaction")
	# Complete a real simulated loop using only normal public commands.
	reset()
	game.depart(0)
	for step in range(1200):
		if not game.s.travel:
			break
		for item in game.resources():
			if item.key not in game.s.collected and absf(item.distance - float(game.s.progress)) < 5 and game.weight() < 10:
				game.collect(item.key)
		if game.s.stamina < 30:
			game.eat("berry")
		game.advance(0.2, 2)
	check(not game.s.travel and game.s.stats.arrive == 1 and game.s.stats.gather >= 5, "Playable journey completes with gathering and food use")
	check(game.craft("board") and game.trade("board", false) and game.transfer("fiber", true), "Journey yields materials usable for crafting, selling and storage")
	check(game.valid(game.s), "End-to-end loop ends with a valid inventory")
	for suffix in ["", ".bak", ".tmp", ".damaged"]:
		if FileAccess.file_exists(path + suffix):
			DirAccess.remove_absolute(path + suffix)
	print("RESULT: %d passed, %d failed" % [passed, failed])
	quit(1 if failed else 0)
