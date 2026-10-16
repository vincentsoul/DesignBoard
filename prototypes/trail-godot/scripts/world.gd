extends Node3D
## Entire landscape and traveler are original procedural geometry.
var game
var camera: Camera3D
var traveler: Node3D
var left_leg: Node3D
var right_leg: Node3D
var pickups := {}
var camp: Node3D
var cabin: Node3D
var fire: MeshInstance3D
var world_root: Node3D
var time := 0.0
var visual_progress := 0.0
var mat_cache := {}
var lighting: Environment
var sunlight: DirectionalLight3D

func material(hex: String) -> StandardMaterial3D:
	if mat_cache.has(hex):
		return mat_cache[hex]
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(hex).srgb_to_linear()
	mat.roughness = 0.95
	mat_cache[hex] = mat
	return mat

func mesh(parent: Node3D, shape: Mesh, at: Vector3, color: String) -> MeshInstance3D:
	var node := MeshInstance3D.new()
	node.mesh = shape
	node.material_override = material(color)
	node.position = at
	parent.add_child(node)
	return node

func box(parent: Node3D, at: Vector3, size: Vector3, color: String) -> MeshInstance3D:
	var shape := BoxMesh.new()
	shape.size = size
	return mesh(parent, shape, at, color)

func cone(parent: Node3D, at: Vector3, radius: float, height: float, color: String, top: float = 0.0, sides: int = 7) -> MeshInstance3D:
	var shape := CylinderMesh.new()
	shape.top_radius = top
	shape.bottom_radius = radius
	shape.height = height
	shape.radial_segments = sides
	return mesh(parent, shape, at, color)

func stone(parent: Node3D, at: Vector3, size: float, color: String) -> MeshInstance3D:
	var shape := SphereMesh.new()
	shape.radial_segments = 6
	shape.rings = 3
	shape.radius = size
	shape.height = size * 1.5
	return mesh(parent, shape, at, color)

func path_x(distance: float) -> float:
	return sin(distance / 24.0) * 4.0 + sin(distance / 61.0) * 4.0

func point(distance: float) -> Vector3:
	return Vector3(path_x(distance), 0, -distance)

func setup(state) -> void:
	game = state
	var environment := WorldEnvironment.new()
	var env := Environment.new()
	lighting = env
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color("c5d7cb")
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color("d2dfd1")
	env.ambient_light_energy = 0.42
	env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	env.fog_enabled = true
	env.fog_light_color = Color("b7d0c4")
	env.fog_density = 0.006
	environment.environment = env
	add_child(environment)
	var sun := DirectionalLight3D.new()
	sunlight = sun
	sun.rotation_degrees = Vector3(-48, -28, 0)
	sun.light_color = Color("ffe2ad")
	sun.light_energy = 0.72
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 90.0
	add_child(sun)
	camera = Camera3D.new()
	camera.fov = 48.0
	camera.far = 420.0
	add_child(camera)
	camera.make_current()
	build_world()
	build_traveler()
	sync()
	visual_progress = game.s.progress if game.s.travel else 0.0
	update_view(0.01, false, true)

func build_world() -> void:
	world_root = Node3D.new()
	add_child(world_root)
	box(world_root, Vector3(0, -0.55, -140), Vector3(270, 1, 510), "8d9d63")
	# Gently meandering ochre footpath.
	for i in range(-4, 151):
		var p := point(i * 2.0)
		var segment := box(world_root, p + Vector3(0, -0.005, 0), Vector3(5.0, 0.08, 2.4), "d9bf87")
		segment.rotation.y = atan2(path_x(i * 2.0 + 1) - path_x(i * 2.0), -1)
	# River and sandy bank run alongside the trail.
	for i in range(90):
		var z := -i * 4.0
		var x := -19.0 + sin(z / 35.0) * 5
		box(world_root, Vector3(x, -0.01, z), Vector3(11, 0.12, 4.5), "b7b98d")
		box(world_root, Vector3(x, 0.08, z), Vector3(8.4, 0.10, 4.4), "69a8a7")
		box(world_root, Vector3(x + 1.3, 0.14, z - 1), Vector3(4.1, 0.015, 0.09), "acd2c2")
	var rng := RandomNumberGenerator.new()
	rng.seed = 70931
	for i in range(270):
		var z := rng.randf_range(-325, 24)
		var side := -1 if i % 2 == 0 else 1
		var x := path_x(-z) + side * rng.randf_range(7.5, 65)
		if x > -28 and x < -10:
			continue
		if z > -8 and absf(x) < 17:
			continue
		var h := rng.randf_range(4.2, 9.0)
		tree(Vector3(x, 0, z), h, ["456c51", "557b53", "66845a", "78925e"][i % 4])
	for i in range(110):
		var z := rng.randf_range(-300, 15)
		var x := path_x(-z) + (-1 if i % 2 else 1) * rng.randf_range(4, 13)
		stone(world_root, Vector3(x, 0.15, z), rng.randf_range(0.25, 0.65), "929b7c")
		for j in range(3):
			cone(world_root, Vector3(x + j * 0.22, 0.22, z + 1), 0.1, 0.55, "748e50", 0, 3)
	for i in range(18):
		var at := Vector3((-1 if i % 2 else 1) * rng.randf_range(70, 120), 8, -i * 24 + 20)
		cone(world_root, at, rng.randf_range(28, 44), rng.randf_range(40, 68), "8da997", 1, 5)
	camp = Node3D.new()
	world_root.add_child(camp)
	build_camp()

