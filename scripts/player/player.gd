class_name Player
extends CharacterBody3D
## FPS-контроллер: ходьба/бег/прыжок/присед, камера от первого лица, headbob,
## выбор слота хотбара (1–5, колёсико), выброс предметов, захват мыши.

const HEAD_STAND_Y: float = 1.6
const HEAD_CROUCH_Y: float = 1.0
const CAPSULE_STAND_HEIGHT: float = 1.8
const CAPSULE_CROUCH_HEIGHT: float = 1.1

const PickupItemScene: PackedScene = preload("res://scenes/interaction/pickup_item.tscn")

@export var walk_speed: float = 4.0
@export var sprint_speed: float = 7.0
@export var crouch_speed: float = 2.0
@export var jump_velocity: float = 4.5
@export var acceleration: float = 40.0
@export var mouse_sensitivity: float = 0.003
@export var headbob_frequency: float = 1.8
@export var headbob_amplitude: float = 0.035

@onready var head: Node3D = $Head
@onready var camera: Camera3D = $Head/Camera3D
@onready var collision_shape: CollisionShape3D = $CollisionShape3D
@onready var head_clearance: ShapeCast3D = $HeadClearance
@onready var inventory: Inventory = $Inventory
@onready var stats: Stats = $Stats

var gravity: float = ProjectSettings.get_setting("physics/3d/default_gravity")
var is_crouching := false
var panel_open := false

var _pitch := 0.0
var _bob_timer := 0.0
var _step_accum := 0.0


func _ready() -> void:
	add_to_group("player")
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	SignalHub.inventory_open_changed.connect(_on_inventory_open_changed)


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion and not panel_open \
			and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		rotate_y(-event.relative.x * mouse_sensitivity)
		_pitch = clampf(_pitch - event.relative.y * mouse_sensitivity, deg_to_rad(-89.0), deg_to_rad(89.0))
		head.rotation.x = _pitch
		return
	if event.is_action_pressed("ui_cancel"):
		return  # Esc обрабатывает меню паузы / панель инвентаря
	elif event is InputEventMouseButton and event.pressed and not panel_open \
			and Input.mouse_mode == Input.MOUSE_MODE_VISIBLE:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

	if panel_open:
		return
	if event is InputEventMouseButton and event.pressed:
		match event.button_index:
			MOUSE_BUTTON_WHEEL_UP:
				inventory.cycle_slot(-1)
			MOUSE_BUTTON_WHEEL_DOWN:
				inventory.cycle_slot(1)
			MOUSE_BUTTON_LEFT:
				if Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
					use_selected_item()
	elif event is InputEventKey and event.pressed and not event.echo:
		if event.keycode >= KEY_1 and event.keycode <= KEY_5:
			inventory.select_slot(event.keycode - KEY_1)


func _physics_process(delta: float) -> void:
	_update_crouch_state()

	if not is_on_floor():
		velocity.y -= gravity * delta
	elif Input.is_action_just_pressed("jump") and not is_crouching and not panel_open:
		velocity.y = jump_velocity

	var input_dir := Input.get_vector("move_left", "move_right", "move_forward", "move_back")
	if panel_open:
		input_dir = Vector2.ZERO
	var direction := (transform.basis * Vector3(input_dir.x, 0.0, input_dir.y)).normalized()
	var speed := _current_speed()

	velocity.x = move_toward(velocity.x, direction.x * speed, acceleration * delta)
	velocity.z = move_toward(velocity.z, direction.z * speed, acceleration * delta)

	_update_headbob(delta, input_dir)
	move_and_slide()
	_update_footsteps(delta, input_dir)


func _current_speed() -> float:
	if is_crouching:
		return crouch_speed
	if Input.is_action_pressed("sprint"):
		return sprint_speed
	return walk_speed


func _update_crouch_state() -> void:
	if Input.is_action_pressed("crouch"):
		_set_crouching(true)
	elif is_crouching and not head_clearance.is_colliding():
		# Встаём только если над головой свободно
		_set_crouching(false)


func _set_crouching(value: bool) -> void:
	if is_crouching == value:
		return
	is_crouching = value
	var capsule := collision_shape.shape as CapsuleShape3D
	capsule.height = CAPSULE_CROUCH_HEIGHT if value else CAPSULE_STAND_HEIGHT
	collision_shape.position.y = capsule.height * 0.5
	var tween := create_tween()
	tween.tween_property(head, "position:y", HEAD_CROUCH_Y if value else HEAD_STAND_Y, 0.15)


## Шаги: звук каждые ~1.9 м пройденного пути; крадёмся — тише.
func _update_footsteps(delta: float, input_dir: Vector2) -> void:
	if is_on_floor() and input_dir.length() > 0.1:
		_step_accum += Vector2(velocity.x, velocity.z).length() * delta
		if _step_accum >= 1.9:
			_step_accum = 0.0
			var vol := -22.0 if is_crouching else (-17.0 if is_sprinting_now() else -19.0)
			AudioManager.play_step(vol)
	else:
		_step_accum = 0.0


func _update_headbob(delta: float, input_dir: Vector2) -> void:
	if is_on_floor() and input_dir.length() > 0.1:
		_bob_timer += delta * headbob_frequency * (_current_speed() / walk_speed)
		camera.position.y = sin(_bob_timer * TAU) * headbob_amplitude
		camera.position.x = cos(_bob_timer * TAU * 0.5) * headbob_amplitude * 0.6
	else:
		_bob_timer = 0.0
		camera.position = camera.position.lerp(Vector3.ZERO, delta * 8.0)


## Идёт ли игрок бегом прямо сейчас (для ускоренного расхода статов).
func is_sprinting_now() -> bool:
	if is_crouching or not is_on_floor():
		return false
	if not Input.is_action_pressed("sprint"):
		return false
	return Input.get_vector("move_left", "move_right", "move_forward", "move_back").length() > 0.1


## Использовать предмет из активного слота хотбара (ЛКМ).
func use_selected_item() -> void:
	var data := inventory.get_selected_slot()
	var item: ItemData = data.get("item")
	if item == null:
		return
	if item.is_edible or item.nutrition > 0.0 or item.hydration > 0.0:
		if inventory.remove_item(item, 1):
			stats.apply_food(item.nutrition, item.hydration)
			AudioManager.play_eat()
			SignalHub.notify.emit("Съедено: %s" % item.display_name)
	else:
		SignalHub.notify.emit("Это нельзя съесть")


## Выбросить всю пачку из слота перед собой.
func drop_slot(index: int) -> void:
	var data := inventory.take_all(index)
	if data.get("item") == null:
		return
	var pickup := PickupItemScene.instantiate() as PickupItem
	pickup.item = data["item"]
	pickup.count = data["count"]
	get_parent().add_child(pickup)
	var dir := -camera.global_transform.basis.z
	dir.y = 0.0
	dir = dir.normalized()
	pickup.global_position = global_position + dir * 1.2 + Vector3(0.0, 0.3, 0.0)


func _on_inventory_open_changed(open: bool) -> void:
	panel_open = open
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE if open else Input.MOUSE_MODE_CAPTURED
