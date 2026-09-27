class_name Stats
extends Node
## Статы выживания игрока. Дочерний узел Player.
## Расход: сытость медленнее, жажда быстрее; бег ускоряет расход.
## Пустой стат — медленная потеря HP. HP 0 — смерть.

const MAX_VALUE := 100.0
const HUNGER_PER_SEC := 100.0 / 600.0  # ~10 минут до голода
const THIRST_PER_SEC := 100.0 / 360.0  # ~6 минут до жажды
const STARVE_DPS := 1.0                # урон HP в секунду при пустом стате
const SPRINT_MULT := 1.6               # множитель расхода при беге
const NIGHT_MULT := 1.5                # множитель расхода ночью (прохладно)
const REGEN_MIN_STAT := 70.0           # регенерация HP, если еда И вода выше этого
const REGEN_RATE := 0.5                # HP в секунду

var health := MAX_VALUE
var hunger := MAX_VALUE
var thirst := MAX_VALUE

var is_dead := false

var _is_night := false
var _last_emitted := Vector3(-1.0, -1.0, -1.0)

@onready var _player: Player = get_parent()


func _ready() -> void:
	# UI создаётся позже — первичное состояние отложенно
	call_deferred("_emit")
	SignalHub.time_of_day_changed.connect(_on_time_of_day_changed)


func _physics_process(delta: float) -> void:
	if is_dead:
		return
	var mult := SPRINT_MULT if _player and _player.is_sprinting_now() else 1.0
	if _is_night:
		mult *= NIGHT_MULT

	hunger = maxf(0.0, hunger - HUNGER_PER_SEC * mult * delta)
	thirst = maxf(0.0, thirst - THIRST_PER_SEC * mult * delta)
	if hunger <= 0.0 or thirst <= 0.0:
		health = maxf(0.0, health - STARVE_DPS * delta)
		if health <= 0.0:
			is_dead = true
			SignalHub.player_died.emit()
	elif hunger >= REGEN_MIN_STAT and thirst >= REGEN_MIN_STAT:
		health = minf(MAX_VALUE, health + REGEN_RATE * delta)
	_emit()


func _emit() -> void:
	var current := Vector3(snappedf(health, 0.1), snappedf(hunger, 0.1), snappedf(thirst, 0.1))
	if current != _last_emitted:
		_last_emitted = current
		SignalHub.stats_changed.emit(health, hunger, thirst)


## Получить урон (кабан и будущие источники). Игнорируется после смерти.
func take_damage(amount: float) -> void:
	if is_dead:
		return
	health = maxf(0.0, health - amount)
	SignalHub.player_damaged.emit(amount)
	if health <= 0.0:
		is_dead = true
		SignalHub.player_died.emit()
	_emit()


func _on_time_of_day_changed(time: float) -> void:
	_is_night = time < 0.25 or time > 0.75


## Установить все статы разом (загрузка сохранения).
func set_all(h: float, hu: float, th: float) -> void:
	health = clampf(h, 0.0, MAX_VALUE)
	hunger = clampf(hu, 0.0, MAX_VALUE)
	thirst = clampf(th, 0.0, MAX_VALUE)
	_emit()


## Съесть/выпить предмет: восстанавливает сытость и жажду.
func apply_food(nutrition: float, hydration: float) -> void:
	hunger = minf(MAX_VALUE, hunger + nutrition)
	thirst = minf(MAX_VALUE, thirst + hydration)
	_emit()
