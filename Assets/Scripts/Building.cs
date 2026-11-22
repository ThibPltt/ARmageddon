using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Un bâtiment du champ de bataille : il appartient à une équipe et sert de couvert aux soldats proches.
// Les balles arrêtées par ses murs l'abîment (5 suffisent à l'abattre), les bombes aussi.
// Pour gagner, une équipe doit éliminer les soldats adverses et raser leurs bâtiments.
public class Building : MonoBehaviour
{
    public static readonly List<Building> All = new List<Building>();

    public Team team = Team.Blue;

    public float maxHealth = 100f;
    public int shotsToDestroy = 5;             // balles arrêtées par les murs avant l'effondrement
    public float coverMargin = 0.15f;          // distance autour des murs où un soldat est à couvert (unités du monde)

    public float Health { get; private set; }
    public bool IsStanding => Health > 0f;
    public float CoverRadius { get; private set; } = 1f;

    private Renderer[] renderers;
    private MaterialPropertyBlock block;

    void Awake()
    {
        Health = maxHealth;
        renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
    }

    void Start()
    {
        // Rayon de couvert : la demi-largeur du bâtiment plus une marge.
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            CoverRadius = Mathf.Max(b.extents.x, b.extents.z) + coverMargin;
        }
        ApplyTint();
    }

    // Couleur de l'équipe, assombrie à mesure que le bâtiment est touché.
    private void ApplyTint()
    {
        float burnt = 1f - Health / maxHealth;
        Color teamTint = Color.Lerp(Color.white, Unit.TeamColor(team), 0.55f);
        Color tint = Color.Lerp(teamTint, new Color(0.25f, 0.22f, 0.2f), burnt);
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_Color", tint);
            block.SetColor("_BaseColor", tint);
            r.SetPropertyBlock(block);
        }
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // Vrai si ce point est à l'abri d'un bâtiment encore debout.
    public static bool IsInCover(Vector3 worldPos) => CoverFor(worldPos) != null;

    // Le bâtiment qui abrite ce point (le plus proche), ou null.
    public static Building CoverFor(Vector3 worldPos)
    {
        Building best = null;
        float bestDist = float.MaxValue;
        foreach (Building b in All)
        {
            if (b == null || !b.IsStanding) continue;
            Vector3 d = worldPos - b.transform.position;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < b.CoverRadius && dist < bestDist) { bestDist = dist; best = b; }
        }
        return best;
    }

    // Point du mur le plus proche du tireur, pour que la trace de la balle s'arrête dessus.
    public Vector3 WallPointToward(Vector3 from)
    {
        if (renderers.Length == 0) return transform.position;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) if (r != null) b.Encapsulate(r.bounds);
        Vector3 p = b.ClosestPoint(from);
        p.y = Mathf.Min(from.y, b.max.y);
        return p;
    }

    // Une balle arrêtée par les murs.
    public void TakeShot()
    {
        TakeDamage(maxHealth / Mathf.Max(1, shotsToDestroy) + 0.01f);
    }

    public void TakeDamage(float amount)
    {
        if (!IsStanding) return;
        Health = Mathf.Max(0f, Health - amount);

        ApplyTint();

        if (!IsStanding) StartCoroutine(Collapse());
    }

    private IEnumerator Collapse()
    {
        All.Remove(this);
        foreach (NavMeshObstacle o in GetComponentsInChildren<NavMeshObstacle>()) o.enabled = false;

        Effects.Dust(transform.position, CoverRadius);
        GameAudio.Collapse();
        if (GameManager.Instance != null) GameManager.Instance.OnBuildingDestroyed(this);

        // Il tremble, penche et s'enfonce dans le sol.
        Vector3 start = transform.position;
        Quaternion rot = transform.rotation;
        Quaternion tilt = rot * Quaternion.Euler(Random.Range(-12f, 12f), 0f, Random.Range(-12f, 12f));
        float height = CoverRadius * 2f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 1.2f)
        {
            Vector3 shake = Random.insideUnitSphere * 0.03f * (1f - t);
            transform.position = start + Vector3.down * height * t * t + shake;
            transform.rotation = Quaternion.Slerp(rot, tilt, t);
            yield return null;
        }
        Destroy(gameObject);
    }
}
