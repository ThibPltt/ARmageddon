using UnityEngine;

public class SpawnSoldier : MonoBehaviour
{
    [Header("Paramètres de placement")]
    public GameObject soldierPrefab;
    public Camera mainCamera;
    public LayerMask terrainLayer;           // Layer du terrain : Ground uniquement
    public float soldierScale = 0.07f;       // Échelle appliquée au prefab

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
        if (gm != null && !gm.CanPlaceSoldier(out string reason))
        {
            gm.ShowMessage(reason);
            isPlacing = false;
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, terrainLayer))
            return;

        if (gm != null && !gm.IsInZone(hit.point, gm.PlacingTeam))
        {
            gm.ShowMessage(gm.PlacingTeam == Team.Blue
                ? "Bleus : de ton côté de la ligne."
                : "Rouges : de l'autre côté de la ligne.");
            return;
        }

        ProceduralTerrain terrain = hit.collider.GetComponentInParent<ProceduralTerrain>();
        GameObject soldier = SpawnAt(hit.point, terrain);

        if (gm != null)
        {
            soldier.transform.rotation = gm.FacingFor(gm.PlacingTeam);
            gm.RegisterSoldier(soldier, gm.PlacingTeam);
        }
        else
            isPlacing = false; // sans GameManager (scène de test) : un soldat par appui, comme avant

        // Avec le GameManager, le mode reste actif : on pose plusieurs soldats d'affilée.
    }

    // Crée un soldat sur le terrain, rattaché à lui pour suivre le relief. Utilisé aussi pour l'armée rouge.
    public GameObject SpawnAt(Vector3 point, ProceduralTerrain terrain)
    {
        Transform parent = terrain != null ? terrain.transform : null;
        GameObject soldier = Instantiate(soldierPrefab, point, Quaternion.identity, parent);
        soldier.transform.localScale = soldier.transform.localScale * soldierScale;

        if (terrain != null)
            terrain.RegisterPlacedObject(soldier.transform);

        return soldier;
    }

    // Méthode à appeler via bouton UI : un appui active le mode placement, un second le désactive.
    public void EnablePlacement()
    {
        GameManager gm = GameManager.Instance;
        if (gm != null && !gm.CanPlaceSoldier(out string reason))
        {
            gm.ShowMessage(reason);
            return;
        }

        isPlacing = !isPlacing || gm == null;
        if (isPlacing)
        {
            SpawnBuilding building = FindObjectOfType<SpawnBuilding>();
            if (building != null) building.CancelPlacement();
        }

        if (gm != null)
        {
            gm.ShowMessage(isPlacing ? "Touche le terrain pour poser des soldats." : "Soldats posés.");
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
