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

const Stone: ItemData = preload("res://resources/items/stone.tres")
const Berry: ItemData = preload("res://resources/items/berry.tres")
const Flask: ItemData = preload("res://resources/items/flask.tres")
const Axe: ItemData = preload("res://resources/items/axe.tres")

@export var scatter_seed := 4242
@export var grove_center := Vector2(-45.0, -40.0)
@export var grove_radius := 25.0
@export var grove_trees := 25
@export var lone_trees := 10
@export var rocks_count := 10
@export var bushes_count := 15
@export var animals_count := 3

var _terrain: Terrain
var _occupied: Array[Vector2] = []


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

	# подборы рядом со спауном: полная фляга, камень, ягоды
	_place_pickup(Flask, Vector2(2.5, 1.5))
	_place_pickup(Stone, Vector2(-3.0, 2.0))
	_place_pickup(Berry, Vector2(1.5, -3.0), 3)
	_place_pickup(Axe, Vector2(-1.5, -2.0))


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


func _place_pickup(item: ItemData, p: Vector2, count := 1) -> void:
	# предмет задаём ДО добавления в дерево — чтобы подхватился цвет в _ready
	var n := PickupScene.instantiate() as PickupItem
	n.item = item
	n.count = count
	n.position = Vector3(p.x, _terrain.height_at(p.x, p.y) + 0.25, p.y)
	_occupied.append(p)
	add_child(n)
