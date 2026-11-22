using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

// Pose du terrain sur une vraie surface : un viseur suit le sol détecté au centre de l'écran,
// le joueur règle la taille puis touche « Poser le terrain ». Le terrain est posé exactement
// à l'endroit visé, tourné face au joueur, à la hauteur du plan touché.
public class ARPlacement : MonoBehaviour
{
    public ARPlaneManager planeManager;
    public GameObject terrainPrefab;
    public GameObject lockButton;

    [Header("Taille réelle du terrain")]
    [Tooltip("Côté du terrain dans la pièce, en mètres. 2 m de côté = 4 m².")]
    public float terrainSizeMeters = 2f;
    public float minSizeMeters = 1f;
    public float maxSizeMeters = 3f;

    public static ARPlacement Instance { get; private set; }
    public bool IsPlaced => isPlaced;
    public bool HasValidSpot => hasSpot;
    public float SizeMeters => terrainSizeMeters;

    private GameObject spawnedTerrain;
    private bool isPlaced;
    private XROrigin origin;
    private ARRaycastManager raycastManager;
    private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

    // Viseur : le contour du futur terrain, posé sur le sol visé.
    private LineRenderer reticle;
    private bool hasSpot;
    private Pose spot;
    private ARPlane spotPlane;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        origin = FindObjectOfType<XROrigin>();
        if (origin != null)
        {
            raycastManager = origin.GetComponent<ARRaycastManager>();
            if (raycastManager == null) raycastManager = origin.gameObject.AddComponent<ARRaycastManager>();
        }
        ApplyWorldScale(terrainSizeMeters, null);
        CreateReticle();
    }

    private float TerrainUnits
    {
        get
        {
            ProceduralTerrain pt = terrainPrefab != null ? terrainPrefab.GetComponent<ProceduralTerrain>() : null;
            return pt != null ? Mathf.Max(pt.width, pt.height) : 4f;
        }
    }

    // Change la taille réelle du terrain (curseur « Taille du terrain »).
    public void SetTerrainSize(float meters)
    {
        meters = Mathf.Clamp(meters, minSizeMeters, maxSizeMeters);
        if (Mathf.Approximately(meters, terrainSizeMeters)) return;
        terrainSizeMeters = meters;

        // Le point qui ne doit pas bouger dans la pièce : le centre du terrain, ou le sol visé.
        Vector3? pivot = null;
        if (spawnedTerrain != null)
        {
            ProceduralTerrain pt = spawnedTerrain.GetComponent<ProceduralTerrain>();
            pivot = spawnedTerrain.transform.TransformPoint(new Vector3(pt.width / 2f, 0f, pt.height / 2f));
        }
        else if (hasSpot) pivot = spot.position;

        ApplyWorldScale(meters, pivot);
    }

    // Méthode recommandée par AR Foundation pour une scène miniature : on agrandit l'XR Origin
    // plutôt que de rétrécir le contenu. Le terrain garde ses unités de jeu (NavMesh, physique),
    // mais apparaît à la taille voulue dans la pièce. Le pivot reste au même endroit de la pièce.
    private void ApplyWorldScale(float meters, Vector3? pivot)
    {
        if (origin == null || meters <= 0f) return;
        Transform o = origin.transform;
        float k = TerrainUnits / meters;

        if (pivot.HasValue)
        {
            // Position du pivot dans l'espace de la session AR, puis nouvelle position de l'origine
            // pour que ce point de la pièce corresponde toujours au même point du monde de jeu.
            Vector3 p = pivot.Value;
            float s = o.localScale.x;
            Vector3 sessionPoint = Quaternion.Inverse(o.rotation) * (p - o.position) / s;
            o.localScale = Vector3.one * k;
            o.position = p - o.rotation * (sessionPoint * k);
        }
        else
        {
            o.localScale = Vector3.one * k;
        }

        if (origin.Camera != null)
            origin.Camera.farClipPlane = Mathf.Max(origin.Camera.farClipPlane, 20f * k);
    }

    void Update()
    {
        bool started = GameManager.Instance == null || GameManager.Instance.HasStarted;
        if (isPlaced || !started || raycastManager == null)
        {
            if (reticle != null) reticle.enabled = false;
            return;
        }

        // Sol visé au centre de l'écran : seulement un plan horizontal tourné vers le haut, à l'intérieur de son contour.
        bool hadSpot = hasSpot;
        hasSpot = false;
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
        if (raycastManager.Raycast(center, hits, TrackableType.PlaneWithinPolygon))
        {
            foreach (ARRaycastHit hit in hits)
            {
                ARPlane plane = planeManager != null ? planeManager.GetPlane(hit.trackableId) : null;
                if (plane != null && plane.alignment != PlaneAlignment.HorizontalUp) continue;
                spot = hit.pose;
                spotPlane = plane;
                hasSpot = true;
                break;
            }
        }

        UpdateReticle();
        if (hadSpot != hasSpot && GameManager.Instance != null) GameManager.Instance.RefreshUI();
    }

    // Rotation du terrain : un bord face au joueur.
    private Quaternion FacingRotation()
    {
        Camera cam = origin != null ? origin.Camera : Camera.main;
        if (cam == null) return Quaternion.identity;
        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        return fwd.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(fwd.normalized, Vector3.up);
    }

    private void CreateReticle()
    {
        var go = new GameObject("PlacementReticle");
        reticle = go.AddComponent<LineRenderer>();
        reticle.loop = true;
        reticle.positionCount = 4;
        reticle.useWorldSpace = true;
        reticle.widthMultiplier = 0.04f;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("UI/Default");
        reticle.material = new Material(shader);
        reticle.startColor = reticle.endColor = new Color(0.95f, 0.70f, 0.24f, 0.95f);
        reticle.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        reticle.enabled = false;
    }

    private void UpdateReticle()
    {
        if (reticle == null) return;
        reticle.enabled = hasSpot;
        if (!hasSpot) return;

        float half = TerrainUnits / 2f;
        Quaternion rot = FacingRotation();
        Vector3 c = spot.position + Vector3.up * 0.01f;
        reticle.SetPosition(0, c + rot * new Vector3(-half, 0f, -half));
        reticle.SetPosition(1, c + rot * new Vector3(half, 0f, -half));
        reticle.SetPosition(2, c + rot * new Vector3(half, 0f, half));
        reticle.SetPosition(3, c + rot * new Vector3(-half, 0f, half));
    }

    // Bouton « Poser le terrain ».
    public void PlaceTerrain()
    {
        if (isPlaced || !hasSpot || terrainPrefab == null) return;

        Quaternion rot = FacingRotation();
        spawnedTerrain = Instantiate(terrainPrefab, Vector3.zero, rot);
        ProceduralTerrain terrainScript = spawnedTerrain.GetComponent<ProceduralTerrain>();
        if (terrainScript == null) return;

        terrainScript.offset = new Vector2(spot.position.x, spot.position.z);
        terrainScript.GenerateBaseTerrain();

        // Le centre du terrain sur le point visé (le pivot du terrain est son coin).
        float halfWidth = terrainScript.width / 2f;
        float halfHeight = terrainScript.height / 2f;
        spawnedTerrain.transform.position = spot.position - rot * new Vector3(halfWidth, 0f, halfHeight);

        terrainScript.AnimateUp();
        isPlaced = true;
        reticle.enabled = false;

        // Les autres plans détectés ne servent plus : on les cache pour dégager la vue.
        if (planeManager != null)
        {
            foreach (ARPlane plane in planeManager.trackables)
                plane.gameObject.SetActive(false);
            planeManager.requestedDetectionMode = PlaneDetectionMode.None;
        }

        GameAudio.Place();
        Haptics.Tap(40);

        if (GameManager.Instance != null)
            GameManager.Instance.OnTerrainSpawned(terrainScript);
    }
}
