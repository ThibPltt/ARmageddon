using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;


[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralTerrain : MonoBehaviour
{
    [Header("Dimensions (unités locales)")]
    public int width = 20;
    public int height = 20;
    [Tooltip("Nombre de cases par côté du maillage. Plus il y en a, plus la sculpture est fine.")]
    public int resolution = 100;

    [Header("Relief")]
    public float scale = 0.1f;
    public float heightMultiplier = 15f;
    [Tooltip("Part du relief de base conservée à la génération : 0 = terrain plat, 1 = montagnes d'origine.")]
    [Range(0f, 1f)] public float baseReliefScale = 0.006f;   // terrain presque plat au départ (le terrain fait 4 unités de côté)
    public float minSculptHeight = -0.4f; // on peut creuser sous le niveau du sol (lacs)
    public float maxSculptHeight = 1.5f;

    [Header("Apparition")]
    public float animationDuration = 1f;
    [Tooltip("Génère le terrain dans Start(). Laisser décoché en AR : c'est ARPlacement qui le génère.")]
    public bool generateOnStart = false;

    [Header("Matériau")]
    public Material terrainMaterial;

    [Header("Décorations (posées au verrouillage du terrain)")]
    public GameObject[] treePrefabs;
    public GameObject[] rockPrefabs;
    public GameObject[] flowerPrefabs;      // herbes, fleurs, champignons
    public GameObject[] bushPrefabs;        // buissons, souches

    [Header("Répartition (bruit de Perlin)")]
    public float decorationSpacing = 0.11f;  // pas de la grille de candidats, en unités locales
    public float grassDensity = 0.7f;         // 0 = aucune herbe, 1 = partout où le bruit le permet
    public float rockDensity = 0.27f;
    public float bushDensity = 0.22f; 
    public float treeBorder = 0.4f;          // forêt dense sur cette bande au bord, pour ne pas cacher la bataille
    public float treeDensity = 0.36f;
    public float groveDensity = 0.2f;        // bosquets isolés à l'intérieur, là où le bruit est le plus fort
    public int maxDecorations = 260;         // plafond, pour les performances sur téléphone

    [Header("Lacs (zones creusées, au verrouillage)")]
    public Material waterMaterial;
    public float lakeDigThreshold = 0.03f;   // profondeur de creusement à partir de laquelle l'eau apparaît
    public float lakeSurfaceDrop = 0.015f;   // l'eau affleure juste sous le niveau du sol d'origine

    [HideInInspector]
    public Vector2 offset;

    [HideInInspector]
    public bool isLocked = false;

    // ---------- Navigation ----------
    // Réglages du NavMesh adaptés à des soldats miniatures (en unités du monde du jeu).
    public const float NavAgentRadius = 0.12f;
    public const float NavAgentHeight = 0.4f;
    private static NavMeshBuildSettings navSettings;
    private static bool navSettingsCreated = false;
    public static int NavAgentTypeId => GetNavSettings().agentTypeID;

    private NavMeshData navData;
    private NavMeshDataInstance navInstance;

    private const int Octaves = 5;
    private const float Persistence = 0.6f;
    private const string GroundLayerName = "Ground";

    private Mesh mesh;
    private Vector3[] baseVertices;
    private Vector3[] animatedVertices;
    private float[] sculptOffsets;    // hauteur ajoutée par le joueur, sommet par sommet
    private float[] noiseCache;       // relief de base par sommet (calculé une fois : le bruit est coûteux)
    private float[] edgeCache;        // masque de bord par sommet
    private List<GameObject> spawnedDecorations = new List<GameObject>();
    private List<Transform> placedObjects = new List<Transform>(); // soldats et bâtiments posés sur le terrain


    private float CellX => (float)width / resolution;
    private float CellZ => (float)height / resolution;
    private int VertsPerRow => resolution + 1;

    void Start()
    {
        if (generateOnStart)
        {
            GenerateBaseTerrain();
            AnimateUp();
        }
    }

    void OnDestroy()
    {
        if (navInstance.valid) navInstance.Remove();
    }

    // Hauteur du relief de base (bruit de Perlin) au point local (x, z), sans la sculpture du joueur.
    private float SampleNoiseHeight(float x, float z)
    {
        float centerX = width / 2f;
        float centerZ = height / 2f;

        float distX = Mathf.Abs(x - centerX) / centerX;
        float distZ = Mathf.Abs(z - centerZ) / centerZ;
        float dist = Mathf.Max(distX, distZ);
        float hillFactor = Mathf.Clamp01(1f - dist);

        float mask = Mathf.PerlinNoise((x + 1000) * scale * 0.5f, (z + 1000) * scale * 0.5f);
        mask = Mathf.Lerp(0.5f, 1f, mask);

        float y = 0f;
        float frequency = 1f;
        float amplitude = heightMultiplier;

        for (int octave = 0; octave < Octaves; octave++)
        {
            float sampleXOct = (x + offset.x) * scale * frequency;
            float sampleZOct = (z + offset.y) * scale * frequency;
            y += Mathf.PerlinNoise(sampleXOct, sampleZOct) * amplitude;
            frequency *= 2f;
            amplitude *= Persistence;
        }

        return y * hillFactor * mask * baseReliefScale;
    }

    // 1 au centre, 0 sur les bords : la sculpture s'efface près des bords pour garder un terrain posé au sol.
    private float EdgeMask(float x, float z)
    {
        float border = Mathf.Min(Mathf.Min(x, width - x), Mathf.Min(z, height - z));
        return Mathf.Clamp01(border / (Mathf.Min(width, height) * 0.08f));
    }

    private float VertexHeight(int i)
    {
        float y = noiseCache[i] + sculptOffsets[i] * edgeCache[i];
        return Mathf.Clamp(y, minSculptHeight, maxSculptHeight);
    }

    private void ComputeNoiseCache()
    {
        int count = VertsPerRow * VertsPerRow;
        if (noiseCache == null || noiseCache.Length != count) { noiseCache = new float[count]; edgeCache = new float[count]; }
        for (int i = 0, gz = 0; gz <= resolution; gz++)
            for (int gx = 0; gx <= resolution; gx++, i++)
            {
                noiseCache[i] = SampleNoiseHeight(gx * CellX, gz * CellZ);
                edgeCache[i] = EdgeMask(gx * CellX, gz * CellZ);
            }
    }

    public void GenerateBaseTerrain()
    {
        resolution = Mathf.Max(4, resolution);

        mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        GetComponent<MeshFilter>().mesh = mesh;

        int count = VertsPerRow * VertsPerRow;
        baseVertices = new Vector3[count];
        animatedVertices = new Vector3[count];
        sculptOffsets = new float[count];
        ComputeNoiseCache();
        int[] triangles = new int[resolution * resolution * 6];
        Vector2[] uv = new Vector2[count];

        for (int i = 0, gz = 0; gz <= resolution; gz++)
        {
            for (int gx = 0; gx <= resolution; gx++, i++)
            {
                float x = gx * CellX;
                float z = gz * CellZ;
                baseVertices[i] = new Vector3(x, VertexHeight(i), z);
                animatedVertices[i] = new Vector3(x, 0, z);
                uv[i] = new Vector2((float)gx / resolution, (float)gz / resolution);
            }
        }

        int vert = 0;
        int tris = 0;
        for (int gz = 0; gz < resolution; gz++)
        {
            for (int gx = 0; gx < resolution; gx++)
            {
                triangles[tris + 0] = vert + 0;
                triangles[tris + 1] = vert + resolution + 1;
                triangles[tris + 2] = vert + 1;

                triangles[tris + 3] = vert + 1;
                triangles[tris + 4] = vert + resolution + 1;
                triangles[tris + 5] = vert + resolution + 2;

                vert++;
                tris += 6;
            }
            vert++;
        }

        mesh.Clear();
        mesh.vertices = animatedVertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        RefreshCollider();

        int groundLayer = LayerMask.NameToLayer(GroundLayerName);
        if (groundLayer >= 0)
            gameObject.layer = groundLayer;
        else
            Debug.LogError("Layer \"Ground\" introuvable : l'ajouter dans Project Settings > Tags and Layers.");

        ApplyMaterial();
        ClearDecorations();
    }

    // Recalcule le MeshCollider à partir du mesh actuel.
    // Unity ne met pas le collider à jour quand les sommets changent : il faut lui réassigner le mesh.
    // Non convexe : un collider convexe suit l'enveloppe du relief et fait flotter les objets au-dessus des creux.
    public void RefreshCollider()
    {
        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc == null)
            mc = gameObject.AddComponent<MeshCollider>();

        mc.convex = false;
        mc.sharedMesh = null;
        mc.sharedMesh = mesh;
        mc.enabled = true;
    }

    public void AnimateUp()
    {
        StopAllCoroutines();
        StartCoroutine(AnimateTerrainUp());
    }

    IEnumerator AnimateTerrainUp()
    {
        float elapsed = 0f;
        while (elapsed < animationDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / animationDuration);

            for (int i = 0; i < baseVertices.Length; i++)
            {
                float newY = Mathf.Lerp(0, baseVertices[i].y, t);
                animatedVertices[i] = new Vector3(baseVertices[i].x, newY, baseVertices[i].z);
            }

            mesh.vertices = animatedVertices;
            mesh.RecalculateNormals();
            yield return null;
        }

        mesh.vertices = baseVertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Le collider avait été créé sur le terrain encore plat : on le recalcule sur le relief final.
        RefreshCollider();

        // Bake du NavMesh après la génération et l'animation du mesh.
        // Décor et lacs arrivent au verrouillage du terrain (DressTerrain), une fois le relief fini.
        BakeNavMesh();
    }

    // Recalcule toutes les hauteurs (relief de base + sculpture) et met le mesh à jour.
    private void RebuildHeights(bool refreshCollider)
    {
        for (int i = 0, gz = 0; gz <= resolution; gz++)
        {
            for (int gx = 0; gx <= resolution; gx++, i++)
            {
                float y = VertexHeight(i);
                baseVertices[i].y = y;
                animatedVertices[i].y = y;
            }
        }

        mesh.vertices = baseVertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (refreshCollider)
            RefreshCollider();

        RepositionDecorations();
        RepositionPlacedObjects();
    }

    // Change la hauteur de tout le relief de base (ancien geste à deux doigts).
    public void UpdateTerrain(float newHeightMultiplier)
    {
        if (isLocked || mesh == null) return;
        heightMultiplier = newHeightMultiplier;
        ComputeNoiseCache();
        RebuildHeights(true);
    }

    // Monte (amount > 0) ou creuse (amount < 0) le relief autour d'un point local.
    // Profil en cloche (cosinus), légèrement irrégulier, puis un lissage local pour éviter les arêtes.
    // Le collider n'est pas recalculé ici (coûteux) : appeler FinishSculpt() quand le geste se termine.
    // force = true : sculpte même si le terrain est verrouillé (cratères des bombes).
    public void Sculpt(Vector3 localPoint, float radius, float amount, bool force = false)
    {
        if ((isLocked && !force) || mesh == null || radius <= 0f) return;

        int minGx = Mathf.Max(0, Mathf.FloorToInt((localPoint.x - radius) / CellX) - 1);
        int maxGx = Mathf.Min(resolution, Mathf.CeilToInt((localPoint.x + radius) / CellX) + 1);
        int minGz = Mathf.Max(0, Mathf.FloorToInt((localPoint.z - radius) / CellZ) - 1);
        int maxGz = Mathf.Min(resolution, Mathf.CeilToInt((localPoint.z + radius) / CellZ) + 1);

        for (int gz = minGz; gz <= maxGz; gz++)
        {
            for (int gx = minGx; gx <= maxGx; gx++)
            {
                float x = gx * CellX, z = gz * CellZ;
                float d = Mathf.Sqrt((x - localPoint.x) * (x - localPoint.x) + (z - localPoint.z) * (z - localPoint.z));
                if (d >= radius) continue;

                float k = 0.5f * (1f + Mathf.Cos(Mathf.PI * d / radius));          // 1 au centre, 0 au bord, sans cassure
                float grain = 0.8f + 0.4f * Mathf.PerlinNoise(x * 6f + 37f, z * 6f + 11f); // relief moins lisse, plus naturel
                int i = gz * VertsPerRow + gx;
                sculptOffsets[i] += amount * k * grain;
                ClampOffset(i);
            }
        }

        SmoothOffsets(minGx, maxGx, minGz, maxGz, 0.2f);
        RebuildHeights(false);
    }

    // Borne la sculpture pour que la hauteur finale reste entre min et max.
    private void ClampOffset(int i)
    {
        float noise = noiseCache[i];
        float edge = Mathf.Max(edgeCache[i], 0.0001f);
        sculptOffsets[i] = Mathf.Clamp(sculptOffsets[i], (minSculptHeight - noise) / edge, (maxSculptHeight - noise) / edge);
    }

    // Un passage de flou sur la zone sculptée : adoucit les pentes trop raides.
    private void SmoothOffsets(int minGx, int maxGx, int minGz, int maxGz, float strength)
    {
        int w = maxGx - minGx + 1, h = maxGz - minGz + 1;
        var copy = new float[w * h];
        for (int gz = minGz; gz <= maxGz; gz++)
            for (int gx = minGx; gx <= maxGx; gx++)
                copy[(gz - minGz) * w + (gx - minGx)] = sculptOffsets[gz * VertsPerRow + gx];

        for (int gz = minGz + 1; gz < maxGz; gz++)
        {
            for (int gx = minGx + 1; gx < maxGx; gx++)
            {
                int c = (gz - minGz) * w + (gx - minGx);
                float avg = (copy[c - 1] + copy[c + 1] + copy[c - w] + copy[c + w]) * 0.25f;
                int i = gz * VertsPerRow + gx;
                sculptOffsets[i] = Mathf.Lerp(copy[c], avg, strength);
                ClampOffset(i);
            }
        }
    }

    public void FinishSculpt()
    {
        if (mesh != null) RefreshCollider();
    }

    void ApplyMaterial()
    {
        var renderer = GetComponent<MeshRenderer>();
        if (renderer != null && terrainMaterial != null)
            renderer.material = terrainMaterial;
    }

    // ---------- Décor et lacs ----------

    // Appelé au verrouillage : remplit d'eau les zones creusées, puis pose le décor autour.
    public void DressTerrain()
    {
        BuildLakes();
        SpawnDecorations();
    }

    // Appelé au déverrouillage : on retire eau et décor pour sculpter librement.
    public void UndressTerrain()
    {
        ClearLakes();
        ClearDecorations();
    }

    // Vrai si le joueur a creusé ce sommet sous le niveau du sol d'origine.
    private bool IsDug(int i) => sculptOffsets != null && sculptOffsets[i] * edgeCache[i] < -lakeDigThreshold;

    private bool IsDugNear(float x, float z)
    {
        int gx = Mathf.Clamp(Mathf.RoundToInt(x / CellX), 0, resolution);
        int gz = Mathf.Clamp(Mathf.RoundToInt(z / CellZ), 0, resolution);
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int cx = Mathf.Clamp(gx + dx, 0, resolution), cz = Mathf.Clamp(gz + dz, 0, resolution);
                if (IsDug(cz * VertsPerRow + cx)) return true;
            }
        return false;
    }

    // Pente locale (différence de hauteur sur une cellule), pour éviter de poser le décor dans les pentes raides.
    private float SlopeAt(float x, float z)
    {
        float h = GetHeightAtPosition(x, z);
        float dx = Mathf.Abs(GetHeightAtPosition(x + CellX, z) - h);
        float dz = Mathf.Abs(GetHeightAtPosition(x, z + CellZ) - h);
        return Mathf.Max(dx, dz) / CellX;
    }

    public void SpawnDecorations()
    {
        ClearDecorations();
        if (baseVertices == null) return;

        // Nouveau tirage à chaque terrain : le bruit est décalé au hasard.
        float seedA = Random.Range(0f, 1000f), seedB = Random.Range(0f, 1000f), seedC = Random.Range(0f, 1000f);
        float margin = Mathf.Min(width, height) * 0.04f;
        int placed = 0;

        // Candidats sur une grille, visités dans le désordre : si le plafond est atteint, le décor reste réparti partout.
        var candidates = new List<Vector2>();
        for (float z = margin; z < height - margin; z += decorationSpacing)
            for (float x = margin; x < width - margin; x += decorationSpacing)
                candidates.Add(new Vector2(x, z));
        for (int k = candidates.Count - 1; k > 0; k--)
        {
            int j = Random.Range(0, k + 1);
            (candidates[k], candidates[j]) = (candidates[j], candidates[k]);
        }

        foreach (Vector2 cell in candidates)
        {
            if (placed >= maxDecorations) break;
            {
                // Position décalée au hasard dans la cellule : pas d'alignement visible.
                float px = cell.x + Random.Range(-0.45f, 0.45f) * decorationSpacing;
                float pz = cell.y + Random.Range(-0.45f, 0.45f) * decorationSpacing;
                if (px < margin || pz < margin || px > width - margin || pz > height - margin) continue;
                if (IsDugNear(px, pz) || SlopeAt(px, pz) > 1.2f) continue;

                float border = Mathf.Min(Mathf.Min(px, width - px), Mathf.Min(pz, height - pz));
                float grassNoise = Mathf.PerlinNoise(px * 1.6f + seedA, pz * 1.6f + seedA);
                float rockNoise = Mathf.PerlinNoise(px * 2.3f + seedB, pz * 2.3f + seedB);
                float woodNoise = Mathf.PerlinNoise(px * 1.2f + seedC, pz * 1.2f + seedC);

                GameObject[] pool = null;
                Vector2 heightRange = Vector2.zero;

                if (border < treeBorder && woodNoise > 0.45f && Random.value < treeDensity)
                { pool = treePrefabs; heightRange = new Vector2(0.3f, 0.55f); }
                else if (woodNoise > 0.7f && Random.value < groveDensity)
                { pool = treePrefabs; heightRange = new Vector2(0.25f, 0.4f); }
                else if (woodNoise > 0.5f && Random.value < bushDensity)
                { pool = bushPrefabs; heightRange = new Vector2(0.06f, 0.12f); }
                else if (rockNoise > 0.55f && Random.value < rockDensity)
                { pool = rockPrefabs; heightRange = new Vector2(0.03f, 0.09f); }
                else if (grassNoise > 0.4f && Random.value < grassDensity * Mathf.Clamp01((grassNoise - 0.35f) * 2.5f))
                { pool = flowerPrefabs; heightRange = new Vector2(0.025f, 0.05f); }

                if (pool == null || pool.Length == 0) continue;
                GameObject prefab = pool[Random.Range(0, pool.Length)];
                if (prefab == null) continue;
                SpawnDecoration(prefab, new Vector3(px, GetHeightAtPosition(px, pz), pz), Random.Range(heightRange.x, heightRange.y), pool != treePrefabs);
                placed++;
            }
        }
    }

    // Hauteur réelle d'un prefab à l'échelle 1, mise en cache (on mesure ses Renderers une seule fois).
    private static readonly Dictionary<GameObject, float> prefabHeights = new Dictionary<GameObject, float>();

    private void SpawnDecoration(GameObject prefab, Vector3 position, float targetHeight, bool small)
    {
        GameObject go = Instantiate(prefab, transform);
        go.name = prefab.name;
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Décor purement visuel : pas de collision (les tirs, la pose et le NavMesh l'ignorent).
        foreach (Collider c in go.GetComponentsInChildren<Collider>()) Destroy(c);

        if (!prefabHeights.TryGetValue(prefab, out float natural))
        {
            go.transform.localScale = Vector3.one;
            natural = 0f;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
                natural = Mathf.Max(natural, r.bounds.size.y / Mathf.Max(0.0001f, transform.lossyScale.y));
            if (natural < 0.0001f) natural = 1f;
            prefabHeights[prefab] = natural;
        }
        go.transform.localScale = Vector3.one * (targetHeight / natural);

        // Les petits éléments ne projettent pas d'ombre : moins coûteux, et la scène reste lisible.
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (small) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = !small;
        }
        spawnedDecorations.Add(go);
    }

    // Retire le décor autour d'un point (bâtiment posé, cratère de bombe).
    public void ClearDecorationsAround(Vector3 worldPoint, float radius)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        for (int k = spawnedDecorations.Count - 1; k >= 0; k--)
        {
            GameObject d = spawnedDecorations[k];
            if (d == null) { spawnedDecorations.RemoveAt(k); continue; }
            Vector3 p = d.transform.localPosition;
            if ((new Vector2(p.x - local.x, p.z - local.z)).magnitude < radius)
            {
                Destroy(d);
                spawnedDecorations.RemoveAt(k);
            }
        }
    }

    private GameObject lakeObject;
    private Mesh lakeMesh;

    // Une nappe d'eau sur chaque zone creusée, au niveau du sol d'origine : le terrain autour cache ses bords.
    public void BuildLakes()
    {
        ClearLakes();
        if (sculptOffsets == null || waterMaterial == null) return;

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        var index = new Dictionary<int, int>();

        int Vertex(int gx, int gz)
        {
            int i = gz * VertsPerRow + gx;
            if (index.TryGetValue(i, out int v)) return v;
            v = verts.Count;
            float x = gx * CellX, z = gz * CellZ;
            verts.Add(new Vector3(x, noiseCache[i] - lakeSurfaceDrop, z));
            uvs.Add(new Vector2(x / width, z / height) * 2f);
            index[i] = v;
            return v;
        }

        for (int gz = 0; gz < resolution; gz++)
        {
            for (int gx = 0; gx < resolution; gx++)
            {
                int i = gz * VertsPerRow + gx;
                if (!IsDug(i) && !IsDug(i + 1) && !IsDug(i + VertsPerRow) && !IsDug(i + VertsPerRow + 1)) continue;
                int a = Vertex(gx, gz), b = Vertex(gx + 1, gz), c = Vertex(gx, gz + 1), d = Vertex(gx + 1, gz + 1);
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }
        if (tris.Count == 0) return;

        lakeMesh = new Mesh { name = "Lakes" };
        lakeMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        lakeMesh.SetVertices(verts);
        lakeMesh.SetUVs(0, uvs);
        lakeMesh.SetTriangles(tris, 0);
        lakeMesh.RecalculateNormals();
        lakeMesh.RecalculateBounds();

        lakeObject = new GameObject("Lakes");
        lakeObject.transform.SetParent(transform, false);
        lakeObject.AddComponent<MeshFilter>().sharedMesh = lakeMesh;
        var mr = lakeObject.AddComponent<MeshRenderer>();
        var mat = new Material(waterMaterial);
        mr.sharedMaterial = mat;

        // L'écume du bord des lacs lit la profondeur de la scène : la caméra doit la produire.
        Camera cam = Camera.main;
        if (cam != null) cam.depthTextureMode |= DepthTextureMode.Depth;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    public void ClearLakes()
    {
        if (lakeObject != null) Destroy(lakeObject);
        lakeObject = null;
        lakeMesh = null;
    }

    // Hauteur du relief en coordonnées locales du terrain (0..width, 0..height).
    public float GetHeightAtPosition(float x, float z)
    {
        if (baseVertices == null) return 0f;

        float fx = Mathf.Clamp(x / CellX, 0f, resolution - 0.001f);
        float fz = Mathf.Clamp(z / CellZ, 0f, resolution - 0.001f);

        int x0 = Mathf.FloorToInt(fx);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, resolution);
        int z1 = Mathf.Min(z0 + 1, resolution);

        float sx = fx - x0;
        float sz = fz - z0;

        float y00 = baseVertices[z0 * VertsPerRow + x0].y;
        float y10 = baseVertices[z0 * VertsPerRow + x1].y;
        float y01 = baseVertices[z1 * VertsPerRow + x0].y;
        float y11 = baseVertices[z1 * VertsPerRow + x1].y;

        float y0 = Mathf.Lerp(y00, y10, sx);
        float y1 = Mathf.Lerp(y01, y11, sx);
        return Mathf.Lerp(y0, y1, sz);
    }


    private void ClearDecorations()
    {
        foreach (var obj in spawnedDecorations)
        {
            if (obj != null)
                Destroy(obj);
        }
        spawnedDecorations.Clear();
    }

    public void RepositionDecorations()
    {
        foreach (var decoration in spawnedDecorations)
        {
            if (decoration != null)
            {
                Vector3 localPos = decoration.transform.localPosition;
                float newY = GetHeightAtPosition(localPos.x, localPos.z);
                decoration.transform.localPosition = new Vector3(localPos.x, newY, localPos.z);
            }
        }
    }

    // Enregistre un soldat, un bâtiment ou un repère, pour qu'il suive le relief quand on le sculpte.
    public void RegisterPlacedObject(Transform placed)
    {
        if (placed != null && !placedObjects.Contains(placed))
            placedObjects.Add(placed);
    }

    private void RepositionPlacedObjects()
    {
        placedObjects.RemoveAll(t => t == null);

        foreach (Transform placed in placedObjects)
        {
            Vector3 local = transform.InverseTransformPoint(placed.position);
            local.y = GetHeightAtPosition(local.x, local.z);
            Vector3 world = transform.TransformPoint(local);

            NavMeshAgent agent = placed.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled && agent.isOnNavMesh)
                agent.Warp(world);
            else
                placed.position = world;
        }
    }

    // ---------- NavMesh ----------

    private static NavMeshBuildSettings GetNavSettings()
    {
        if (!navSettingsCreated)
        {
            navSettings = NavMesh.CreateSettings();
            navSettings.agentRadius = NavAgentRadius;
            navSettings.agentHeight = NavAgentHeight;
            navSettings.agentClimb = 0.25f;
            navSettings.agentSlope = 60f;
            navSettings.overrideVoxelSize = true;
            navSettings.voxelSize = 0.04f;
            navSettings.overrideTileSize = true;
            navSettings.tileSize = 128;
            navSettings.minRegionArea = 0.05f;
            navSettingsCreated = true;
        }
        return navSettings;
    }

    // Construit le NavMesh directement depuis le mesh du terrain, avec des réglages à l'échelle des soldats.
    // Les bâtiments creusent leur place grâce à leur NavMeshObstacle.
    public void BakeNavMesh() => BuildNavMesh(false);

    // Version asynchrone, sans à-coup : utilisée après un cratère de bombe pendant la bataille.
    public void RebuildNavMeshAsync() => BuildNavMesh(true);

    private void BuildNavMesh(bool async)
    {
        if (mesh == null) return;

        NavMeshBuildSettings settings = GetNavSettings();
        var sources = new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Mesh,
                sourceObject = mesh,
                transform = transform.localToWorldMatrix,
                area = 0
            }
        };

        // L'eau n'est pas praticable : sa surface est « non marchable » et masque le fond du lac.
        if (lakeMesh != null && lakeObject != null)
        {
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Mesh,
                sourceObject = lakeMesh,
                transform = lakeObject.transform.localToWorldMatrix,
                area = 1
            });
        }

        Bounds localBounds = mesh.bounds;
        localBounds.Expand(new Vector3(1f, 2f, 1f));

        if (navData == null)
        {
            navData = new NavMeshData(settings.agentTypeID)
            {
                position = transform.position,
                rotation = transform.rotation
            };
        }

        // Les bornes sont exprimées dans le repère du NavMeshData (position et rotation du terrain).
        Vector3 s = transform.lossyScale;
        Bounds scaled = new Bounds(Vector3.Scale(localBounds.center, s), Vector3.Scale(localBounds.size, s));
        if (async && navData != null && navInstance.valid)
        {
            NavMeshBuilder.UpdateNavMeshDataAsync(navData, settings, sources, scaled);
            return;
        }
        NavMeshBuilder.UpdateNavMeshData(navData, settings, sources, scaled);

        if (!navInstance.valid)
            navInstance = NavMesh.AddNavMeshData(navData, transform.position, transform.rotation);

        foreach (string warning in settings.ValidationReport(scaled))
            Debug.LogWarning("NavMesh : " + warning);
    }
}
