class_name Terrain
extends StaticBody3D
## Процедурный ландшафт: сетка по шуму Перлина, вершинные цвета,
## коллизия HeightMapShape3D. Один экземпляр на мир, генерируется при старте.

@export var size := 200.0
@export var segments := 100          # ячеек сетки (сегментов) по стороне
@export var height_scale := 9.0      # амплитуда холмов
@export var noise_seed := 1337
## Вокруг спауна (0, 0) — равнина
@export var spawn_flat_radius := 15.0
## Чаша озера
@export var lake_center := Vector2(35.0, -35.0)
@export var lake_radius := 10.0
@export var lake_depth := 4.0

var heights: PackedFloat32Array
var resolution: int                  # вершин по стороне (segments + 1)

# зонирование цвета рельефа (см. world_gen.gd — роща/трасса)
var GROVE_CENTER := Vector2(-45.0, -40.0)
var _tint_noise := FastNoiseLite.new()


func _ready() -> void:
	_tint_noise.seed = noise_seed + 7
	_tint_noise.noise_type = FastNoiseLite.TYPE_PERLIN
	_tint_noise.frequency = 12.0 / size
	_generate()


## Высота рельефа в мировой точке (x, z), билинейная интерполяция.
func height_at(x: float, z: float) -> float:
	if heights.is_empty():
		return 0.0
	var fx := clampf((x / size + 0.5) * segments, 0.0, segments - 0.001)
	var fz := clampf((z / size + 0.5) * segments, 0.0, segments - 0.001)
	var x0 := int(fx)
	var z0 := int(fz)
	var tx := fx - x0
	var tz := fz - z0
	var r := resolution
	var h00: float = heights[z0 * r + x0]
	var h10: float = heights[z0 * r + x0 + 1]
	var h01: float = heights[(z0 + 1) * r + x0]
	var h11: float = heights[(z0 + 1) * r + x0 + 1]
	return lerpf(lerpf(h00, h10, tx), lerpf(h01, h11, tx), tz)


func _generate() -> void:
	resolution = segments + 1
	heights.resize(resolution * resolution)

	var noise := FastNoiseLite.new()
	noise.seed = noise_seed
	noise.noise_type = FastNoiseLite.TYPE_PERLIN
	noise.frequency = 2.2 / size
	noise.fractal_octaves = 4
	noise.fractal_gain = 0.5

	for z in resolution:
		for x in resolution:
			var wx := (float(x) / segments - 0.5) * size
			var wz := (float(z) / segments - 0.5) * size
			var h := (noise.get_noise_2d(wx, wz) * 0.5 + 0.5) * height_scale
			# площадка спауна
			var d_spawn := Vector2(wx, wz).length()
			h *= smoothstep(4.0, spawn_flat_radius, d_spawn)
			# чаша озера
			var d_lake := Vector2(wx, wz).distance_to(lake_center)
			h -= (1.0 - smoothstep(0.0, lake_radius, d_lake)) * lake_depth
			heights[z * resolution + x] = h

	_build_mesh()
	_build_collision()


func _vertex_pos(x: int, z: int) -> Vector3:
	var wx := (float(x) / segments - 0.5) * size
	var wz := (float(z) / segments - 0.5) * size
	return Vector3(wx, heights[z * resolution + x], wz)


func _color_for(wx: float, wz: float, h: float) -> Color:
	# базовая трава с лёгкой пятнистостью
	var t := _tint_noise.get_noise_2d(wx, wz) * 0.5 + 0.5
	var col := Color(0.2, 0.3, 0.17).lerp(Color(0.27, 0.37, 0.21), t)

	var p := Vector2(wx, wz)
	# роща — темнее и сочнее
	var d_grove := p.distance_to(GROVE_CENTER)
	if d_grove < 30.0:
		col = col.lerp(Color(0.16, 0.28, 0.14), 1.0 - smoothstep(18.0, 30.0, d_grove))
	# трасса — серая полоса (диагональ z = 4/11 x - 25)
	var d_road := absf(0.364 * wx - wz - 25.0) / 1.064
	if d_road < 4.0:
		col = col.lerp(Color(0.32, 0.32, 0.33), 1.0 - smoothstep(2.0, 4.0, d_road))
	# берег озера — тёмная влажная земля
	var d_lake := p.distance_to(lake_center)
	if d_lake < lake_radius + 3.0:
		col = col.lerp(Color(0.2, 0.22, 0.16), 1.0 - smoothstep(lake_radius * 0.5, lake_radius + 3.0, d_lake))
	# каменистые склоны и вершины
	if h > 5.5:
		col = col.lerp(Color(0.48, 0.47, 0.5), smoothstep(5.5, 8.0, h))
	return col


func _build_mesh() -> void:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	for z in segments:
		for x in segments:
			var v00 := _vertex_pos(x, z)
			var v10 := _vertex_pos(x + 1, z)
			var v01 := _vertex_pos(x, z + 1)
			var v11 := _vertex_pos(x + 1, z + 1)
			st.set_color(_color_for(v00.x, v00.z, v00.y))
			st.add_vertex(v00)
			st.set_color(_color_for(v10.x, v10.z, v10.y))
			st.add_vertex(v10)
			st.set_color(_color_for(v01.x, v01.z, v01.y))
			st.add_vertex(v01)

			st.set_color(_color_for(v10.x, v10.z, v10.y))
			st.add_vertex(v10)
			st.set_color(_color_for(v11.x, v11.z, v11.y))
			st.add_vertex(v11)
			st.set_color(_color_for(v01.x, v01.z, v01.y))
			st.add_vertex(v01)

	st.generate_normals()

	var mesh := st.commit()
	var mat := StandardMaterial3D.new()
	mat.vertex_color_use_as_albedo = true
	mat.roughness = 1.0
	mesh.surface_set_material(0, mat)

	var mi := MeshInstance3D.new()
	mi.mesh = mesh
	add_child(mi)


func _build_collision() -> void:
	var shape := HeightMapShape3D.new()
	shape.map_width = resolution
	shape.map_depth = resolution
	shape.map_data = heights
	var col := CollisionShape3D.new()
	col.shape = shape
	# HeightMapShape3D имеет шаг 1 м между вершинами — растягиваем на 2 м
	col.scale = Vector3(size / segments, 1.0, size / segments)
	add_child(col)
