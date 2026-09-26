# говно игра (survival)

Одиночная 3D FPS survival-игра на Godot 4.7.
План разработки и память проекта: [MEMORY.md](MEMORY.md)

## Быстрый старт (разработчик)

1. Скачай и распакуй **Godot 4.7.2** (обычный, не .NET):
   https://godotengine.org/download/archive/4.7.2-stable/
2. Клонируй репозиторий:
   ```
   git clone https://github.com/goverfar-tech/govno-igra.git
   ```
3. В проект-менеджере Godot: **Import** → выбери `project.godot` в папке репозитория
4. **F5** — игра запускается. Шаблоны экспорта для разработки НЕ нужны.

## Сборка .exe (экспорт)

1. В редакторе: **Editor → Manage Export Templates → Download and Install**
   (нужно один раз, ~1 ГБ; версия шаблонов должна быть 4.7.2-stable)
2. **Project → Export… → Windows Desktop → Export Project** —
   готовый `.exe` появится в папке `builds/` (в Git не коммитится)
3. Чтобы отправить другу игру: архивируй содержимое `builds/`,
   ему нужен только `.exe` + лежащий рядом `.pck`

Сборка из командной строки (после установки шаблонов):
```
Godot_v4.7.2-stable_win64.exe --headless --path . --export-release "Windows Desktop" builds/survival.exe
```

## Управление в прототипе

- WASD — движение, Shift — бег, Пробел — прыжок, Ctrl (зажать) — присед
- Мышь — камера, E — взаимодействие, Esc — освободить курсор
