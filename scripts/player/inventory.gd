class_name Inventory
extends Node
## Ядро инвентаря: массив слотов со стаканием.
## UI подписывается на сигнал changed.

signal changed

const SLOT_COUNT := 20

## Реестр предметов по id (для сохранения)
const ITEM_DB := {
	&"stone": preload("res://resources/items/stone.tres"),
	&"wood": preload("res://resources/items/wood.tres"),
	&"berry": preload("res://resources/items/berry.tres"),
	&"flask": preload("res://resources/items/flask.tres"),
	&"flask_empty": preload("res://resources/items/flask_empty.tres"),
	&"axe": preload("res://resources/items/axe.tres"),
	&"meat": preload("res://resources/items/meat.tres"),
	&"cooked_meat": preload("res://resources/items/cooked_meat.tres"),
	&"wall": preload("res://resources/items/wall.tres"),
	&"campfire": preload("res://resources/items/campfire.tres"),
}

## Слоты: {"item": ItemData | null, "count": int}
var slots: Array[Dictionary] = []


func _ready() -> void:
	slots.resize(SLOT_COUNT)
	for i in SLOT_COUNT:
		slots[i] = {"item": null, "count": 0}
	changed.connect(_on_changed)
	# UI может создаться позже — шлём первичное состояние отложенно
	call_deferred("_emit_initial")


func _on_changed() -> void:
	SignalHub.inventory_changed.emit(slots)


func _emit_initial() -> void:
	SignalHub.inventory_changed.emit(slots)
	SignalHub.selection_changed.emit(selected_slot)


## Активный слот хотбара (0..4).
var selected_slot := 0


func select_slot(index: int) -> void:
	selected_slot = clampi(index, 0, 4)
	SignalHub.selection_changed.emit(selected_slot)


func cycle_slot(dir: int) -> void:
	select_slot((selected_slot + dir + 5) % 5)


## Содержимое активного слота хотбара.
func get_selected_slot() -> Dictionary:
	return slots[selected_slot]


## Сериализация для сохранения: [{id, count}, ...]
func serialize() -> Array:
	var out: Array = []
	for slot in slots:
		if slot["item"] != null and slot["count"] > 0:
			out.append({"id": slot["item"].id, "count": slot["count"]})
	return out


func deserialize(data: Array) -> void:
	for slot in slots:
		slot["item"] = null
		slot["count"] = 0
	for e in data:
		var item: ItemData = ITEM_DB.get(StringName(e.get("id", "")))
		if item:
			add_item(item, int(e.get("count", 1)))
	changed.emit()


## Забирает всю пачку из слота (для выбрасывания). Возвращает {"item","count"}.
func take_all(index: int) -> Dictionary:
	if index < 0 or index >= slots.size():
		return {}
	var slot := slots[index]
	var out := slot.duplicate()
	slot["item"] = null
	slot["count"] = 0
	changed.emit()
	return out


## Кладёт предметы в инвентарь. Возвращает, сколько не влезло (0 = всё влезло).
func add_item(item: ItemData, count: int) -> int:
	if item == null or count <= 0:
		return count
	var remaining := count
	# Сначала достакиваем существующие слоты
	for slot in slots:
		if remaining <= 0:
			break
		if slot["item"] == item and slot["count"] < item.max_stack:
			var can_add: int = mini(item.max_stack - slot["count"], remaining)
			slot["count"] += can_add
			remaining -= can_add
	# Потом занимаем пустые слоты
	for slot in slots:
		if remaining <= 0:
			break
		if slot["item"] == null:
			var can_add: int = mini(item.max_stack, remaining)
			slot["item"] = item
			slot["count"] = can_add
			remaining -= can_add
	changed.emit()
	return remaining


## Сколько всего такого предмета в инвентаре.
func count_of(item: ItemData) -> int:
	var total := 0
	for slot in slots:
		if slot["item"] == item:
			total += slot["count"]
	return total


func has_item(item: ItemData, count: int = 1) -> bool:
	return count_of(item) >= count


## Хватает ли ресурсов по рецепту {id: need}.
func has_items(cost: Dictionary) -> bool:
	for id in cost:
		var item: ItemData = ITEM_DB.get(id)
		if item == null or count_of(item) < cost[id]:
			return false
	return true


## Списывает ресурсы по рецепту (только если их хватает). Возвращает успех.
func pay_cost(cost: Dictionary) -> bool:
	if not has_items(cost):
		return false
	for id in cost:
		remove_item(ITEM_DB[id], cost[id])
	return true


## Забирает count предметов. false, если не хватило.
func remove_item(item: ItemData, count: int) -> bool:
	if not has_item(item, count):
		return false
	var remaining := count
	for i in slots.size():
		if remaining <= 0:
			break
		var slot := slots[i]
		if slot["item"] != item:
			continue
		var take: int = mini(slot["count"], remaining)
		slot["count"] -= take
		remaining -= take
		if slot["count"] <= 0:
			slot["item"] = null
			slot["count"] = 0
	changed.emit()
	return true
