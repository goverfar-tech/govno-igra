using UnityEngine;

// Процедурный рельеф по шуму Перлина (аналог terrain.gd из Godot-версии, M4).
// Одна функция HeightAt используется и для меша, и для расстановки объектов.
public static class TerrainGen
{
    public const float Size = 200f;      // карта 200×200 м
    public const float Amp = 5f;         // высота холмов
    public const float Freq = 0.02f;     // частота (пятна ~50 м)

    // Озеро: котловина в рельефе + WaterSource ставится на его центр
    public static readonly Vector2 LakeCenter = new Vector2(55f, 55f);
    public const float LakeRadius = 12f;
    public const float LakeDepth = 3f;

    static readonly Vector2 off1 = new Vector2(13.7f, 7.1f);
    static readonly Vector2 off2 = new Vector2(91.3f, 44.9f);

    public static float HeightAt(float x, float z)
    {
        float h = Mathf.PerlinNoise(x * Freq + off1.x, z * Freq + off1.y) * Amp
                + Mathf.PerlinNoise(x * Freq * 3f + off2.x, z * Freq * 3f + off2.y) * Amp * 0.25f;

        // плато у спауна (центр): ровное место под базу
        float dSpawn = new Vector2(x, z).magnitude;
        h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 30f, dSpawn));

        // котловина озера
        float dLake = Vector2.Distance(new Vector2(x, z), LakeCenter);
        float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LakeRadius * 0.5f, LakeRadius, dLake));
        h -= LakeDepth * (1f - rim);
        return h;
    }

    // res×res квадратов; вершины в локальных координатах, центр в (0,0).
    public static Mesh BuildMesh(int res = 100)
    {
        var mesh = new Mesh { name = "Terrain" };
        int side = res + 1;
        var verts = new Vector3[side * side];
        var uvs = new Vector2[side * side];
        float half = Size / 2f;

        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            float wx = -half + Size * x / res;
            float wz = -half + Size * z / res;
            verts[z * side + x] = new Vector3(wx, HeightAt(wx, wz), wz);
            uvs[z * side + x] = new Vector2((float)x / res, (float)z / res);
        }

        var tris = new int[res * res * 6];
        int t = 0;
        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
        {
            int i0 = z * side + x, i1 = i0 + 1, i2 = i0 + side, i3 = i2 + 1;
            tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
            tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
