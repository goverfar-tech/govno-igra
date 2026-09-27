class_name WorldGen
extends Node3D
## Детерминированное рассеивание объектов по ландшафту:
## роща добываемых деревьев, жилы камня, кусты ягод, подборы у спауна.
## Зависит от Terrain (должен быть сыном того же мира и идти раньше в дереве).

const TreeScene: PackedScene = preload("res://scenes/interaction/resource_node_tree.tscn")
const RockScene: PackedScene = preload("res://scenes/interaction/resource_node_rock.tscn")
const BushScene: PackedScene = preload("res://scenes/interaction/berry_bush.tscn")
const PickupScene: PackedScene = preload("res://scenes/interaction/pickup_item.tscn")
const AnimalScene: PackedScene = preload("res://scenes/world/animal.tscn")
const RuinScene: PackedScene = preload("res://scenes/buildings/ruin.tscn")
const PoleScene: PackedScene = preload("res://scenes/buildings/pole.tscn")
const CrateScene: PackedScene = preload("res://scenes/buildings/crate.tscn")
const BarrelScene: PackedScene = preload("res://scenes/buildings/barrel.tscn")
const TireScene: PackedScene = preload("res://scenes/buildings/tire.tscn")
const RoadScene: PackedScene = preload("res://scenes/buildings/road_strip.tscn")
const GrassScene: PackedScene = preload("res://assets/models/patch-grass.glb")
const GrassLargeScene: PackedScene = preload("res://assets/models/patch-grass-large.glb")
const GrassSmallScene: PackedScene = preload("res://assets/models/grass.glb")

const Stone: ItemData = preload("res://resources/items/stone.tres")
const Berry: ItemData = preload("res://resources/items/berry.tres")
const Flask: ItemData = preload("res://resources/items/flask.tres")

@export var scatter_seed := 4242
@export var grove_center := Vector2(-45.0, -40.0)
@export var grove_radius := 25.0
@export var grove_trees := 25
@export var lone_trees := 10
@export var rocks_count := 10
@export var bushes_count := 15
@export var animals_count := 6
@export var ruins_count := 4
@export var poles_count := 6
@export var crates_per_ruin := 2

var _terrain: Terrain
var _occupied: Array[Vector2] = []

# Для сохранений: сгенерированным объектам присваиваем стабильные имена
var _spawned_nodes: Array[Node] = []
var _spawned_names: Array[String] = []
var _spawn_counter := 0


func _ready() -> void:
	_terrain = get_parent().get_node_or_null("Terrain") as Terrain
	if _terrain == null:
		push_error("WorldGen: не найден Terrain")
		return
	_scatter()
	_align_spawn_marker()
	_align_lake()


func _align_lake() -> void:
	# вода чуть ниже края чаши озера
	var lake := get_parent().get_node_or_null("Lake") as Node3D
	if lake:
		lake.position.y = _terrain.height_at(
			_terrain.lake_center.x, _terrain.lake_center.y) + 1.2


func _align_spawn_marker() -> void:
	var spawn := get_parent().get_node_or_null("PlayerSpawn") as Node3D
	if spawn:
		spawn.global_position.y = _terrain.height_at(
			spawn.global_position.x, spawn.global_position.z) + 0.4


func _min_dist_ok(p: Vector2, min_dist: float) -> bool:
	if p.distance_to(Vector2.ZERO) < 8.0:                 # не у спауна
		return false
	if p.distance_to(_terrain.lake_center) < _terrain.lake_radius + 3.0:
		return false
	for q in _occupied:
		if p.distance_to(q) < min_dist:
			return false
	return true


func _flat_enough(p: Vector2) -> bool:
	var h := _terrain.height_at(p.x, p.y)
	var dx := absf(_terrain.height_at(p.x + 2.0, p.y) - _terrain.height_at(p.x - 2.0, p.y))
	var dz := absf(_terrain.height_at(p.x, p.y + 2.0) - _terrain.height_at(p.x, p.y - 2.0))
	return (dx + dz) < 2.4 and h > 0.6                    # не круто, не в воде


