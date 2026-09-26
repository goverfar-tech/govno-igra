class_name InteractRay
extends RayCast3D
## Луч взаимодействия из камеры: находит Interactable перед игроком,
## обновляет подсказку на HUD и вызывает interact() по клавише E.

var _current: Interactable = null


func _ready() -> void:
	# Игрок — предок луча; не ловим его собственную капсулу
	var player := get_parent().get_parent()  # Head -> Player
	if player is CollisionObject3D:
		add_exception(player)


func _physics_process(_delta: float) -> void:
	# Подобранный/удалённый объект — забываем и прячем подсказку
	if _current and not is_instance_valid(_current):
		_current = null
		SignalHub.prompt_changed.emit("")

	var target := _get_interactable()
	if target != _current:
		_current = target
		SignalHub.prompt_changed.emit(target.get_prompt() if target else "")

	if _current and Input.is_action_just_pressed("interact"):
		var target_obj := _current
		_current = null
		SignalHub.prompt_changed.emit("")
		target_obj.interact(owner as Player)
		force_raycast_update()


func _get_interactable() -> Interactable:
	if not is_colliding():
		return null
	var collider := get_collider()
	if collider is Interactable:
		return collider
	return null
