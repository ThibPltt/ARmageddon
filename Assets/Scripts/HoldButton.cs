using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

// Bouton qui signale quand on l'appuie et quand on le relâche (pour les boutons ▲ / ▼ de sculpture).
public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public UnityEvent onPress = new UnityEvent();
    public UnityEvent onRelease = new UnityEvent();
    private bool pressed;

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;
        onPress.Invoke();
    }

    public void OnPointerUp(PointerEventData eventData) => Release();
    public void OnPointerExit(PointerEventData eventData) => Release();
    void OnDisable() => Release();

    private void Release()
    {
        if (!pressed) return;
        pressed = false;
        onRelease.Invoke();
    }
}
