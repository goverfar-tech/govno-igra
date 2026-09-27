class_name InventoryPanel
extends Control
## Полная панель инвентаря: Tab/Esc — открыть/закрыть,
## ЛКМ по слоту 0–4 — выбрать в хотбар, ПКМ — выбросить пачку.

const SLOT_COUNT := 20

var _slot_bgs: Array[ColorRect] = []
var _slot_labels: Array[Label] = []
var _slot_panels: Array[PanelContainer] = []
var _player: Player
var _held_index := -1  # слот, «взятый» в руку в панели (жёлтая рамка)

@onready var _grid: GridContainer = %Grid


func _ready() -> void:
	visible = false
	_build_slots()
	SignalHub.inventory_changed.connect(_refresh)
	SignalHub.selection_changed.connect(_refresh_selection)
	_player = get_tree().get_first_node_in_group("player")


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_inventory"):
		_set_open(not visible)
		get_viewport().set_input_as_handled()
	elif visible and event.is_action_pressed("ui_cancel"):
		_set_open(false)
		get_viewport().set_input_as_handled()


func _set_open(open: bool) -> void:
	visible = open
	if not open:
		_held_index = -1
		if _player:
			_refresh_selection(_player.inventory.selected_slot)
	SignalHub.inventory_open_changed.emit(open)


func _build_slots() -> void:
	for i in SLOT_COUNT:
		var panel := PanelContainer.new()
		var sb := StyleBoxFlat.new()
		sb.bg_color = Color(0.12, 0.12, 0.12, 0.9)
		sb.set_border_width_all(2)
		sb.border_color = Color(0.35, 0.35, 0.35)
		panel.add_theme_stylebox_override("panel", sb)
		var bg := ColorRect.new()
		bg.custom_minimum_size = Vector2(48, 48)
		bg.color = Color(0, 0, 0, 0)
		bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var label := Label.new()
		label.set_anchors_preset(Control.PRESET_FULL_RECT)
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		bg.add_child(label)
		panel.add_child(bg)
		panel.gui_input.connect(_on_slot_gui_input.bind(i))
		_grid.add_child(panel)
		_slot_panels.append(panel)
		_slot_bgs.append(bg)
		_slot_labels.append(label)


func _on_slot_gui_input(event: InputEvent, index: int) -> void:
	if not (event is InputEventMouseButton and event.pressed) or _player == null:
		return
	if event.button_index == MOUSE_BUTTON_LEFT:
		if _held_index == -1:
			# взять пачку, если слот не пуст
			var d: Dictionary = _player.inventory.slots[index]
			if d.get("item") != null:
				_held_index = index
				_refresh_selection(_player.inventory.selected_slot)
		else:
			# положить/свапнуть
			_player.inventory.swap_slots(_held_index, index)
			_held_index = -1
			_refresh_selection(_player.inventory.selected_slot)
	elif event.button_index == MOUSE_BUTTON_RIGHT:
		if _held_index != -1:
			_held_index = -1
			_refresh_selection(_player.inventory.selected_slot)
		else:
			_player.drop_slot(index)


func _refresh(slots: Array) -> void:
	for i in SLOT_COUNT:
		var d: Dictionary = slots[i] if i < slots.size() else {}
		var item: ItemData = d.get("item")
		if item == null:
			_slot_bgs[i].color = Color(0, 0, 0, 0)
			_slot_labels[i].text = ""
		else:
			_slot_bgs[i].color = item.icon_color
			_slot_labels[i].text = "×%d" % d["count"] if d["count"] > 1 else item.display_name.substr(0, 6)


func _refresh_selection(selected: int) -> void:
	for i in SLOT_COUNT:
		var sb := _slot_panels[i].get_theme_stylebox("panel") as StyleBoxFlat
		if i == _held_index:
			sb.border_color = Color(1, 0.85, 0.2)  # в руке — жёлтый
		elif i == selected:
			sb.border_color = Color(1, 1, 1)
		else:
			sb.border_color = Color(0.35, 0.35, 0.35)
