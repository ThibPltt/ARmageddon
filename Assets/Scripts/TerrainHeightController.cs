using System.Collections.Generic;
using UnityEngine;

// Sculpture du relief point par point.
// Mode Sculpter actif : toucher le terrain pose un point blanc (ou sélectionne le point existant le plus proche),
// puis les boutons ▲ / ▼ (maintenus) ou un glissé à deux doigts montent ou creusent le relief autour de ce point.
// Le composant est désactivé par le bouton Lock, ce qui fige le relief.
public class TerrainHeightController : MonoBehaviour
{
    public ProceduralTerrain terrain;

    [Header("Pinceau (unités locales du terrain)")]
    public float brushRadius = 0.5f;         // rayon de la zone sculptée (le terrain fait 4 unités de côté)
    public float raiseSpeed = 0.35f;          // hauteur ajoutée par seconde avec les boutons (vitesse maximale)
    public float buttonRamp = 3f;            // montée en vitesse progressive des boutons
    public float dragSensitivity = 0.0007f;   // glissé à deux doigts : hauteur par pixel glissé
    public float sensitivity = 0.005f;       // ancien réglage, conservé pour le prefab (plus utilisé)
    public float smoothing = 5f;             // plus petit = plus doux (le relief rattrape le doigt en douceur)
    public float maxStepPerSecond = 0.4f;    // vitesse maximale de déformation
    public float minHeight = 0.5f;           // conservés pour compatibilité avec le prefab
    public float maxHeight = 5f;

    [Header("Points")]
    public float markerSize = 0.08f;         // diamètre du point blanc, en unités locales

    public static TerrainHeightController Active { get; private set; }
    public bool SculptMode { get; private set; }
    public bool HasSelection => selected != null;

    private readonly List<Transform> markers = new List<Transform>();
    private Transform selected;
    private int holdDirection;               // +1 monter, -1 creuser, 0 rien
    private float holdSpeed;                 // vitesse actuelle des boutons, qui accélère en douceur
    private float pendingAmount;             // hauteur demandée par le glissé, pas encore appliquée
    private bool waitAllFingersUp;           // après un geste à deux doigts, on ignore le doigt restant
    private bool sculptedThisGesture;
    private Vector2 lastTwoFingerCenter;
    private static Material markerMaterial;

    void Awake()
    {
        if (terrain == null) terrain = GetComponent<ProceduralTerrain>();
    }

    void OnEnable()
    {
        Active = this;
        SetMarkersVisible(true);
    }

    void OnDisable()
    {
        SetSculptMode(false);
        SetMarkersVisible(false);
        if (Active == this) Active = null;
    }

    public void SetSculptMode(bool on)
    {
        SculptMode = on && enabled;
        if (!SculptMode) SetHold(0);
        RefreshMarkerLook();
        if (GameManager.Instance != null) GameManager.Instance.RefreshUI();
    }

    // Appelé par les boutons ▲ / ▼ : +1 quand on appuie sur ▲, -1 sur ▼, 0 au relâchement.
    // Curseur « Taille » : rayon de la zone sculptée (le point blanc grossit avec).
    public void SetBrushRadius(float radius)
    {
        brushRadius = Mathf.Clamp(radius, 0.1f, 2f);
        RefreshMarkerLook();
    }

    public void SetHold(int direction)
    {
        holdDirection = direction;
        if (direction != 0) holdSpeed = 0f;
    }