func tree(at: Vector3, height: float, color: String) -> void:
	cone(world_root, at + Vector3(0, height * 0.3, 0), 0.26, height * 0.6, "766342", 0.19)
	cone(world_root, at + Vector3(0, height * 0.50, 0), height * 0.30, height * 0.60, color)
	cone(world_root, at + Vector3(0, height * 0.74, 0), height * 0.23, height * 0.53, color)

func build_camp() -> void:
	cone(camp, Vector3(0, 0.05, -2), 8, 0.12, "bcab7a", 8, 24)
	# Canvas tents, crates, workbench, a fire ring and a future home.
	var tent := cone(camp, Vector3(-5.4, 1.35, -3.6), 2.5, 2.8, "cd8e54", 0, 4)
	tent.rotation.y = PI / 4
	box(camp, Vector3(-5.4, 0.9, -1.87), Vector3(1.15, 1.8, 0.06), "514f38")
	cone(camp, Vector3(-5.4, 2.6, -3.6), 0.07, 1.5, "695b43", 0.07)
	box(camp, Vector3(5, 1.0, -3), Vector3(3.2, 0.25, 1.25), "99714b")
	for x in [3.9, 6.1]:
		box(camp, Vector3(x, 0.5, -3), Vector3(0.18, 1, 0.8), "715f44")
	box(camp, Vector3(5.8, 0.6, -0.5), Vector3(1.6, 1.2, 1.2), "b18a59")
	box(camp, Vector3(5.8, 1.2, -0.5), Vector3(1.7, 0.1, 1.3), "745c40")
	for i in range(9):
		var a := i * TAU / 9.0
		stone(camp, Vector3(cos(a) * 1.2, 0.2, sin(a) * 1.2 - 2), 0.3, "737a68")
	var log1 := box(camp, Vector3(0, 0.25, -2), Vector3(1.4, 0.3, 0.25), "685040")
	log1.rotation.y = 0.6
	var log2 := box(camp, Vector3(0, 0.35, -2), Vector3(1.4, 0.3, 0.25), "685040")
	log2.rotation.y = -0.6
	fire = cone(camp, Vector3(0, 0.9, -2), 0.55, 1.3, "e8a04b", 0, 5)
	cone(camp, Vector3(0, 0.65, -1.8), 0.3, 0.9, "f8d482", 0, 5)
	box(camp, Vector3(-2.1, 0.35, 0.4), Vector3(2.5, 0.6, 0.6), "7c6748")
	cabin = Node3D.new()
	camp.add_child(cabin)
	box(cabin, Vector3(5.4, 1.6, -9), Vector3(4.5, 3.2, 3.7), "ac8759")
	var roof := cone(cabin, Vector3(5.4, 3.6, -9), 3.7, 2.3, "5e7770", 0, 4)
	roof.rotation.y = PI / 4
	box(cabin, Vector3(5, 0.95, -7.1), Vector3(1.1, 1.9, 0.05), "66563c")
	box(cabin, Vector3(6.5, 1.8, -7.1), Vector3(0.8, 0.8, 0.05), "f1cf83")

