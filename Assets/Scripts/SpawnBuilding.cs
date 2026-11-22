using UnityEngine;
using UnityEngine.AI;

public class SpawnBuilding : MonoBehaviour
{
    [Header("Paramètres de placement")]
    public GameObject buildingPrefab;        // Prefab du bâtiment
    public Camera mainCamera;                // Caméra principale
    public LayerMask terrainLayer;           // Layer du terrain : Ground uniquement
    public Vector3 buildingScale = new Vector3(0.1f, 0.1f, 0.1f); // Taille du bâtiment (réglée dans la scène)
    public float sizeFactor = 0.75f;          // réduction appliquée à cette taille

    private bool isPlacing = false;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    void Update()
    {
        // Un seul doigt : le geste à deux doigts sert à sculpter le relief, pas à placer.
        if (!isPlacing || Input.touchCount > 1 || !Input.GetMouseButtonDown(0) || PlacementInput.IsPointerOverUI())
            return;

        GameManager gm = GameManager.Instance;
        if (gm != null && !gm.CanPlaceBuilding(out string reason))
        {
            gm.ShowMessage(reason);
            isPlacing = false;
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, terrainLayer))
            return;

        Team team = gm != null ? gm.PlacingTeam : Team.Blue;
        if (gm != null && !gm.IsInZone(hit.point, team))
        {
            gm.ShowMessage(team == Team.Blue ? "Bâtiments bleus : de ton côté de la ligne." : "Bâtiments rouges : de l'autre côté de la ligne.");
            return;
        }

        ProceduralTerrain terrain = hit.collider.GetComponentInParent<ProceduralTerrain>();
        Quaternion facing = gm != null ? gm.FacingFor(team) : Quaternion.identity;
        SpawnAt(hit.point, facing, terrain, team, true);

        // Le mode reste actif : on pose plusieurs bâtiments d'affilée, jusqu'au maximum.
        if (gm == null) isPlacing = false;
    }

    // Pose un bâtiment (aussi utilisé par « Rejouer la même bataille » pour reconstruire ceux détruits).
    public GameObject SpawnAt(Vector3 point, Quaternion rotation, ProceduralTerrain terrain, Team team, bool feedback)
    {
        GameManager gm = GameManager.Instance;
        Transform parent = terrain != null ? terrain.transform : null;

        GameObject building = Instantiate(buildingPrefab, point, rotation, parent);
        building.transform.localScale = buildingScale * sizeFactor;

        // Les soldats contournent le bâtiment : il creuse un trou dans le NavMesh.
        if (building.GetComponentInChildren<NavMeshObstacle>() == null)
        {
            Bounds bounds = ComputeBounds(building);
            var obstacle = building.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = building.transform.InverseTransformPoint(bounds.center);
            Vector3 s = building.transform.lossyScale;
            obstacle.size = new Vector3(bounds.size.x / Mathf.Max(s.x, 0.0001f), bounds.size.y / Mathf.Max(s.y, 0.0001f), bounds.size.z / Mathf.Max(s.z, 0.0001f));
            obstacle.carving = true;
        }

        Building b = building.GetComponent<Building>();
        if (b == null) b = building.AddComponent<Building>();
        b.team = team;

        if (terrain != null)
            terrain.RegisterPlacedObject(building.transform);
        if (gm != null)
            gm.RegisterBuilding(building, feedback);
        return building;
    }

    private static Bounds ComputeBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    // Méthode à appeler via bouton UI
    public void EnablePlacement()
    {
        GameManager gm = GameManager.Instance;
        if (gm != null && !gm.CanPlaceBuilding(out string reason))
        {
            gm.ShowMessage(reason);
            return;
        }

        // Un appui active le mode placement, un second le désactive.
        isPlacing = !isPlacing || gm == null;
        if (isPlacing)
        {
            SpawnSoldier soldiers = FindObjectOfType<SpawnSoldier>();
            if (soldiers != null) soldiers.CancelPlacement();
            if (TerrainHeightController.Active != null) TerrainHeightController.Active.SetSculptMode(false);
        }

        if (gm != null)
        {
            gm.ShowMessage(isPlacing ? "Touche le terrain pour poser un bâtiment." : "Bâtiments posés.");
            gm.RefreshUI();
        }
    }

    public bool IsPlacing => isPlacing;

    public void CancelPlacement()
    {
        isPlacing = false;
        if (GameManager.Instance != null) GameManager.Instance.RefreshUI();
    }
}
