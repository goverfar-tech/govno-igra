extends Node
## AudioManager (AutoLoad): процедурные звуки без ассетов.
## _make_*  генерирует AudioStreamWAV в памяти один раз при старте.
## play_*   играют звук (плоский или позиционный 3D).

const SAMPLE_RATE := 22050

var _step_stream: AudioStreamWAV
var _hit_stream: AudioStreamWAV
var _pickup_stream: AudioStreamWAV
var _eat_stream: AudioStreamWAV


func _ready() -> void:
	_step_stream = _make_step()
	_hit_stream = _make_hit()
	_pickup_stream = _make_blip(880.0, 1320.0, 0.12)
	_eat_stream = _make_blip(520.0, 300.0, 0.25)
	_play_ambient(_make_wind())


func play_step(volume_db: float) -> void:
	_play_flat(_step_stream, volume_db)


func play_hit(pos: Vector3) -> void:
	_play_3d(_hit_stream, pos, -14.0)


func play_pickup() -> void:
	_play_flat(_pickup_stream, -12.0)


func play_eat() -> void:
	_play_flat(_eat_stream, -13.0)


# ---------- генерация ----------

func _make_wav(samples: PackedByteArray) -> AudioStreamWAV:
	var s := AudioStreamWAV.new()
	s.format = AudioStreamWAV.FORMAT_8_BITS
	s.mix_rate = SAMPLE_RATE
	s.data = samples
	return s


## Глухой шлепок: отфильтрованный шум с быстрым спадом.
func _make_step() -> AudioStreamWAV:
	var n := int(SAMPLE_RATE * 0.12)
	var data := PackedByteArray()
	data.resize(n)
	var rng := RandomNumberGenerator.new()
	rng.randomize()
	var prev := 0.0
	for i in n:
		var env := exp(-30.0 * i / n)
		var white := rng.randf_range(-1.0, 1.0)
		prev = prev * 0.7 + white * 0.3
		data[i] = int(clampf(prev * env * 0.45, -1.0, 1.0) * 127.0 + 128.0)
	return _make_wav(data)


## Удар по дереву/камню: короткий глубокий тук.
func _make_hit() -> AudioStreamWAV:
	var n := int(SAMPLE_RATE * 0.18)
	var data := PackedByteArray()
	data.resize(n)
	var rng := RandomNumberGenerator.new()
	rng.randomize()
	var phase := 0.0
	for i in n:
		var t := float(i) / SAMPLE_RATE
		var env := exp(-18.0 * t)
		var freq := lerpf(160.0, 60.0, t / 0.18)
		phase += freq * TAU / SAMPLE_RATE
		var tone := sin(phase) * 0.8
		var noise := rng.randf_range(-1.0, 1.0) * 0.25
		data[i] = int(clampf((tone + noise) * env * 0.5, -1.0, 1.0) * 127.0 + 128.0)
	return _make_wav(data)


## Короткий слайд-тон (подбор вверх, еда вниз).
func _make_blip(f_from: float, f_to: float, dur: float) -> AudioStreamWAV:
	var n := int(SAMPLE_RATE * dur)
	var data := PackedByteArray()
	data.resize(n)
	var phase := 0.0
	for i in n:
		var t := float(i) / n
		var env := exp(-5.0 * t)
		var freq := lerpf(f_from, f_to, t)
		phase += freq * TAU / SAMPLE_RATE
		data[i] = int(clampf(sin(phase) * env * 0.3, -1.0, 1.0) * 127.0 + 128.0)
	return _make_wav(data)


## Петля ветра: медленно «дышащий» шум, края сшиты кроссфейдом.
func _make_wind() -> AudioStreamWAV:
	var dur := 3.0
	var n := int(SAMPLE_RATE * dur)
	var data := PackedByteArray()
	data.resize(n)
	var rng := RandomNumberGenerator.new()
	rng.randomize()
	var prev := 0.0
	var lfo_phase := rng.randf() * TAU
	for i in n:
		var t := float(i) / SAMPLE_RATE
		lfo_phase += 0.35 * TAU / SAMPLE_RATE
		var amp := 0.2 + 0.15 * sin(lfo_phase + 0.5 * sin(t * 0.6))
		var white := rng.randf_range(-1.0, 1.0)
		prev = prev * 0.92 + white * 0.08  # сильный ФНЧ = гул
		data[i] = int(clampf(prev * amp, -1.0, 1.0) * 127.0 + 128.0)
	# сшиваем края, чтобы петля не щёлкала
	var xfade := 512
	for i in xfade:
		var a := float(i) / xfade
		data[i] = int(lerpf(float(data[i]), float(data[n - xfade + i]), 0.0 * a + (1.0 - a)))
		data[n - xfade + i] = int(lerpf(float(data[n - xfade + i]), float(data[i]), a))
	var s := _make_wav(data)
	s.loop_mode = AudioStreamWAV.LOOP_FORWARD
	s.loop_begin = 0
	s.loop_end = n - 1
	return s


# ---------- проигрывание ----------

func _play_flat(stream: AudioStreamWAV, volume_db: float) -> void:
	var p := AudioStreamPlayer.new()
	p.stream = stream
	p.volume_db = volume_db
	p.pitch_scale = randf_range(0.92, 1.08)
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
	p.pitch_scale = randf_range(0.92, 1.12)
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
