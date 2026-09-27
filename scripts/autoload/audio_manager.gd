extends Node
## AudioManager (AutoLoad): процедурные звуки без ассетов.
## _make_*  генерирует AudioStreamWAV (16 бит) в памяти один раз при старте.
## play_*   играют звук (плоский или позиционный 3D).
## Генерация идёт через float-сэмплы [-1, 1], конвертация в конце.

const SAMPLE_RATE := 22050

var _step_stream: AudioStreamWAV
var _hit_stream: AudioStreamWAV
var _pickup_stream: AudioStreamWAV
var _eat_stream: AudioStreamWAV
var _grunt_stream: AudioStreamWAV
var _squeal_stream: AudioStreamWAV


func _ready() -> void:
	_step_stream = _make_step()
	_hit_stream = _make_hit()
	_pickup_stream = _make_blip(660.0, 880.0, 0.12)
	_eat_stream = _make_blip(440.0, 260.0, 0.25)
	_grunt_stream = _make_grunt()
	_squeal_stream = _make_blip(420.0, 900.0, 0.22)
	_play_ambient(_make_wind())


func play_step(volume_db: float) -> void:
	_play_flat(_step_stream, volume_db)


func play_hit(pos: Vector3) -> void:
	_play_3d(_hit_stream, pos, -16.0)


func play_pickup() -> void:
	_play_flat(_pickup_stream, -14.0)


func play_eat() -> void:
	_play_flat(_eat_stream, -15.0)


## Хрюканье/фырканье кабана (позиционное).
func play_grunt(pos: Vector3) -> void:
	_play_3d(_grunt_stream, pos, -12.0)


## Визг-агро.
func play_squeal(pos: Vector3) -> void:
	_play_3d(_squeal_stream, pos, -14.0)


# ---------- генерация ----------

## float-сэмплы [-1,1] -> 16-bit WAV.
func _make_wav(f: PackedFloat32Array) -> AudioStreamWAV:
	var n := f.size()
	var data := PackedByteArray()
	data.resize(n * 2)
	for i in n:
		var v := int(clampf(f[i], -1.0, 1.0) * 32767.0)
		data[i * 2] = v & 0xFF
		data[i * 2 + 1] = (v >> 8) & 0xFF
	var s := AudioStreamWAV.new()
	s.format = AudioStreamWAV.FORMAT_16_BITS
	s.mix_rate = SAMPLE_RATE
	s.data = data
	return s


## Глухой мягкий шлепок: сильно отфильтрованный шум, быстрый спад.
func _make_step() -> AudioStreamWAV:
	var n := int(SAMPLE_RATE * 0.12)
	var f := PackedFloat32Array()
	f.resize(n)
	var rng := RandomNumberGenerator.new()
	rng.randomize()
	var prev := 0.0
	for i in n:
		var env := exp(-26.0 * i / n)
		var white := rng.randf_range(-1.0, 1.0)
		prev = prev * 0.82 + white * 0.18
		f[i] = prev * env * 0.35
	return _make_wav(f)


## Удар по дереву/камню: короткий глубокий тук.
func _make_hit() -> AudioStreamWAV:
	var n := int(SAMPLE_RATE * 0.18)
	var f := PackedFloat32Array()
	f.resize(n)
	var rng := RandomNumberGenerator.new()
	rng.randomize()
	var phase := 0.0
	for i in n:
		var t := float(i) / SAMPLE_RATE
		var env := exp(-18.0 * t)
		var freq := lerpf(150.0, 60.0, t / 0.18)
		phase += freq * TAU / SAMPLE_RATE
		var tone := sin(phase) * 0.65
		var noise := rng.randf_range(-1.0, 1.0) * 0.18
		f[i] = (tone + noise) * env * 0.4
	return _make_wav(f)


## Короткий слайд-тон (подбор вверх, еда вниз).
func _make_blip(f_from: float, f_to: float, dur: float) -> AudioStreamWAV:
	var n := int(SAMPLE_RATE * dur)
	var f := PackedFloat32Array()
	f.resize(n)
	var phase := 0.0
	for i in n:
		var t := float(i) / n
		var env := exp(-5.0 * t)
		var freq := lerpf(f_from, f_to, t)
		phase += freq * TAU / SAMPLE_RATE
		f[i] = sin(phase) * env * 0.22
	return _make_wav(f)


## Ветер: глубокий «дышащий» гул (сильный ФНЧ, длинная петля, края сшиты).
func _make_wind() -> AudioStreamWAV:
	var dur := 5.0
	var n := int(SAMPLE_RATE * dur)
	var f := PackedFloat32Array()
	f.resize(n)
	var rng := RandomNumberGenerator.new()
	rng.randomize()
	var prev := 0.0
	var lfo_phase := rng.randf() * TAU
	for i in n:
		var t := float(i) / SAMPLE_RATE
		lfo_phase += 0.25 * TAU / SAMPLE_RATE
		var amp := 0.10 + 0.07 * (0.5 + 0.5 * sin(lfo_phase + 0.4 * sin(t * 0.3)))
		var white := rng.randf_range(-1.0, 1.0)
		prev = prev * 0.97 + white * 0.03
		f[i] = prev * amp
	# сшиваем края петли кроссфейдом, чтобы не щёлкала
	var xfade := 2048
	for i in xfade:
		var a := float(i) / xfade
		f[i] = lerpf(f[n - xfade + i], f[i], a)
	var s := _make_wav(f)
	s.loop_mode = AudioStreamWAV.LOOP_FORWARD
	s.loop_begin = 0
	s.loop_end = n - 1
	return s


## Кабан: низкий хриплый свинячий тон с шумом.
func _make_grunt() -> AudioStreamWAV:
	var dur := 0.30
	var n := int(SAMPLE_RATE * dur)
	var f := PackedFloat32Array()
	f.resize(n)
	var rng := RandomNumberGenerator.new()
	rng.randomize()
	var phase := 0.0
	for i in n:
		var t := float(i) / n
		var env := sin(t * PI)  # нарастание-спад
		var freq := lerpf(110.0, 65.0, t)
		phase += freq * TAU / SAMPLE_RATE
		var tone := sin(phase) * 0.5 + sin(phase * 2.0) * 0.2
		var noise := rng.randf_range(-1.0, 1.0) * 0.25
		f[i] = (tone + noise) * env * 0.45
	return _make_wav(f)


# ---------- проигрывание ----------

func _play_flat(stream: AudioStreamWAV, volume_db: float) -> void:
	var p := AudioStreamPlayer.new()
	p.stream = stream
	p.volume_db = volume_db
	p.pitch_scale = randf_range(0.94, 1.06)
	p.finished.connect(p.queue_free)
	add_child(p)
	p.play()


func _play_3d(stream: AudioStreamWAV, pos: Vector3, volume_db: float) -> void:
	var scene := get_tree().current_scene
	if scene == null:
		return
	var p := AudioStreamPlayer3D.new()
	p.stream = stream
	p.volume_db = volume_db
	p.pitch_scale = randf_range(0.94, 1.08)
	p.finished.connect(p.queue_free)
	scene.add_child(p)
	p.global_position = pos
	p.play()


func _play_ambient(stream: AudioStreamWAV) -> void:
	var p := AudioStreamPlayer.new()
	p.stream = stream
	p.volume_db = -34.0
	add_child(p)
	p.play()
