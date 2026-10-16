extends RefCounted
## Local, transactional game rules. The independent demo does not use APK protocols.
signal changed
signal message(text: String)

const ITEMS = {
	"wood": {"name": "松木", "tag": "木", "weight": 0.7, "price": 3, "color": "a8784c", "use": "制作木板，搭建小屋"},
	"fiber": {"name": "亚麻", "tag": "麻", "weight": 0.25, "price": 2, "color": "91a56b", "use": "编织绳索，升级背包"},
	"berry": {"name": "野莓", "tag": "莓", "weight": 0.2, "price": 3, "color": "b65d65", "food": 12, "use": "恢复 12 体力；2 份可做口粮"},
	"board": {"name": "木板", "tag": "板", "weight": 0.8, "price": 9, "color": "bb9259", "use": "扩容背包与建设小屋"},
	"rope": {"name": "麻绳", "tag": "绳", "weight": 0.4, "price": 8, "color": "bdac7b", "use": "扩容背包与建设小屋"},
	"ration": {"name": "莓果口粮", "tag": "粮", "weight": 0.3, "price": 9, "color": "d39558", "food": 32, "use": "恢复 32 体力，适合长途旅行"}
}
const RECIPES = {
	"board": {"wood": 2}, "rope": {"fiber": 3}, "ration": {"berry": 2}
}
const ROUTES = [
	{"name": "松风小径", "subtitle": "穿过松林，去有炊烟的地方", "length": 180.0, "reward": 18, "desc": "松木 · 亚麻 · 野莓  /  约 2 分钟"},
	{"name": "暮色河谷", "subtitle": "沿着河流，寻找下一处营火", "length": 260.0, "reward": 28, "desc": "更多材料 · 更长路途  /  建议带上口粮"}
]
const QUESTS = [
	{"title": "把风景装进行囊", "desc": "在路上成功采集 5 次", "key": "gather", "goal": 5, "reward": 15},
	{"title": "营火旁的手艺", "desc": "在营地制作 3 件物品", "key": "craft", "goal": 3, "reward": 20},
	{"title": "第一段远行", "desc": "走完一条路线，抵达营地", "key": "arrive", "goal": 1, "reward": 20},
	{"title": "更大的世界", "desc": "升级一次旅行背包", "key": "upgrade", "goal": 1, "reward": 25}
]
const STACK = 5
var s: Dictionary
var save_path := "user://journey.json"
var save_ok := true
var recovery_note := ""
var auto_save_time := 0.0

func _init(path: String = "user://journey.json") -> void:
	save_path = path
	fresh()

func fresh() -> void:
	s = {"version": 1, "coins": 25, "stamina": 100.0, "bag_level": 0, "home": false,
		"uid": 1, "bag": [], "storage": [], "trip_id": 0, "travel": false, "route": 0,
		"progress": 0.0, "collected": [], "stats": {"gather": 0, "craft": 0, "arrive": 0, "upgrade": 0},
		"claimed": [], "visits": 0, "name": "远行者"}
	add_to(s, "bag", "berry", 3)
	add_to(s, "bag", "wood", 2)
	add_to(s, "bag", "fiber", 3)

func slots(data: Dictionary = s) -> int:
	return 12 + int(data.bag_level) * 4

func max_weight(data: Dictionary = s) -> float:
	return 12.0 + int(data.bag_level) * 6.0

func weight(data: Dictionary = s) -> float:
	var total := 0.0
	for stack in data.bag:
		total += ITEMS[stack.id].weight * stack.n
	return total

func count(id: String, container: String = "bag", data: Dictionary = s) -> int:
	var total := 0
	for stack in data[container]:
		if stack.id == id:
			total += int(stack.n)
	return total

func add_to(data: Dictionary, container: String, id: String, amount: int) -> void:
	var left := amount
	for stack in data[container]:
		if stack.id == id and stack.n < STACK:
			var part: int = mini(left, STACK - int(stack.n))
			stack.n += part
			left -= part
			if left == 0:
				return
	while left > 0:
		var part := mini(left, STACK)
		data[container].append({"id": id, "n": part, "uid": int(data.uid)})
		data.uid += 1
		left -= part

