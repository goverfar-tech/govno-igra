// Протокол взаимодействия (аналог Interactable из Godot-версии).
// Всё, с чем можно взаимодействовать лучом из камеры, реализует этот интерфейс.
public interface IInteractable
{
    string GetPrompt();
    void Interact(Player player);
}
