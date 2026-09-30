using UnityEngine;

// Процедурный синтез звуков без аудио-ассетов — порт генераторов из
// Godot-эталона scripts/autoload/audio_manager.gd (те же формулы, частоты
// и sample rate 22050), плюс майонезные добавки по §9.6 (плеск, чавканье).
// Генерация идёт в float-сэмплах [-1, 1], на выходе — готовый AudioClip.
public static class ProceduralSfx
{
    public const int SampleRate = 22050; // как в эталоне

    static AudioClip MakeClip(string name, float[] f)
    {
        for (int i = 0; i < f.Length; i++) f[i] = Mathf.Clamp(f[i], -1f, 1f);
        var clip = AudioClip.Create(name, f.Length, 1, SampleRate, false);
        clip.SetData(f, 0);
        return clip;
    }

    static float White() => Random.Range(-1f, 1f);

    // Короткий слайд-тон (подбор — вверх, еда — вниз). Порт _make_blip.
    public static AudioClip MakeBlip(string name, float from, float to, float dur)
    {
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float env = Mathf.Exp(-5f * t);
            float freq = Mathf.Lerp(from, to, t);
            phase += freq * Mathf.PI * 2f / SampleRate;
            f[i] = Mathf.Sin(phase) * env * 0.22f;
        }
        return MakeClip(name, f);
    }

    // Шаг шагом: глухой мягкий шлепок (сильно фильтрованный шум, быстрый спад).
    public static AudioClip MakeStep()
    {
        int n = Mathf.RoundToInt(SampleRate * 0.12f);
        var f = new float[n];
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float env = Mathf.Exp(-26f * i / n);
            prev = prev * 0.82f + White() * 0.18f;
            f[i] = prev * env * 0.35f;
        }
        return MakeClip("sfx_step", f);
    }

    // Шаг бегом: мокрый «чвяк» — громкий плеск вытекающего майонеза (§9.2.3:
    // при беге ведро слышно и игроку, и тем, кто идёт по следу).
    public static AudioClip MakeStepSplash()
    {
        float dur = 0.22f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float prev = 0f, phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float env = Mathf.Exp(-11f * t);
            float freq = Mathf.Lerp(85f, 40f, t / dur);
            phase += freq * Mathf.PI * 2f / SampleRate;
            float tone = Mathf.Sin(phase) * 0.30f;
            prev = prev * 0.72f + White() * 0.28f;
            f[i] = (tone + prev * 0.9f) * env * 0.5f;
        }
        return MakeClip("sfx_step_splash", f);
    }

    // Удар по дереву/камню: короткий глубокий «тук». Порт _make_hit.
    public static AudioClip MakeHit()
    {
        float dur = 0.18f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float env = Mathf.Exp(-18f * t);
            float freq = Mathf.Lerp(150f, 60f, t / dur);
            phase += freq * Mathf.PI * 2f / SampleRate;
            float tone = Mathf.Sin(phase) * 0.65f;
            float noise = White() * 0.18f;
            f[i] = (tone + noise) * env * 0.4f;
        }
        return MakeClip("sfx_hit", f);
    }

    // Свист взмаха инструментом: короткий падающий шум. Порт _make_whoosh.
    public static AudioClip MakeWhoosh()
    {
        float dur = 0.14f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = (float)i / n;
            float env = Mathf.Sin(u * Mathf.PI);
            prev = prev * 0.6f + White() * 0.4f;
            f[i] = prev * env * 0.3f;
        }
        return MakeClip("sfx_whoosh", f);
    }

    // Ветер: глубокий «дышащий» гул с редкими свистящими порывами (§9.6).
    // Порт _make_wind; сверху добавлен полосовой шум-порыв для ночной тоски.
    // Края сшиты кроссфейдом — клип зацикливается без щелчка.
    public static AudioClip MakeWind(float dur = 6f)
    {
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float prev = 0f;
        float lfoPhase = Random.value * Mathf.PI * 2f;
        float lpFast = 0f, lpSlow = 0f; // разность двух ФНЧ = полосовой шум свиста
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            lfoPhase += 0.25f * Mathf.PI * 2f / SampleRate;
            float amp = 0.10f + 0.07f * (0.5f + 0.5f * Mathf.Sin(lfoPhase + 0.4f * Mathf.Sin(t * 0.3f)));
            float white = White();
            prev = prev * 0.97f + white * 0.03f;

            lpFast = lpFast * 0.90f + white * 0.10f;
            lpSlow = lpSlow * 0.998f + white * 0.002f;
            float gustPhase = 2f * Mathf.PI * 0.09f * t + 1.3f * Mathf.Sin(t * 0.23f);
            float gust = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(gustPhase), 2f);
            float whistle = (lpFast - lpSlow) * gust * 2.4f;

            f[i] = prev * amp + whistle;
        }
        const int xfade = 2048;
        for (int i = 0; i < xfade; i++)
        {
            float a = (float)i / xfade;
            f[i] = Mathf.Lerp(f[n - xfade + i], f[i], a);
        }
        return MakeClip("sfx_wind", f);
    }

    // Кабан: низкий хриплый свинячий тон с шумом. Порт _make_grunt (M5).
    public static AudioClip MakeGrunt()
    {
        float dur = 0.30f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = (float)i / n;
            float env = Mathf.Sin(u * Mathf.PI);
            float freq = Mathf.Lerp(110f, 65f, u);
            phase += freq * Mathf.PI * 2f / SampleRate;
            float tone = Mathf.Sin(phase) * 0.5f + Mathf.Sin(phase * 2f) * 0.2f;
            float noise = White() * 0.25f;
            f[i] = (tone + noise) * env * 0.45f;
        }
        return MakeClip("sfx_grunt", f);
    }

    // Далёкое «чавканье» в тумане (§9.6): низкий воблый тон с придыханием
    // шума. Ночная атмосфера — звук должен пугать, а не оправдываться.
    public static AudioClip MakeGurgle()
    {
        float dur = 0.85f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float phase = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = (float)i / n;          // 0..1 внутри клипа
            float t = (float)i / SampleRate; // секунды
            float env = Mathf.Sin(u * Mathf.PI);
            env *= env;
            float freq = Mathf.Lerp(78f, 46f, u) * (1f + 0.22f * Mathf.Sin(2f * Mathf.PI * 6.5f * t));
            phase += freq * Mathf.PI * 2f / SampleRate;
            float tone = Mathf.Sin(phase) * 0.5f + Mathf.Sin(phase * 1.5f) * 0.16f;
            prev = prev * 0.90f + White() * 0.10f;
            f[i] = (tone + prev * 0.55f) * env * 0.4f;
        }
        return MakeClip("sfx_gurgle", f);
    }

    // Стинг гибели игрока: низкий падающий вой с шумовым хвостом.
    public static AudioClip MakeDeathSting()
    {
        float dur = 1.4f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float phase = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float env = Mathf.Exp(-2.2f * t);
            float freq = Mathf.Lerp(240f, 38f, Mathf.Pow(t / dur, 0.7f));
            phase += freq * Mathf.PI * 2f / SampleRate;
            float tone = Mathf.Sin(phase) * 0.55f + Mathf.Sin(phase * 0.5f) * 0.22f;
            prev = prev * 0.9f + White() * 0.1f;
            f[i] = (tone + prev * 0.5f) * env * 0.5f;
        }
        return MakeClip("sfx_death", f);
    }
}