func remove_from(data: Dictionary, container: String, id: String, amount: int) -> void:
	var left := amount
	for i in range(data[container].size() - 1, -1, -1):
		var stack: Dictionary = data[container][i]
		if stack.id == id:
			var part := mini(left, int(stack.n))
			stack.n -= part
			left -= part
			if stack.n == 0:
				data[container].remove_at(i)
			if left == 0:
				return

func fits(data: Dictionary) -> bool:
	return data.bag.size() <= slots(data) and weight(data) <= max_weight(data) + 0.0001

func reject(text: String) -> bool:
	message.emit(text)
	return false

func commit(next: Dictionary, text: String = "") -> bool:
	var old := s
	s = next
	if not save():
		s = old
		return reject("暂时无法保存，操作未提交。请检查磁盘空间。")
	changed.emit()
	if not text.is_empty():
		message.emit(text)
	return true

func depart(route: int) -> bool:
	if s.travel or route < 0 or route >= ROUTES.size():
		return reject("现在无法出发。")
	if route == 1 and s.stats.arrive < 1:
		return reject("先走完松风小径，河谷就会开放。")
	var next := s.duplicate(true)
	next.travel = true
	next.route = route
	next.progress = 0.0
	next.stamina = 100.0
	next.trip_id += 1
	next.collected = []
	return commit(next, "顺着小径前行。靠近资源后，点击拾取或按 F。")

func resources() -> Array:
	var result := []
	var types := ["wood", "fiber", "berry", "fiber", "wood", "berry"]
	var length: float = ROUTES[int(s.route)].length
	for i in range(int(length / 9.0) - 1):
		result.append({"key": "%d:%d" % [s.trip_id, i], "id": types[i % types.size()], "distance": 9.0 + i * 9.0, "side": -1 if i % 2 == 0 else 1})
	return result

func collect(key: String) -> bool:
	if not s.travel or key in s.collected:
		return reject("这份资源已经收好了。")
	for item in resources():
		if item.key == key:
			if absf(item.distance - float(s.progress)) > 14.0:
				return reject("再靠近一点，就能拾取。")
			var next := s.duplicate(true)
			add_to(next, "bag", item.id, 1)
			if not fits(next):
				return reject("背包已满。可以吃点食物腾出重量，或回营存放。")
			next.collected.append(key)
			next.stats.gather += 1
			return commit(next, "+1 %s · 已放入背包" % ITEMS[item.id].name)
	return reject("这里没有可采集的资源。")

func advance(delta: float, speed: int) -> void:
	if not s.travel or speed == 0 or s.stamina <= 0.0:
		return
	var distance := delta * (2.5 if speed == 1 else 4.3)
	distance = minf(distance, ROUTES[int(s.route)].length - float(s.progress))
	var cost := 0.26 * (1.0 if speed == 1 else 1.32) * (1.0 + weight() / 30.0)
	distance = minf(distance, float(s.stamina) / cost)
	s.progress += distance
	s.stamina = maxf(0.0, float(s.stamina) - distance * cost)
	auto_save_time += delta
	if float(s.progress) >= ROUTES[int(s.route)].length - 0.01:
		arrive()
	elif s.stamina <= 0.001:
		s.stamina = 0.0
		save()
		changed.emit()
		message.emit("走累了。吃点食物，或免费返回营地；物品都会保留。")
	elif auto_save_time >= 3.0:
		auto_save_time = 0.0
		save()

func arrive() -> bool:
	if not s.travel or s.progress < ROUTES[int(s.route)].length - 0.01:
		return false
	var next := s.duplicate(true)
	next.travel = false
	next.stamina = 100.0
	next.coins += ROUTES[int(s.route)].reward
	next.stats.arrive += 1
	next.visits += 1
	return commit(next, "抵达营地！旅途奖励 +%d 叶币，体力已恢复。" % ROUTES[int(s.route)].reward)

func rescue() -> bool:
	if not s.travel:
		return false
	var next := s.duplicate(true)
	next.travel = false
	next.stamina = 100.0
	return commit(next, "已安全回营，所有物品保留。这次不领取到达奖励。")

