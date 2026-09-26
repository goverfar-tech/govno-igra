class_name Hotbar
extends HBoxContainer
## Хотбар: слоты 0–4 инвентаря внизу экрана.
## Цветной квадрат = цвет предмета, цифра = количество, белая рамка = активный.

const SLOT_COUNT := 5

var _slot_panels: Array[PanelContainer] = []
var _slot_bgs: Array[ColorRect] = []
var _slot_labels: Array[Label] = []


func _ready() -> void:
	for i in SLOT_COUNT:
		var panel := PanelContainer.new()
		var sb := StyleBoxFlat.new()
		sb.bg_color = Color(0.12, 0.12, 0.12, 0.85)
		sb.set_border_width_all(2)
		sb.border_color = Color(0.35, 0.35, 0.35)
		panel.add_theme_stylebox_override("panel", sb)
		panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var bg := ColorRect.new()
		bg.custom_minimum_size = Vector2(44, 44)
		bg.color = Color(0, 0, 0, 0)
		bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var label := Label.new()
		label.set_anchors_preset(Control.PRESET_FULL_RECT)
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		bg.add_child(label)
		panel.add_child(bg)
		add_child(panel)
		_slot_panels.append(panel)
		_slot_bgs.append(bg)
		_slot_labels.append(label)
	SignalHub.inventory_changed.connect(_on_inventory_changed)
	SignalHub.selection_changed.connect(_on_selection_changed)


func _on_inventory_changed(slots: Array) -> void:
	for i in SLOT_COUNT:
		var d: Dictionary = slots[i] if i < slots.size() else {}
		var item: ItemData = d.get("item")
		if item == null:
			_slot_bgs[i].color = Color(0, 0, 0, 0)
			_slot_labels[i].text = ""
		else:
			_slot_bgs[i].color = item.icon_color
			_slot_labels[i].text = "×%d" % d["count"] if d["count"] > 1 else ""


func _on_selection_changed(selected: int) -> void:
	for i in SLOT_COUNT:
		var sb := _slot_panels[i].get_theme_stylebox("panel") as StyleBoxFlat
		sb.border_color = Color(1, 1, 1) if i == selected else Color(0.35, 0.35, 0.35)
