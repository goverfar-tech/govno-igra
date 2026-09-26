class_name DeathScreen
extends Control
## Экран смерти: по сигналу player_died ставит игру на паузу и
## предлагает начать заново (перезагрузка сцены).
## process_mode = ALWAYS — работает на паузе.


func _ready() -> void:
	visible = false
	SignalHub.player_died.connect(_on_player_died)
	$Center/VBox/RestartButton.pressed.connect(_on_restart_pressed)


func _unhandled_input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_accept"):
		_on_restart_pressed()


func _on_player_died() -> void:
	visible = true
	get_tree().paused = true
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE


func _on_restart_pressed() -> void:
	get_tree().paused = false
	get_tree().reload_current_scene()