func eat(id: String) -> bool:
	if not ITEMS.has(id) or not ITEMS[id].has("food") or count(id) == 0:
		return reject("背包里没有这份食物。")
	if s.stamina >= 100.0:
		return reject("体力充足，食物留到路上再吃吧。")
	var next := s.duplicate(true)
	remove_from(next, "bag", id, 1)
	var gain := minf(100.0 - float(next.stamina), ITEMS[id].food)
	next.stamina += gain
	return commit(next, "吃下%s，恢复 %d 体力" % [ITEMS[id].name, gain])

func craft(id: String) -> bool:
	if s.travel or not RECIPES.has(id):
		return reject("回到营火旁才能制作。")
	for input in RECIPES[id]:
		if count(input) < RECIPES[id][input]:
			return reject("材料不足：需要 %d 份%s。" % [RECIPES[id][input], ITEMS[input].name])
	var next := s.duplicate(true)
	for input in RECIPES[id]:
		remove_from(next, "bag", input, RECIPES[id][input])
	add_to(next, "bag", id, 1)
	if not fits(next):
		return reject("没有空间放下成品，材料已保留。")
	next.stats.craft += 1
	return commit(next, "做好了！%s +1" % ITEMS[id].name)

func trade(id: String, buying: bool) -> bool:
	if s.travel or not ITEMS.has(id):
		return false
	var price: int = ITEMS[id].price * (2 if buying else 1)
	var next := s.duplicate(true)
	if buying:
		if next.coins < price:
			return reject("叶币不足，可以先出售一些旅途收获。")
		add_to(next, "bag", id, 1)
		if not fits(next):
			return reject("背包放不下，叶币未扣除。")
		next.coins -= price
	else:
		if count(id) <= 0:
			return reject("背包里没有可出售的%s。" % ITEMS[id].name)
		remove_from(next, "bag", id, 1)
		next.coins += price
	return commit(next, ("买入" if buying else "卖出") + "%s · %s%d 叶币" % [ITEMS[id].name, "−" if buying else "+", price])

func transfer(id: String, storing: bool) -> bool:
	if s.travel or not ITEMS.has(id):
		return false
	var source := "bag" if storing else "storage"
	var dest := "storage" if storing else "bag"
	if count(id, source) < 1:
		return reject("这里还没有这件物品。")
	var next := s.duplicate(true)
	remove_from(next, source, id, 1)
	add_to(next, dest, id, 1)
	if not fits(next):
		return reject("背包放不下，物品仍在仓库里。")
	return commit(next, "%s已%s" % [ITEMS[id].name, "存入仓库" if storing else "放回背包"])

func swap_slots(a: int, b: int) -> bool:
	if a == b or mini(a, b) < 0 or maxi(a, b) >= s.bag.size():
		return false
	var next := s.duplicate(true)
	var old: Dictionary = next.bag[a]
	next.bag[a] = next.bag[b]
	next.bag[b] = old
	return commit(next)

func sort_bag() -> bool:
	var next := s.duplicate(true)
	next.bag.sort_custom(func(a, b): return a.id < b.id)
	return commit(next, "背包已按物品整理。")

func upgrade(house: bool = false) -> bool:
	if s.travel or (s.home if house else s.bag_level >= 1):
		return reject("这项建设已完成，或需要先回营。")
	var wood_cost := 4 if house else 2
	var rope_cost := 2 if house else 3
	var cost := 40 if house else 30
	if count("board") < wood_cost or count("rope") < rope_cost or s.coins < cost:
		return reject("还需要备齐木板、麻绳和叶币。材料在背包内才可使用。")
	var next := s.duplicate(true)
	remove_from(next, "bag", "board", wood_cost)
	remove_from(next, "bag", "rope", rope_cost)
	next.coins -= cost
	if house:
		next.home = true
	else:
		next.bag_level = 1
		next.stats.upgrade += 1
	return commit(next, "小屋建好了！营地有了属于你的灯光。" if house else "背包已升级：16 格 · 18 kg。")

func claim(index: int) -> bool:
	if index < 0 or index >= QUESTS.size() or index in s.claimed:
		return false
	var quest: Dictionary = QUESTS[index]
	if s.stats[quest.key] < quest.goal:
		return reject("旅途还在继续，完成目标后再来领取。")
	var next := s.duplicate(true)
	next.claimed.append(index)
	next.coins += quest.reward
	return commit(next, "旅程目标完成！+%d 叶币" % quest.reward)

