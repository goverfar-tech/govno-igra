class_name Hotbar
extends HBoxContainer
## Хотбар: первые 5 слотов инвентаря внизу экрана.
## Цветной квадрат = цвет предмета, цифра = количество.

const SLOT_COUNT := 5

var _slot_panels: Array[ColorRect] = []
var _slot_labels: Array[Label] = []


func _ready() -> void:
	for i in SLOT_COUNT:
		var slot := ColorRect.new()
		slot.custom_minimum_size = Vector2(48, 48)
		slot.color = Color(0.12, 0.12, 0.12, 0.8)
		slot.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var label := Label.new()
		label.set_anchors_preset(Control.PRESET_FULL_RECT)
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		slot.add_child(label)
		add_child(slot)
		_slot_panels.append(slot)
		_slot_labels.append(label)
	SignalHub.inventory_changed.connect(_on_inventory_changed)


func _on_inventory_changed(slots: Array) -> void:
	for i in SLOT_COUNT:
		var data: Dictionary = slots[i] if i < slots.size() else {}
		if data.get("item") == null:
			_slot_labels[i].text = ""
			_slot_panels[i].color = Color(0.12, 0.12, 0.12, 0.8)
		else:
			_slot_labels[i].text = "×%d" % data["count"]
			_slot_panels[i].color = (data["item"] as ItemData).icon_color
