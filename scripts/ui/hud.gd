class_name HUD
extends CanvasLayer
## Игровой HUD: прицел-точка, подсказка взаимодействия, тосты, хотбар.
## Слушает SignalHub, сам ни от кого не зависит.

const TOAST_TIME := 2.0

@onready var prompt_label: Label = %PromptLabel
@onready var toast_label: Label = %ToastLabel

var _toast_timer := 0.0


func _ready() -> void:
	prompt_label.visible = false
	toast_label.visible = false
	SignalHub.prompt_changed.connect(_on_prompt_changed)
	SignalHub.notify.connect(_on_notify)


func _process(delta: float) -> void:
	if toast_label.visible:
		_toast_timer -= delta
		if _toast_timer <= 0.0:
			toast_label.visible = false


func _on_prompt_changed(text: String) -> void:
	prompt_label.text = text
	prompt_label.visible = text != ""


func _on_notify(text: String) -> void:
	toast_label.text = text
	toast_label.visible = true
	_toast_timer = TOAST_TIME
