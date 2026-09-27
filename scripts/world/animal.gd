class_name Animal
extends CharacterBody3D

const PickupScene: PackedScene = preload("res://scenes/interaction/pickup_item.tscn")
const Meat: ItemData = preload("res://resources/items/meat.tres")
## Кабан: патруль / погоня / атака. Обнаружение — по дистанции.
## Урон игроку — через player.stats.take_damage.

enum State { IDLE, WANDER, CHASE, ATTACK }

@export var max_health := 30.0
@export var walk_speed := 1.5
@export var run_speed := 5.5
@export var damage := 10.0
@export var home_radius := 15.0        # территория патруля вокруг точки спауна
@export var detection_radius := 12.0   # замечает игрока
@export var lose_radius := 18.0        # теряет игрока
@export var lose_time := 6.0           # ...если далеко столько секунд
@export var attack_radius := 1.8
@export var attack_windup := 0.5
@export var attack_cooldown := 1.2

var health: float

var _state := State.IDLE
var _player: Player
var _home: Vector3
var _target: Vector3
var _idle_timer := 0.0
var _windup_timer := 0.0
var _cooldown_timer := 0.0
var _lose_timer := 0.0
var _grunt_timer := 3.0
var _gravity: float


func _ready() -> void:
	health = max_health
	_gravity = ProjectSettings.get_setting("physics/3d/default_gravity")
	_home = global_position
	add_to_group("animals")


func take_damage(amount: float, from_position := Vector3.ZERO) -> void:
	health -= amount
	if health <= 0.0:
		SignalHub.notify.emit("Кабан повержен")
		var drop := PickupScene.instantiate() as PickupItem
		drop.item = Meat
		drop.count = 2
		get_parent().add_child(drop)
		drop.global_position = global_position + Vector3(0.0, 0.3, 0.0)
		queue_free()
	else:
		# отброс от атакующего
		var away := global_position - from_position
		away.y = 0.0
		velocity += away.normalized() * 4.0


func _physics_process(delta: float) -> void:
	if not is_on_floor():
		velocity.y -= _gravity * delta
	if _cooldown_timer > 0.0:
		_cooldown_timer -= delta

	match _state:
		State.IDLE:
			_idle(delta)
		State.WANDER:
			_wander(delta)
		State.CHASE:
			_chase(delta)
		State.ATTACK:
			_attack(delta)

	move_and_slide()


func _grunt(delta: float) -> void:
	_grunt_timer -= delta
	if _grunt_timer <= 0.0 and _player_dist() < 22.0:
		AudioManager.play_grunt(global_position)
		_grunt_timer = randf_range(3.0, 7.0)


func _player_dist() -> float:
	# игрок может появиться позже животного (порядок _ready), ищем лениво
	if _player == null or not is_instance_valid(_player):
		_player = get_tree().get_first_node_in_group("player")
	if _player == null:
		return INF
	return Vector3(global_position.x, 0, global_position.z) \
		.distance_to(Vector3(_player.global_position.x, 0, _player.global_position.z))


func _idle(delta: float) -> void:
	velocity.x = move_toward(velocity.x, 0.0, walk_speed * delta * 8.0)
	velocity.z = move_toward(velocity.z, 0.0, walk_speed * delta * 8.0)
	_idle_timer -= delta
	if _idle_timer <= 0.0:
		# новая случайная точка на территории
		var off := Vector2.from_angle(randf() * TAU) * randf() * home_radius
		_target = _home + Vector3(off.x, 0, off.y)
		_state = State.WANDER
	_grunt(delta)
	if _player_dist() < detection_radius:
		_state = State.CHASE
		AudioManager.play_squeal(global_position)


func _wander(delta: float) -> void:
	_move_toward(_target, walk_speed, delta)
	var flat := Vector2(global_position.x, global_position.z)
	if flat.distance_to(Vector2(_target.x, _target.z)) < 1.0:
		_state = State.IDLE
		_idle_timer = randf_range(1.5, 4.0)
	_grunt(delta)
	if _player_dist() < detection_radius:
		_state = State.CHASE
		AudioManager.play_squeal(global_position)


func _chase(delta: float) -> void:
	if _player == null or _player.stats.is_dead:
		_state = State.IDLE
		_idle_timer = 2.0
		return
	_target = _player.global_position
	_move_toward(_target, run_speed, delta)

	var d := _player_dist()
	if d > lose_radius:
		_lose_timer += delta
		if _lose_timer >= lose_time:
			_lose_timer = 0.0
			_state = State.WANDER
			_target = _home
	else:
		_lose_timer = 0.0

	if d < attack_radius and _cooldown_timer <= 0.0:
		_state = State.ATTACK
		_windup_timer = attack_windup


func _attack(delta: float) -> void:
	# разворот к игроку, замах
	if _player != null:
		_face(_player.global_position, delta)
	velocity.x = move_toward(velocity.x, 0.0, run_speed * delta * 8.0)
	velocity.z = move_toward(velocity.z, 0.0, run_speed * delta * 8.0)
	_windup_timer -= delta
	if _windup_timer <= 0.0:
		AudioManager.play_grunt(global_position, -20.0)
		if _player_dist() < attack_radius + 0.6:
			_player.stats.take_damage(damage)
		_cooldown_timer = attack_cooldown
		_state = State.CHASE


func _move_toward(target: Vector3, speed: float, delta: float) -> void:
	var dir := Vector3(target.x - global_position.x, 0, target.z - global_position.z)
	if dir.length() > 0.05:
		dir = dir.normalized()
		velocity.x = move_toward(velocity.x, dir.x * speed, speed * delta * 6.0)
		velocity.z = move_toward(velocity.z, dir.z * speed, speed * delta * 6.0)
		_face(global_position + dir, delta)
	else:
		velocity.x = 0.0
		velocity.z = 0.0


func _face(target: Vector3, delta: float) -> void:
	var d := target - global_position
	d.y = 0.0
	if d.length() < 0.01:
		return
	var target_yaw := atan2(-d.x, -d.z)
	rotation.y = lerp_angle(rotation.y, target_yaw, 6.0 * delta)
