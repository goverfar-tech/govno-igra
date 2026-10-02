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

    // Стинг гибели игрока: низкий провальный «бум» (72→26 Гц) + дребезг —
    // короткая серия шумовых щелчков с затуханием, как содрогание ведра.
    public static AudioClip MakeDeathSting()
    {
        float dur = 1.6f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float env = Mathf.Exp(-3.2f * t);
            float freq = Mathf.Lerp(72f, 26f, Mathf.Pow(t / dur, 0.8f));
            phase += freq * Mathf.PI * 2f / SampleRate;
            f[i] = (Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 0.5f) * 0.3f) * env * 0.6f;
        }
        // дребезг: 8 щелчков с шагом ~55 мс, каждый слабее предыдущего
        for (int k = 0; k < 8; k++)
        {
            int s0 = Mathf.RoundToInt((0.02f + k * 0.055f + Random.value * 0.015f) * SampleRate);
            int m = Mathf.RoundToInt(SampleRate * 0.04f);
            float amp = 0.35f * (1f - k / 9f);
            for (int j = 0; j < m && s0 + j < n; j++)
                f[s0 + j] += White() * Mathf.Exp(-j / (m * 0.35f)) * amp;
        }
        return MakeClip("sfx_death", f);
    }

    // UI-шорошок: открытие панели «раскрывается» к верхам, закрытие —
    // сворачивается вниз (меняется глубина ФНЧ по ходу клипа).
    public static AudioClip MakeRustle(bool opening)
    {
        float dur = 0.11f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = (float)i / n;
            float env = Mathf.Sin(u * Mathf.PI);
            float a = opening ? Mathf.Lerp(0.75f, 0.2f, u) : Mathf.Lerp(0.2f, 0.75f, u);
            prev = prev * a + White() * (1f - a);
            f[i] = prev * env * 0.5f;
        }
        return MakeClip(opening ? "sfx_ui_open" : "sfx_ui_close", f);
    }

    // Треск костра: глухой «гул горения» + случайные деревянные щелчки
    // (~26 в секунду, гаснут за миллисекунды). Петля, края сшиты кроссфейдом.
    public static AudioClip MakeCrackle(float dur = 3.5f)
    {
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float bed = 0f, pop = 0f, popLp = 0f;
        for (int i = 0; i < n; i++)
        {
            float white = White();
            bed = bed * 0.985f + white * 0.015f;
            if (Random.value < 0.0012f)
                pop = Random.Range(0.5f, 1f) * (Random.value < 0.5f ? -1f : 1f);
            pop *= 0.86f;
            popLp = popLp * 0.55f + pop * 0.45f;
            f[i] = bed * 0.4f + popLp * 0.5f;
        }
        const int xfade = 1024;
        for (int i = 0; i < xfade; i++)
        {
            float a = (float)i / xfade;
            f[i] = Mathf.Lerp(f[n - xfade + i], f[i], a);
        }
        return MakeClip("sfx_crackle", f);
    }

    // Гул-стон зомби: низкий пилящий тон (пила 58 Гц с вибрато, приглаженная
    // ФНЧ, чтобы пугала, а не резала слух) поверх шумового дыхания. Петля;
    // «дыхание» делает целое число волн за петлю — шва не слышно. Громкостью
    // по расстоянию управляет AudioManager.
    public static AudioClip MakeZombieMoan(float dur = 4f)
    {
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float phase = 0f, saw = 0f, noise = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float u = (float)i / n;
            float freq = 58f * (1f + 0.03f * Mathf.Sin(2f * Mathf.PI * 4.3f * t));
            phase += freq * Mathf.PI * 2f / SampleRate;
            float cycles = phase / (Mathf.PI * 2f);
            float sawRaw = 2f * (cycles - Mathf.Floor(cycles + 0.5f)); // -1..1
            saw = saw * 0.86f + sawRaw * 0.14f;
            noise = noise * 0.96f + White() * 0.04f;
            float swell = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 2f * u); // 2 волны за петлю
            f[i] = (saw * 0.5f + noise * 0.6f) * swell * 0.5f;
        }
        const int xfade = 2048;
        for (int i = 0; i < xfade; i++)
        {
            float a = (float)i / xfade;
            f[i] = Mathf.Lerp(f[n - xfade + i], f[i], a);
        }
        return MakeClip("sfx_zombie_moan", f);
    }

    // Сердцебиение: «луб-дуп» в тишине, петля period секунд (~1.2 с между
    // парами ударов). Включением и громкостью управляет AudioManager.
    public static AudioClip MakeHeartbeat(float period = 1.2f)
    {
        int n = Mathf.RoundToInt(SampleRate * period);
        var f = new float[n];
        AddThump(f, 0.00f, 62f, 44f, 0.10f, 0.9f);
        AddThump(f, 0.17f, 55f, 40f, 0.09f, 0.55f);
        return MakeClip("sfx_heartbeat", f);
    }

    // Один глухой удар сердца: падающий синус с намёком на шум.
    static void AddThump(float[] f, float at, float from, float to, float dur, float amp)
    {
        int s0 = Mathf.RoundToInt(at * SampleRate);
        int n = Mathf.RoundToInt(SampleRate * dur);
        float phase = 0f;
        for (int j = 0; j < n && s0 + j < f.Length; j++)
        {
            float t = (float)j / SampleRate;
            float env = Mathf.Exp(-t * 34f);
            float freq = Mathf.Lerp(from, to, t / dur);
            phase += freq * Mathf.PI * 2f / SampleRate;
            f[s0 + j] += (Mathf.Sin(phase) * 0.8f + White() * 0.05f) * env * amp;
        }
    }

    // Далёкий ночной «вскрик» (§9.6): призрачный падающий вой 520→170 Гц
    // с дрожью и шумовым придыханием. Играется тихо и со stereo-разносом —
    // непонятно, откуда; это и пугает.
    public static AudioClip MakeNightCry()
    {
        float dur = 1.1f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var f = new float[n];
        float phase = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = (float)i / n;
            float t = (float)i / SampleRate;
            float env = Mathf.Exp(-3.5f * t) * Mathf.Min(1f, t * 9f); // быстрый вход, долгий хвост
            float freq = Mathf.Lerp(520f, 170f, Mathf.Pow(u, 0.6f))
                         * (1f + 0.06f * Mathf.Sin(2f * Mathf.PI * 5.7f * t));
            phase += freq * Mathf.PI * 2f / SampleRate;
            float tone = Mathf.Sin(phase) * 0.42f + Mathf.Sin(phase * 2.01f) * 0.12f;
            prev = prev * 0.9f + White() * 0.1f;
            f[i] = (tone + prev * 0.25f) * env * 0.35f;
        }
        return MakeClip("sfx_night_cry", f);
    }
}
