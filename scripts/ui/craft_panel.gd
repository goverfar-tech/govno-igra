class_name CraftPanel
extends Control
## Панель крафта: C — открыть/закрыть. Рецепты — RecipeData-ресурсы.

const RECIPES: Array = [
	preload("res://resources/recipes/axe.tres"),
	preload("res://resources/recipes/spear.tres"),
	preload("res://resources/recipes/wall.tres"),
	preload("res://resources/recipes/campfire.tres"),
]

var _player: Player
var _rows: Array[Button] = []

@onready var _list: VBoxContainer = %RecipesList


func _ready() -> void:
	visible = false
	_build_rows()
	SignalHub.inventory_changed.connect(func(_s: Array) -> void: _refresh())
	visibility_changed.connect(_refresh)
	_player = get_tree().get_first_node_in_group("player")


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_craft"):
		_set_open(not visible)
		get_viewport().set_input_as_handled()
	elif visible and event.is_action_pressed("ui_cancel"):
		_set_open(false)
		get_viewport().set_input_as_handled()


func _set_open(open: bool) -> void:
	visible = open
	SignalHub.inventory_open_changed.emit(open)


func _build_rows() -> void:
	for recipe: RecipeData in RECIPES:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 10)
		row.mouse_filter = Control.MOUSE_FILTER_IGNORE

		var icon := ColorRect.new()
		icon.custom_minimum_size = Vector2(34, 34)
		icon.color = recipe.result.icon_color
		icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(icon)

		var label := Label.new()
		label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(label)

		var btn := Button.new()
		btn.text = "Создать"
		btn.custom_minimum_size = Vector2(110, 0)
		btn.pressed.connect(_on_craft_pressed.bind(recipe))
		row.add_child(btn)

		_list.add_child(row)
		_rows.append(btn)
	_refresh()


func _refresh() -> void:
	if not is_node_ready():
		return
	for i in RECIPES.size():
		var recipe: RecipeData = RECIPES[i]
		var btn: Button = _rows[i]
		var row := btn.get_parent()
		var label: Label = row.get_child(1)
		var cost_texts: Array[String] = []
		var afford := true
		for id in recipe.cost:
			var need: int = recipe.cost[id]
			var have := 0
			if _player:
				var item: ItemData = Inventory.ITEM_DB.get(id)
				if item:
					have = _player.inventory.count_of(item)
			if have < need:
				afford = false
			var item_name: String = Inventory.ITEM_DB.get(id).display_name if Inventory.ITEM_DB.has(id) else str(id)
			cost_texts.append("%s ×%d (%d)" % [item_name, need, have])
		label.text = "%s\n%s" % [recipe.result.display_name + (" ×%d" % recipe.result_count if recipe.result_count > 1 else ""), " · ".join(cost_texts)]
		btn.disabled = not afford or _player == null


func _on_craft_pressed(recipe: RecipeData) -> void:
	if _player == null:
		return
	if not _player.inventory.has_items(recipe.cost):
		SignalHub.notify.emit("Не хватает ресурсов")
		return
	var leftover := _player.inventory.add_item(recipe.result, recipe.result_count)
	if leftover > 0:
		# ресурсы не тратим, если результат не влез
		SignalHub.notify.emit("Инвентарь полон!")
		return
	_player.inventory.pay_cost(recipe.cost)
	AudioManager.play_pickup()
	SignalHub.notify.emit("Создано: %s" % recipe.result.display_name)
	_refresh()