func valid(data: Variant) -> bool:
	if not data is Dictionary or data.get("version") != 1:
		return false
	for key in s:
		if not data.has(key):
			return false
	if not data.bag is Array or not data.storage is Array or not data.stats is Dictionary or not data.collected is Array or not data.claimed is Array:
		return false
	for key in ["gather", "craft", "arrive", "upgrade"]:
		if not data.stats.has(key) or not (data.stats[key] is float or data.stats[key] is int):
			return false
		if not is_finite(float(data.stats[key])) or data.stats[key] < 0 or int(data.stats[key]) != data.stats[key]:
			return false
	for key in ["coins", "stamina", "route", "bag_level", "progress", "uid", "trip_id", "visits"]:
		if not (data[key] is float or data[key] is int) or not is_finite(float(data[key])):
			return false
	for key in ["coins", "route", "bag_level", "uid", "trip_id", "visits"]:
		if int(data[key]) != data[key] or data[key] < 0:
			return false
	if data.route < 0 or data.route >= ROUTES.size() or int(data.bag_level) not in [0, 1] or data.coins < 0 or data.stamina < 0 or data.stamina > 100:
		return false
	if data.progress < 0 or data.progress > ROUTES[int(data.route)].length or not data.travel is bool or not data.home is bool:
		return false
	var ids := []
	for container in [data.bag, data.storage]:
		for stack in container:
			if not stack is Dictionary or not stack.has_all(["id", "n", "uid"]) or not ITEMS.has(stack.id):
				return false
			if not (stack.n is float or stack.n is int) or not (stack.uid is float or stack.uid is int):
				return false
			if stack.n < 1 or stack.n > STACK or int(stack.n) != stack.n or stack.uid < 1 or int(stack.uid) != stack.uid or stack.uid in ids or stack.uid >= data.uid:
				return false
			ids.append(stack.uid)
	var claims := []
	for claim_index in data.claimed:
		if not (claim_index is int or claim_index is float):
			return false
		if int(claim_index) != claim_index or claim_index < 0 or claim_index >= QUESTS.size() or claim_index in claims:
			return false
		claims.append(claim_index)
	for resource in data.collected:
		if not resource is String:
			return false
	return fits(data)

func parse_file(path: String) -> Variant:
	var json := JSON.new()
	if json.parse(FileAccess.get_file_as_string(path)) != OK:
		return null
	return json.data

func normalize(data: Dictionary) -> Dictionary:
	# JSON numbers are floats. Array membership distinguishes float and int, so
	# normalize identity/reward fields before querying claimed IDs or stack UIDs.
	for key in ["version", "coins", "route", "bag_level", "uid", "trip_id", "visits"]:
		data[key] = int(data[key])
	for key in data.stats:
		data.stats[key] = int(data.stats[key])
	for container in [data.bag, data.storage]:
		for stack in container:
			stack.n = int(stack.n)
			stack.uid = int(stack.uid)
	for i in range(data.claimed.size()):
		data.claimed[i] = int(data.claimed[i])
	return data

func save() -> bool:
	var temp := save_path + ".tmp"
	var file := FileAccess.open(temp, FileAccess.WRITE)
	if file == null:
		save_ok = false
		return false
	file.store_string(JSON.stringify(s))
	file.flush()
	var error := file.get_error()
	file.close()
	if error != OK:
		save_ok = false
		return false
	# Backup is the previous valid snapshot, never an unchecked corrupt file.
	if FileAccess.file_exists(save_path):
		var previous = parse_file(save_path)
		if valid(previous):
			if DirAccess.copy_absolute(save_path, save_path + ".bak") != OK:
				save_ok = false
				return false
	save_ok = DirAccess.rename_absolute(temp, save_path) == OK
	return save_ok

func load_save() -> bool:
	for path in [save_path, save_path + ".bak"]:
		if FileAccess.file_exists(path):
			var parsed = parse_file(path)
			if valid(parsed):
				s = normalize(parsed)
				if path.ends_with(".bak"):
					recovery_note = "已从备份恢复上一份旅程。"
				return true
	if FileAccess.file_exists(save_path):
		DirAccess.copy_absolute(save_path, save_path + ".damaged")
		recovery_note = "原存档无法读取，已保留副本；现在开始新的旅程。"
	return false