func _place(scene: PackedScene, p: Vector2, yaw := 0.0) -> Node3D:
	var n := scene.instantiate() as Node3D
	n.position = Vector3(p.x, _terrain.height_at(p.x, p.y), p.y)
	n.rotation.y = yaw
	_occupied.append(p)
	_track(n)
	add_child(n)
	return n


func _scatter() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = scatter_seed
	var half := _terrain.size * 0.5 - 6.0

	_scatter_in_disc(rng, TreeScene, grove_center, grove_radius, grove_trees, 3.0)
	_scatter_in_area(rng, TreeScene, half, lone_trees, 4.0)
	_scatter_in_area(rng, RockScene, half, rocks_count, 4.0)
	_scatter_in_area(rng, BushScene, half, bushes_count, 4.0)
	_scatter_in_area(rng, AnimalScene, half * 0.7, animals_count, 20.0)
	_scatter_decor(rng, half)
	_scatter_grass(rng, half)

	# подборы рядом со спауном: полная фляга, камень, ягоды
	_place_pickup(Flask, Vector2(2.5, 1.5))
	_place_pickup(Stone, Vector2(-3.0, 2.0))
	_place_pickup(Berry, Vector2(1.5, -3.0), 3)


func _scatter_in_disc(rng: RandomNumberGenerator, scene: PackedScene,
		center: Vector2, radius: float, count: int, min_dist: float) -> void:
	var placed := 0
	var tries := 0
	while placed < count and tries < count * 30:
		tries += 1
		var off := Vector2.from_angle(rng.randf() * TAU) * sqrt(rng.randf()) * radius
		var p := center + off
		if _min_dist_ok(p, min_dist) and _flat_enough(p):
			_place(scene, p, rng.randf() * TAU)
			placed += 1


func _scatter_in_area(rng: RandomNumberGenerator, scene: PackedScene,
		half_extent: float, count: int, min_dist: float) -> void:
	var placed := 0
	var tries := 0
	while placed < count and tries < count * 30:
		tries += 1
		var p := Vector2(rng.randf_range(-half_extent, half_extent),
			rng.randf_range(-half_extent, half_extent))
		if _min_dist_ok(p, min_dist) and _flat_enough(p):
			_place(scene, p, rng.randf() * TAU)
			placed += 1


func _track(n: Node3D) -> void:
	var nm := "gen_%d" % _spawn_counter
	_spawn_counter += 1
	n.name = nm
	_spawned_nodes.append(n)
	_spawned_names.append(nm)


## Состояние мира для сохранения: что вырублено/собрано/убито/поднято.
func get_state() -> Dictionary:
	var st := {}
	for i in _spawned_names.size():
		var nm: String = _spawned_names[i]
		var n := _spawned_nodes[i]
		if not is_instance_valid(n):
			st[nm] = {"alive": false}
		elif n is BerryBush and not n._ready_to_pick:
			st[nm] = {"alive": true, "picked": true}
	return st


func apply_state(st: Dictionary) -> void:
	for i in _spawned_names.size():
		var nm: String = _spawned_names[i]
		if not st.has(nm):
			continue
		var n := _spawned_nodes[i]
		var entry: Dictionary = st[nm]
		if not entry.get("alive", true):
			if is_instance_valid(n):
				n.queue_free()
		elif n is BerryBush and entry.get("picked", false) and n._ready_to_pick:
			n._set_ready(false)


