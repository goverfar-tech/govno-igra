class_name HUD
extends CanvasLayer
## Игровой HUD: прицел-точка и подпись подсказки взаимодействия.
## Слушает SignalHub, сам ни от кого не зависит.

@onready var prompt_label: Label = %PromptLabel


func _ready() -> void:
	prompt_label.visible = false
	SignalHub.prompt_changed.connect(_on_prompt_changed)


func _on_prompt_changed(text: String) -> void:
	prompt_label.text = text
	prompt_label.visible = text != ""
