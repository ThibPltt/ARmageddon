using UnityEngine;

// Éteint la lumière d'une explosion en 0,25 s puis la supprime.
public class FlashFade : MonoBehaviour
{
    private Light flash;
    private float start;

    void Awake() { flash = GetComponent<Light>(); start = flash.intensity; }

    void Update()
    {
        flash.intensity -= start * Time.deltaTime / 0.25f;
        if (flash.intensity <= 0f) Destroy(gameObject);
    }
}