## Постсоветский декор: руины, столбы, трасса, ящики/бочки/покрышки у руин.
func _scatter_decor(rng: RandomNumberGenerator, half: float) -> void:
	# разбитая трасса — диагональ через карту, сегменты вровень с рельефом
	for i in range(-9, 10):
		var x := float(i) * 7.0
		var z := float(i) * 2.55 - 25.0
		var p := Vector2(x, z)
		if _flat_enough(p):
			_place_road_segment(p)

	# руины и их окружение
	var ruin_points: Array[Vector2] = []
	var placed := 0
	var tries := 0
	while placed < ruins_count and tries < ruins_count * 40:
		tries += 1
		var p := Vector2(rng.randf_range(-half, half), rng.randf_range(-half, half))
		if _min_dist_ok(p, 18.0) and _flat_enough(p):
			var r := _place(RuinScene, p, rng.randf() * TAU)
			r.position.y -= 0.15  # чуть вросли в землю
			ruin_points.append(p)
			placed += 1
			# детрит вокруг
			for j in crates_per_ruin:
				var off := Vector2.from_angle(rng.randf() * TAU) * rng.randf_range(4.0, 8.0)
				var q := p + off
				if _flat_enough(q):
					var sc: PackedScene = [CrateScene, BarrelScene, TireScene][rng.randi() % 3]
					_place(sc, q, rng.randf() * TAU)

	# столбы ЛЭП — цепочка через карту
	for i in poles_count:
		var p := Vector2(lerpf(-half, half, float(i) / maxi(1, poles_count - 1)), 55.0 + rng.randf_range(-6.0, 6.0))
		if _flat_enough(p):
			_place(PoleScene, p, rng.randf() * 0.15)


## Трава-пятна для объёма мира (без коллизий).
func _scatter_grass(rng: RandomNumberGenerator, half: float) -> void:
	for i in 140:
		var p := Vector2(rng.randf_range(-half, half), rng.randf_range(-half, half))
		if _min_dist_ok(p, 0.5) and _flat_enough(p):
			var sc: PackedScene = [GrassScene, GrassLargeScene, GrassSmallScene][rng.randi() % 3]
			var g := sc.instantiate() as Node3D
			g.position = Vector3(p.x, _terrain.height_at(p.x, p.y), p.y)
			g.rotation.y = rng.randf() * TAU
			var sc_factor := rng.randf_range(1.6, 2.4) if sc == GrassSmallScene else rng.randf_range(0.8, 1.4)
			g.scale = Vector3.ONE * sc_factor
			add_child(g)


## Сегмент дороги, выровненный по наклону рельефа.
func _place_road_segment(p: Vector2) -> void:
	var h_center := _terrain.height_at(p.x, p.y)
	var h_l := _terrain.height_at(p.x - 2.0, p.y)
	var h_r := _terrain.height_at(p.x + 2.0, p.y)
	var h_d := _terrain.height_at(p.x, p.y - 2.0)
	var h_u := _terrain.height_at(p.x, p.y + 2.0)
	var normal := Vector3(h_l - h_r, 4.0, h_d - h_u).normalized()
	# слишком крутой участок вдоль дороги — пропускаем плиту (выбоина)
	var d_dir := absf(_terrain.height_at(p.x + 4.0, p.y + 1.45) - _terrain.height_at(p.x - 4.0, p.y - 1.45))
	if d_dir > 1.4:
		return
	var fwd := Vector3(1.0, 0.0, 4.0 / 11.0).normalized()
	fwd = (fwd - normal * fwd.dot(normal)).normalized()
	var r := RoadScene.instantiate() as Node3D
	r.position = Vector3(p.x, h_center - 0.04, p.y)
	r.basis = Basis.looking_at(fwd, normal)
	_track(r)
	add_child(r)


func _place_pickup(item: ItemData, p: Vector2, count := 1) -> void:
	# предмет задаём ДО добавления в дерево — чтобы подхватился цвет в _ready
	var n := PickupScene.instantiate() as PickupItem
	n.item = item
	n.count = count
	n.position = Vector3(p.x, _terrain.height_at(p.x, p.y) + 0.25, p.y)
	_occupied.append(p)
	_track(n)
	add_child(n)