func build_traveler() -> void:
	traveler = Node3D.new()
	add_child(traveler)
	left_leg = Node3D.new()
	left_leg.position = Vector3(-0.22, 0.8, 0)
	traveler.add_child(left_leg)
	box(left_leg, Vector3(0, -0.35, 0), Vector3(0.3, 0.7, 0.32), "3e5d59")
	box(left_leg, Vector3(0, -0.67, -0.1), Vector3(0.33, 0.18, 0.55), "574b3b")
	right_leg = Node3D.new()
	right_leg.position = Vector3(0.22, 0.8, 0)
	traveler.add_child(right_leg)
	box(right_leg, Vector3(0, -0.35, 0), Vector3(0.3, 0.7, 0.32), "3e5d59")
	box(right_leg, Vector3(0, -0.67, -0.1), Vector3(0.33, 0.18, 0.55), "574b3b")
	cone(traveler, Vector3(0, 1.22, 0), 0.65, 0.95, "cf9152", 0.3, 6)
	stone(traveler, Vector3(0, 1.93, 0), 0.34, "d7b48b")
	cone(traveler, Vector3(0, 2.15, 0), 0.72, 0.10, "786a44", 0.72, 10)
	cone(traveler, Vector3(0, 2.33, 0), 0.34, 0.33, "938257", 0.27, 8)
	box(traveler, Vector3(0, 1.25, 0.47), Vector3(0.65, 0.76, 0.37), "657e6a")
	box(traveler, Vector3(0, 1.73, 0.47), Vector3(0.75, 0.22, 0.26), "cfbd8c")
	cone(traveler, Vector3(0.72, 0.85, -0.2), 0.045, 1.8, "715d3d", 0.04)

func sync() -> void:
	var evening: bool = game.s.travel and game.s.route == 1
	lighting.background_color = Color("d3b3a0") if evening else Color("c5d7cb")
	lighting.fog_light_color = Color("c2ad96") if evening else Color("b7d0c4")
	sunlight.light_color = Color("ffd09a") if evening else Color("ffe2ad")
	sunlight.rotation_degrees.x = -28 if evening else -48
	for node in pickups.values():
		node.queue_free()
	pickups.clear()
	cabin.visible = game.s.home
	if not game.s.travel:
		camp.position = Vector3.ZERO
		return
	camp.position = point(game.ROUTES[int(game.s.route)].length)
	for item in game.resources():
		if item.key in game.s.collected:
			continue
		var node := Node3D.new()
		node.position = point(item.distance) + Vector3(item.side * 3.2, 0.1, 0)
		add_child(node)
		if item.id == "wood":
			var log_node := cone(node, Vector3(0, 0.4, 0), 0.34, 1.4, "9a7148", 0.34)
			log_node.rotation.z = PI / 2
			cone(node, Vector3(0.72, 0.4, 0), 0.26, 0.02, "d0b47d", 0.26).rotation.z = PI / 2
		elif item.id == "fiber":
			for j in range(5):
				cone(node, Vector3((j - 2) * 0.18, 0.5, 0), 0.15, 1.1, "c8c681", 0, 4)
		else:
			stone(node, Vector3(0, 0.4, 0), 0.65, "60805b")
			for j in range(3):
				stone(node, Vector3((j - 1) * 0.34, 0.85, 0.2), 0.17, "ba646a")
		var ring := TorusMesh.new()
		ring.inner_radius = 0.72
		ring.outer_radius = 0.78
		ring.rings = 20
		ring.ring_segments = 6
		mesh(node, ring, Vector3(0, 0.1, 0), "f2e3af")
		pickups[item.key] = node

func update_view(delta: float, walking: bool, instant := false) -> void:
	time += delta
	var target: float = game.s.progress if game.s.travel else 0.0
	visual_progress = target if instant else lerpf(visual_progress, target, minf(1.0, delta * 8.0))
	traveler.position = point(visual_progress) + Vector3(0, 0.04, 1.8 if not game.s.travel else 0)
	traveler.rotation.y = -atan2(path_x(visual_progress + 1) - path_x(visual_progress), 1) if game.s.travel else 0.28
	left_leg.rotation.x = sin(time * 9) * 0.65 if walking else 0
	right_leg.rotation.x = -left_leg.rotation.x
	traveler.position.y += absf(sin(time * 9)) * 0.06 if walking else sin(time * 1.8) * 0.025
	var focus := traveler.position + Vector3(0, 0.9, -6)
	camera.position = traveler.position + Vector3(11.5, 13.5, 19)
	camera.look_at(focus)
	fire.scale = Vector3.ONE * (1.0 + sin(time * 7) * 0.08)
	for key in pickups:
		var node: Node3D = pickups[key]
		var distance := absf(-node.position.z - float(game.s.progress))
		node.visible = distance < 64
		node.scale = Vector3.ONE * (1.0 + sin(time * 3 + node.position.z) * 0.06 if distance < 14 else 1.0)

func pick(screen: Vector2) -> String:
	var nearest := ""
	var distance := 48.0
	for key in pickups:
		var node: Node3D = pickups[key]
		if not node.visible or absf(-node.position.z - float(game.s.progress)) > 14:
			continue
		var pixel := camera.unproject_position(node.position + Vector3(0, 0.5, 0))
		var candidate := pixel.distance_to(screen)
		if candidate < distance:
			distance = candidate
			nearest = key
	return nearest
