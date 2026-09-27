class_name MainMenu
extends Control
## Главное меню: «Начать игру» / «Выход».


func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	$Center/VBox/PlayButton.pressed.connect(_on_play_pressed)
	$Center/VBox/QuitButton.pressed.connect(_on_quit_pressed)


func _on_play_pressed() -> void:
	get_tree().change_scene_to_file("res://scenes/main/game.tscn")


func _on_quit_pressed() -> void:
	get_tree().quit()
