using UnityEngine;

// Дрожащий свет костра/факела (§9.6 мрачного визуала).
[RequireComponent(typeof(Light))]
public class FireLight : MonoBehaviour
{
    public float baseIntensity = 2f;
    public float flicker = 0.6f;
    public float speed = 6f;

    Light l;
    float seed;

    void Awake()
    {
        l = GetComponent<Light>();
        seed = Random.value * 100f;
    }

    void Update()
    {
        l.intensity = baseIntensity - flicker * 0.5f
                      + Mathf.PerlinNoise(seed, Time.time * speed) * flicker;
    }
}
