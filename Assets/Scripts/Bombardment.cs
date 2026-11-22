using System.Collections;
using UnityEngine;

// Bombardement pendant la bataille : le joueur arme une bombe, touche le terrain, elle tombe et explose.
// Chaque équipe a son stock et son bouton : « Bombe bleue » ou « Bombe rouge », puis on touche le terrain.
// L'explosion blesse les soldats, abîme les bâtiments et creuse un cratère dans le relief.
public class Bombardment : MonoBehaviour
{
    [Header("Règles")]
    public int bombsPerBattle = 2;            // par équipe
    public float cooldown = 1f;               // secondes entre deux bombes

    [Header("Explosion (unités du monde)")]
    public float blastRadius = 0.38f;
    public float unitDamage = 180f;           // au centre ; diminue jusqu'au bord du souffle
    public float buildingDamage = 70f;        // deux bombes bien placées abattent un bâtiment
    public float craterRadius = 0.22f;         // en unités locales du terrain
    public float craterDepth = 0.1f;

    [Header("Chute")]
    public float dropHeight = 2f;
    public float fallTime = 0.8f;

    private int blueLeft, redLeft;
    public int RemainingFor(Team t) => t == Team.Blue ? blueLeft : redLeft;
    public int Remaining => blueLeft + redLeft;
    public bool Armed { get; private set; }
    public Team ArmedTeam { get; private set; }
    public bool IsArmed(Team t) => Armed && ArmedTeam == t;

    private float nextDropTime;
    private static Material bombMaterial;

    public void ResetBombs()
    {
        blueLeft = bombsPerBattle;
        redLeft = bombsPerBattle;
        Armed = false;
    }

    // Bouton « Bombe bleue » ou « Bombe rouge » : arme une bombe de cette équipe, ou annule si elle l'était déjà.
    public void ToggleArmed(Team team)
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || gm.State != GameState.Battle) return;
        string name = team == Team.Blue ? "bleue" : "rouge";
        if (RemainingFor(team) <= 0) { gm.ShowMessage($"Plus de bombe {name}."); return; }

        if (IsArmed(team)) Armed = false;
        else { Armed = true; ArmedTeam = team; }
        gm.ShowMessage(Armed ? "Touche l'endroit à bombarder." : "Bombe annulée.");
        gm.RefreshUI();
    }

    public void Disarm()
    {
        Armed = false;
    }

    void Update()
    {
        GameManager gm = GameManager.Instance;
        if (!Armed || gm == null || gm.State != GameState.Battle) return;
        if (Time.time < nextDropTime) return;
        if (Input.touchCount > 1 || !Input.GetMouseButtonDown(0) || PlacementInput.IsPointerOverUI()) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, LayerMask.GetMask("Ground"))) return;

        Team attacker = ArmedTeam;
        if (RemainingFor(attacker) <= 0) { Armed = false; gm.RefreshUI(); return; }
        if (attacker == Team.Blue) blueLeft--; else redLeft--;
        nextDropTime = Time.time + cooldown;
        Armed = false;                        // une bombe par appui sur le bouton
        StartCoroutine(Drop(hit.point, attacker));
        gm.RefreshUI();
    }

    [Header("Modèle de bombe")]
    public float bombSize = 0.14f;             // plus grande dimension, en unités du monde
    // Modèles du pack « Rockets, Missiles & Bombs » copiés dans Assets/Resources ; sinon une sphère.
    private static GameObject blueBombPrefab, redBombPrefab;
    private static bool prefabLooked;

    private GameObject CreateBomb(Team team)
    {
        if (!prefabLooked)
        {
            blueBombPrefab = Resources.Load<GameObject>("BombBlue");
            redBombPrefab = Resources.Load<GameObject>("BombRed");
            prefabLooked = true;
        }

        GameObject bombPrefab = team == Team.Blue ? blueBombPrefab : redBombPrefab;
        if (bombPrefab != null)
        {
            GameObject model = Instantiate(bombPrefab);
            model.name = "Bomb";
            // On ne garde que l'aspect : pas de physique ni de collision venant du pack.
            foreach (Rigidbody rb in model.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (Collider c in model.GetComponentsInChildren<Collider>()) Destroy(c);

            // Mise à l'échelle selon la taille réelle du modèle, quelle que soit celle du pack.
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                float biggest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (biggest > 0.0001f) model.transform.localScale *= bombSize / biggest;

                // Le modèle est debout, nez en haut : on le retourne pour qu'il tombe nez en bas.
                Quaternion noseDown;
                if (b.size.y >= b.size.x && b.size.y >= b.size.z) noseDown = Quaternion.Euler(180f, 0f, 0f);
                else if (b.size.z >= b.size.x) noseDown = Quaternion.Euler(90f, 0f, 0f);
                else noseDown = Quaternion.Euler(0f, 0f, -90f);
                model.transform.rotation = noseDown * model.transform.rotation;
            }
            return model;
        }

        if (bombMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            bombMaterial = new Material(shader) { color = new Color(0.12f, 0.12f, 0.12f) };
        }
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Bomb";
        Destroy(sphere.GetComponent<Collider>());
        sphere.GetComponent<Renderer>().sharedMaterial = bombMaterial;
        sphere.GetComponent<Renderer>().material.color = Color.Lerp(new Color(0.12f, 0.12f, 0.12f), Unit.TeamColor(team), 0.5f);
        sphere.transform.localScale = Vector3.one * 0.08f;
        return sphere;
    }

    private IEnumerator Drop(Vector3 target, Team team)
    {
        // La bombe tombe en accélérant, avec un sifflement.
        GameObject bomb = CreateBomb(team);
        Quaternion startRot = bomb.transform.rotation;
        Vector3 from = target + Vector3.up * dropHeight;
        GameAudio.Whistle();

        for (float t = 0f; t < 1f; t += Time.deltaTime / fallTime)
        {
            bomb.transform.position = Vector3.Lerp(from, target, t * t);
            // Légère rotation sur elle-même pendant la chute.
            bomb.transform.rotation = Quaternion.AngleAxis(t * 220f, Vector3.up) * startRot;
            yield return null;
        }
        Destroy(bomb);
        Explode(target);
    }

    private void Explode(Vector3 point)
    {
        Effects.Explosion(point, blastRadius * 0.6f);
        GameAudio.Explosion();
        Haptics.Tap(80);

        // Soldats dans le souffle : dégâts dégressifs avec la distance.
        foreach (Unit u in Unit.All.ToArray())
        {
            if (u == null || !u.IsAlive) continue;
            float d = Vector3.Distance(u.transform.position, point);
            if (d < blastRadius) u.TakeDamage(unitDamage * (1f - d / blastRadius));
        }

        // Bâtiments touchés.
        foreach (Building b in Building.All.ToArray())
        {
            if (b == null || !b.IsStanding) continue;
            Vector3 diff = b.transform.position - point;
            diff.y = 0f;
            float d = Mathf.Max(0f, diff.magnitude - b.CoverRadius * 0.5f);
            if (d < blastRadius) b.TakeDamage(buildingDamage * (1f - d / blastRadius));
        }

        // Cratère dans le relief, puis mise à jour du NavMesh sans figer le jeu.
        ProceduralTerrain terrain = GameManager.Instance != null ? GameManager.Instance.Terrain : null;
        if (terrain != null)
        {
            Vector3 local = terrain.transform.InverseTransformPoint(point);
            terrain.ClearDecorationsAround(point, craterRadius);
            terrain.Sculpt(local, craterRadius, -craterDepth, true);
            terrain.FinishSculpt();
            terrain.RebuildNavMeshAsync();
        }
    }
}