    void Update()
    {
        if (terrain == null || !SculptMode) return;

        // Deux doigts : glisser vers le haut ou le bas sculpte le point sélectionné.
        // Le glissé n'est pas appliqué d'un coup : il s'ajoute à une réserve que le relief rattrape en douceur.
        bool twoFingers = Input.touchCount == 2;
        if (twoFingers) waitAllFingersUp = true;
        else if (Input.touchCount == 0) waitAllFingersUp = false;
        if (twoFingers)
        {
            Vector2 center = (Input.GetTouch(0).position + Input.GetTouch(1).position) / 2f;
            if (Input.GetTouch(0).phase == TouchPhase.Began || Input.GetTouch(1).phase == TouchPhase.Began)
                lastTwoFingerCenter = center;
            else if (selected != null)
                pendingAmount += (center.y - lastTwoFingerCenter.y) * dragSensitivity;
            lastTwoFingerCenter = center;
        }

        // Boutons maintenus : la vitesse monte progressivement, sans à-coup.
        float target = holdDirection * raiseSpeed;
        holdSpeed = Mathf.MoveTowards(holdSpeed, target, raiseSpeed * buttonRamp * Time.deltaTime);

        float step = 0f;
        if (Mathf.Abs(pendingAmount) > 0.0005f)
        {
            float applied = pendingAmount * (1f - Mathf.Exp(-smoothing * Time.deltaTime));
            applied = Mathf.Clamp(applied, -maxStepPerSecond * Time.deltaTime, maxStepPerSecond * Time.deltaTime);
            pendingAmount -= applied;
            step += applied;
        }
        else
        {
            pendingAmount = 0f;
        }
        step += holdSpeed * Time.deltaTime;

        if (selected != null && Mathf.Abs(step) > 0.00001f)
            SculptSelected(step);

        bool busy = twoFingers || holdDirection != 0 || Mathf.Abs(holdSpeed) > 0.001f || pendingAmount != 0f;
        if (!busy && sculptedThisGesture)
            EndGesture();
        if (busy) return;

        // Un doigt : poser ou choisir un point.
        if (!waitAllFingersUp && Input.touchCount <= 1 && Input.GetMouseButtonDown(0) && !PlacementInput.IsPointerOverUI())
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, LayerMask.GetMask("Ground"))
                && hit.collider.GetComponentInParent<ProceduralTerrain>() == terrain)
            {
                SelectOrCreateMarker(terrain.transform.InverseTransformPoint(hit.point));
            }
        }
    }

    private void SculptSelected(float amount)
    {
        Vector3 local = terrain.transform.InverseTransformPoint(selected.position);
        terrain.Sculpt(local, brushRadius, amount);
        sculptedThisGesture = true;
    }

    private void EndGesture()
    {
        if (!sculptedThisGesture) return;
        sculptedThisGesture = false;
        terrain.FinishSculpt();
    }

    private void SelectOrCreateMarker(Vector3 localPoint)
    {
        Transform nearest = null;
        float best = brushRadius * 0.5f;
        foreach (Transform m in markers)
        {
            if (m == null) continue;
            Vector3 ml = terrain.transform.InverseTransformPoint(m.position);
            float d = Vector2.Distance(new Vector2(ml.x, ml.z), new Vector2(localPoint.x, localPoint.z));
            if (d < best) { best = d; nearest = m; }
        }

        selected = nearest != null ? nearest : CreateMarker(localPoint);
        RefreshMarkerLook();
        if (GameManager.Instance != null) GameManager.Instance.RefreshUI();
    }

    private Transform CreateMarker(Vector3 localPoint)
    {
        if (markerMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            markerMaterial = new Material(shader) { color = Color.white };
        }

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "SculptPoint";
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = markerMaterial;
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.SetParent(terrain.transform, false);
        go.transform.localPosition = new Vector3(localPoint.x, terrain.GetHeightAtPosition(localPoint.x, localPoint.z), localPoint.z);
        go.transform.localScale = Vector3.one * markerSize;

        terrain.RegisterPlacedObject(go.transform); // le point suit le relief
        markers.Add(go.transform);
        return go.transform;
    }

    // Le point sélectionné est plus gros que les autres.
    private void RefreshMarkerLook()
    {
        markers.RemoveAll(m => m == null);
        foreach (Transform m in markers)
            m.localScale = Vector3.one * markerSize * (m == selected && SculptMode ? 1.6f * brushRadius / 0.5f : 1f);
    }

    private void SetMarkersVisible(bool visible)
    {
        markers.RemoveAll(m => m == null);
        foreach (Transform m in markers)
            m.gameObject.SetActive(visible);
    }
}
