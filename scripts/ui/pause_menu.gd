class_name PauseMenu
extends Control
## Меню паузы: Esc открывает/закрывает. process_mode = ALWAYS —
## работает, пока игра на паузе.


func _ready() -> void:
	visible = false
	$Center/VBox/ResumeButton.pressed.connect(close)
	$Center/VBox/QuitButton.pressed.connect(func() -> void: get_tree().quit())


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		if visible:
			close()
		else:
			open()
		get_viewport().set_input_as_handled()


func open() -> void:
	visible = true
	get_tree().paused = true
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE


func close() -> void:
	visible = false
	get_tree().paused = false
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
