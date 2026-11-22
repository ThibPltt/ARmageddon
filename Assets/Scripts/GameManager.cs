using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public enum GameState
{
    Scanning,    // le téléphone cherche le sol
    Sculpting,   // terrain posé, relief modifiable
    Deployment,  // terrain verrouillé, le joueur pose ses soldats
    Battle,      // les deux armées s'affrontent
    GameOver     // une équipe a gagné
}

// Chef d'orchestre de la partie : connaît l'état, valide les placements et déclare le vainqueur.
// Créé automatiquement au chargement d'une scène qui contient ARPlacement : rien à ajouter dans la scène.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Règles")]
    public int maxSoldiersPerTeam = 30;
    public int maxBuildingsPerTeam = 3;
    public float zoneMargin = 0.3f;           // marge en unités du terrain autour de la ligne de front

    public GameState State { get; private set; } = GameState.Scanning;
    public bool HasStarted { get; private set; }   // faux tant que l'écran d'accueil est affiché
    public ProceduralTerrain Terrain { get; private set; }
    public int BlueCount => CountAlive(Team.Blue);
    public int RedCount => CountAlive(Team.Red);
    public int BuildingsOf(Team team)
    {
        int n = 0;
        foreach (GameObject g in buildings)
        {
            if (g == null) continue;
            Building b = g.GetComponent<Building>();
            if (b != null && b.team == team && b.IsStanding) n++;
        }
        return n;
    }
    public Team PlacingTeam { get; private set; } = Team.Blue;
    public Bombardment Bombs { get; private set; }   // équipe des prochains soldats posés

    private readonly List<GameObject> buildings = new List<GameObject>();
    private bool blueIsLowZ = true;          // la moitié bleue est celle du côté du joueur au verrouillage
    private LineRenderer frontLine;
    private GameUI ui;
    private readonly List<GameObject> placementHistory = new List<GameObject>();   // pour Annuler
    private readonly List<(Team team, Vector3 localPos, Quaternion localRot)> battleSetup =
        new List<(Team, Vector3, Quaternion)>();
    private readonly List<(Team team, Vector3 localPos, Quaternion localRot)> buildingSetup =
        new List<(Team, Vector3, Quaternion)>();                                    // pour Rejouer la même bataille
    private bool trackingLost;
    private static bool skipWelcome;          // vrai après « Nouveau terrain » : on ne repasse pas par l'accueil

    public bool CanUndo => State == GameState.Deployment || State == GameState.Sculpting
                           ? placementHistory.Exists(g => g != null) : false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        CreateIfNeeded();
        // Aussi après chaque rechargement de scène (« Nouveau terrain »).
        SceneManager.sceneLoaded += (scene, mode) => CreateIfNeeded();
    }

    private static void CreateIfNeeded()
    {
        if (Instance == null && FindObjectOfType<ARPlacement>() != null)
            new GameObject("GameManager").AddComponent<GameManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        gameObject.AddComponent<GameAudio>();
        Bombs = gameObject.AddComponent<Bombardment>();
        ui = gameObject.AddComponent<GameUI>();
    }

    void Start()
    {
        HideControllerRays();
        if (skipWelcome)
        {
            skipWelcome = false;
            ui.SkipWelcome();
            HasStarted = true;
        }
        Refresh();
    }

    void OnEnable() { ARSession.stateChanged += OnARSessionStateChanged; }
    void OnDisable() { ARSession.stateChanged -= OnARSessionStateChanged; }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;
    }

    // Si ARCore perd le suivi (téléphone trop rapide, pièce trop sombre), la partie se met en pause.
    private void OnARSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        bool lost = HasStarted && Terrain != null && args.state != ARSessionState.SessionTracking;
        if (lost == trackingLost) return;
        trackingLost = lost;
        Time.timeScale = lost ? 0f : 1f;
        ui.ShowTrackingLost(lost, lost ? TrackingAdvice(ARSession.notTrackingReason) : null);
    }

    private static string TrackingAdvice(NotTrackingReason reason)
    {
        switch (reason)
        {
            case NotTrackingReason.InsufficientLight: return "Trop sombre. Allume la lumière.";
            case NotTrackingReason.ExcessiveMotion: return "Bouge le téléphone plus lentement.";
            case NotTrackingReason.InsufficientFeatures: return "Le sol est trop uni. Vise une zone avec plus de détails.";
            default: return "Vise le terrain et bouge doucement.";
        }
    }

    // « Nouveau terrain » : recharge la scène et repart de la détection du sol, sans l'accueil.
    public void NewTerrain()
    {
        Time.timeScale = 1f;
        skipWelcome = true;
        Unit.All.Clear();
        ARSession session = FindObjectOfType<ARSession>();
        if (session != null) session.Reset();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // « Annuler » : retire le dernier soldat ou bâtiment posé.
    public void Undo()
    {
        for (int i = placementHistory.Count - 1; i >= 0; i--)
        {
            GameObject last = placementHistory[i];
            placementHistory.RemoveAt(i);
            if (last == null) continue;

            buildings.Remove(last);
            Unit unit = last.GetComponent<Unit>();
            if (unit != null) Unit.All.Remove(unit);
            Destroy(last);
            Haptics.Tap(10);
            break;
        }
        Refresh();
    }

    // ---------- Étapes de la partie ----------

    public void OnTerrainSpawned(ProceduralTerrain terrain)
    {
        Terrain = terrain;
        SetState(GameState.Sculpting);
    }

    public void OnTerrainLocked()
    {
        if (Terrain == null) return;
        if (TerrainHeightController.Active != null) TerrainHeightController.Active.SetSculptMode(false);

        // La moitié bleue est celle qui fait face au joueur au moment du verrouillage.
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 camLocal = Terrain.transform.InverseTransformPoint(cam.transform.position);
            blueIsLowZ = camLocal.z < Terrain.height / 2f;
        }

        ShowFrontLine(true);
        SetState(GameState.Deployment);
    }

    public void OnTerrainUnlocked()
    {
        ShowFrontLine(false);
        SetState(GameState.Sculpting);
    }

    public bool CanEditTerrain => State == GameState.Sculpting || State == GameState.Deployment;

    public void StartBattle()
    {
        if (State != GameState.Deployment) return;
        if (BlueCount == 0 || RedCount == 0)
        {
            ShowMessage("Il faut au moins un soldat par équipe.");
            return;
        }

        SpawnSoldier soldiers = FindObjectOfType<SpawnSoldier>();
        if (soldiers != null) soldiers.CancelPlacement();
        SpawnBuilding buildingsSpawner = FindObjectOfType<SpawnBuilding>();
        if (buildingsSpawner != null) buildingsSpawner.CancelPlacement();

        buildingSetup.Clear();
        foreach (GameObject b in buildings)
            if (b != null && Terrain != null && b.GetComponent<Building>() != null)
                buildingSetup.Add((b.GetComponent<Building>().team, Terrain.transform.InverseTransformPoint(b.transform.position),
                                   Quaternion.Inverse(Terrain.transform.rotation) * b.transform.rotation));

        battleSetup.Clear();
        foreach (Unit u in Unit.All)
            if (u != null && Terrain != null)
                battleSetup.Add((u.team, Terrain.transform.InverseTransformPoint(u.transform.position),
                                 Quaternion.Inverse(Terrain.transform.rotation) * u.transform.rotation));

        foreach (Unit u in Unit.All.ToArray())
            u.StartBattle();

        Bombs.ResetBombs();
        SetState(GameState.Battle);
    }

    // Bouton « Équipe » : choisit la couleur des prochains soldats posés.
    public void TogglePlacingTeam()
    {
        PlacingTeam = PlacingTeam == Team.Blue ? Team.Red : Team.Blue;
        ShowMessage(PlacingTeam == Team.Blue ? "Tu places les bleus." : "Tu places les rouges.");
        Refresh();
    }

    public void OnUnitDied(Unit unit)
    {
        if (State != GameState.Battle) { Refresh(); return; }

        // Une équipe sans soldat mais avec des bâtiments n'a pas encore perdu.
        Team side = unit.team;
        if (CountAlive(side) == 0 && BuildingsOf(side) > 0 && CountAlive(Other(side)) > 0)
            ShowMessage(side == Team.Blue ? "Plus de soldats bleus. Reste leurs bâtiments." : "Plus de soldats rouges. Reste leurs bâtiments.");
        CheckVictory();
    }

    // Une équipe gagne quand l'autre n'a plus ni soldat ni bâtiment debout.
    // Si plus personne ne peut tirer, l'équipe qui garde le plus de bâtiments l'emporte.
    private void CheckVictory()
    {
        if (State != GameState.Battle) { Refresh(); return; }

        int blue = BlueCount, red = RedCount;
        int blueB = BuildingsOf(Team.Blue), redB = BuildingsOf(Team.Red);
        bool blueOut = blue == 0 && blueB == 0;
        bool redOut = red == 0 && redB == 0;
        bool noShooters = blue == 0 && red == 0;
        if (!blueOut && !redOut && !noShooters) { Refresh(); return; }

        string result, detail;
        if (blueOut && redOut)
        {
            result = "Égalité";
            detail = "Les deux camps sont rasés.";
        }
        else if (blueOut || redOut)
        {
            Team winner = redOut ? Team.Blue : Team.Red;
            result = winner == Team.Blue ? "Les bleus gagnent" : "Les rouges gagnent";
            detail = Survivors(CountAlive(winner), BuildingsOf(winner));
        }
        else
        {
            // Plus aucun soldat, mais des bâtiments des deux côtés.
            result = blueB == redB ? "Égalité" : blueB > redB ? "Les bleus gagnent" : "Les rouges gagnent";
            detail = $"Plus aucun soldat. Bâtiments restants : {blueB} bleu{(blueB > 1 ? "s" : "")}, {redB} rouge{(redB > 1 ? "s" : "")}.";
        }

        foreach (Unit u in Unit.All.ToArray())
            u.StopBattle();
        Bombs.Disarm();
        GameAudio.Victory();
        Haptics.Tap(60);
        ui.ShowResult(result, detail);
        SetState(GameState.GameOver);
    }

    private static string Survivors(int soldiers, int buildingsLeft)
    {
        string s = soldiers == 0 ? "Aucun soldat debout" : $"{soldiers} soldat{(soldiers > 1 ? "s" : "")} debout";
        if (buildingsLeft > 0) s += $", {buildingsLeft} bâtiment{(buildingsLeft > 1 ? "s" : "")} intact{(buildingsLeft > 1 ? "s" : "")}";
        return s + ".";
    }

    private static Team Other(Team t) => t == Team.Blue ? Team.Red : Team.Blue;

    // Rejouer : on remet les deux armées à leur position de départ, prêtes à être relancées ou modifiées.
    public void Replay()
    {
        ClearSoldiers();

        // Les bâtiments détruits pendant la bataille sont reconstruits à l'identique.
        SpawnBuilding builder = FindObjectOfType<SpawnBuilding>();
        if (builder != null && Terrain != null)
        {
            foreach (GameObject b in buildings.ToArray())
                if (b != null) Destroy(b);
            buildings.Clear();
            placementHistory.RemoveAll(g => g == null || buildings.Contains(g) || g.GetComponent<Building>() != null);
            foreach (var s in buildingSetup)
                builder.SpawnAt(Terrain.transform.TransformPoint(s.localPos), Terrain.transform.rotation * s.localRot, Terrain, s.team, false);
        }

        SpawnSoldier spawner = FindObjectOfType<SpawnSoldier>();
        if (spawner != null && Terrain != null)
        {
            foreach (var s in battleSetup)
            {
                GameObject soldier = spawner.SpawnAt(Terrain.transform.TransformPoint(s.localPos), Terrain);
                soldier.transform.rotation = Terrain.transform.rotation * s.localRot;
                RegisterSoldier(soldier, s.team, false);
            }
        }

        ui.HideResult();
        PlacingTeam = Team.Blue;
        SetState(GameState.Deployment);
    }

    // Nouvelles armées sur le même terrain : on retire les soldats et les bâtiments.
    public void ClearArmies()
    {
        ClearSoldiers();
        foreach (GameObject b in buildings.ToArray())
            if (b != null) Destroy(b);
        buildings.Clear();
        placementHistory.RemoveAll(g => g == null || g.GetComponent<Building>() != null);
        ui.HideResult();
        PlacingTeam = Team.Blue;
        SetState(GameState.Deployment);
    }

    private void ClearSoldiers()
    {
        foreach (Unit u in Unit.All.ToArray())
            if (u != null) Destroy(u.gameObject);
        Unit.All.Clear();
        placementHistory.RemoveAll(g => g == null || g.GetComponent<Unit>() != null);
    }

    // ---------- Placement ----------

    public bool CanPlaceSoldier(out string reason)
    {
        reason = null;
        if (State == GameState.Scanning || State == GameState.Sculpting) { reason = "Valide d'abord le terrain."; return false; }
        if (State != GameState.Deployment) { reason = "Bataille en cours."; return false; }
        if (CountAlive(PlacingTeam) >= maxSoldiersPerTeam)
        {
            reason = $"{maxSoldiersPerTeam} soldats {(PlacingTeam == Team.Blue ? "bleus" : "rouges")} maximum.";
            return false;
        }
        return true;
    }

    public bool CanPlaceBuilding(out string reason)
    {
        reason = null;
        if (State == GameState.Scanning) { reason = "Pose d'abord le terrain."; return false; }
        if (State == GameState.Sculpting) { reason = "Valide d'abord le terrain."; return false; }
        if (State == GameState.Battle || State == GameState.GameOver) { reason = "Bataille en cours."; return false; }
        if (BuildingsOf(PlacingTeam) >= maxBuildingsPerTeam)
        {
            reason = $"{maxBuildingsPerTeam} bâtiments {(PlacingTeam == Team.Blue ? "bleus" : "rouges")} maximum.";
            return false;
        }
        return true;
    }

    // Vrai si le point (en coordonnées monde) est dans la moitié de terrain de cette équipe.
    public bool IsInZone(Vector3 worldPoint, Team team)
    {
        if (Terrain == null) return false;
        Vector3 local = Terrain.transform.InverseTransformPoint(worldPoint);
        float mid = Terrain.height / 2f;
        bool lowZ = (team == Team.Blue) == blueIsLowZ;
        return lowZ ? local.z < mid - zoneMargin * 0.5f : local.z > mid + zoneMargin * 0.5f;
    }

    // Moitié de terrain où tombe un point, sans marge : sert à savoir quelle équipe bombarde.
    public Team HalfOf(Vector3 worldPoint)
    {
        if (Terrain == null) return Team.Blue;
        Vector3 local = Terrain.transform.InverseTransformPoint(worldPoint);
        bool low = local.z < Terrain.height / 2f;
        return low == blueIsLowZ ? Team.Blue : Team.Red;
    }

    // Orientation de départ d'un soldat : il regarde vers le camp adverse.
    public Quaternion FacingFor(Team team)
    {
        if (Terrain == null) return Quaternion.identity;
        bool lowZ = (team == Team.Blue) == blueIsLowZ;
        return Quaternion.LookRotation(lowZ ? Terrain.transform.forward : -Terrain.transform.forward);
    }

    public void RegisterSoldier(GameObject soldier, Team team, bool feedback = true)
    {
        // Le prefab contient l'ancien script de test (les deux soldats disparaissaient au contact) : on le retire.
        var legacy = soldier.GetComponent<SoldierPathFInding>();
        if (legacy != null) { legacy.enabled = false; Destroy(legacy); }

        Unit unit = soldier.GetComponent<Unit>();
        if (unit == null) unit = soldier.AddComponent<Unit>();
        unit.team = team;
        placementHistory.Add(soldier);
        if (feedback) { GameAudio.Place(); Haptics.Tap(); }
        Refresh();
    }

    public void OnBuildingDestroyed(Building building)
    {
        buildings.Remove(building.gameObject);
        ShowMessage(building.team == Team.Blue ? "Bâtiment bleu détruit." : "Bâtiment rouge détruit.");
        CheckVictory();
    }

    public void RegisterBuilding(GameObject building, bool feedback = true)
    {
        buildings.Add(building);
        placementHistory.Add(building);
        if (!feedback) { if (Terrain != null) Terrain.ClearDecorationsAround(building.transform.position, 0.2f); Refresh(); return; }
        if (Terrain != null) Terrain.ClearDecorationsAround(building.transform.position, 0.2f);
        GameAudio.Place();
        Haptics.Tap();
        Refresh();
    }

    public void ShowMessage(string message) => ui.Flash(message);

    public void RefreshUI() => Refresh();

    // Bouton « Jouer » de l'écran d'accueil.
    public void StartGame()
    {
        HasStarted = true;
        Refresh();
    }

    // ---------- Sculpture ----------

    public void ToggleSculptMode()
    {
        TerrainHeightController sculptor = TerrainHeightController.Active;
        if (sculptor == null || State != GameState.Sculpting)
        {
            ShowMessage("Appuie d'abord sur Modifier le terrain.");
            return;
        }

        bool on = !sculptor.SculptMode;
        if (on)
        {
            SpawnBuilding b = FindObjectOfType<SpawnBuilding>();
            if (b != null) b.CancelPlacement();
            SpawnSoldier s = FindObjectOfType<SpawnSoldier>();
            if (s != null) s.CancelPlacement();
        }
        sculptor.SetSculptMode(on);
    }

    // ---------- Ligne de front ----------

    private void ShowFrontLine(bool visible)
    {
        if (Terrain == null) return;

        if (frontLine == null)
        {
            var go = new GameObject("FrontLine");
            go.transform.SetParent(Terrain.transform, false);
            frontLine = go.AddComponent<LineRenderer>();
            frontLine.useWorldSpace = false;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            frontLine.material = new Material(shader);
            frontLine.startColor = frontLine.endColor = new Color(1f, 1f, 1f, 0.8f);
            frontLine.widthMultiplier = 0.03f;
            frontLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // La ligne suit le relief au milieu du terrain.
        int points = Terrain.width * 10 + 1;
        frontLine.positionCount = points;
        float mid = Terrain.height / 2f;
        for (int i = 0; i < points; i++)
        {
            float x = Terrain.width * i / (float)(points - 1);
            frontLine.SetPosition(i, new Vector3(x, Terrain.GetHeightAtPosition(x, mid) + 0.02f, mid));
        }
        frontLine.enabled = visible;
    }

    // ---------- Outils ----------

    // Le XR Origin de la scène contient des contrôleurs XRI (Left/Right Controller) dont le rayon
    // s'affiche en rouge sur le téléphone. Il ne sert à rien en AR mobile : on cache seulement son tracé.
    private static void HideControllerRays()
    {
        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "XRInteractorLineVisual") continue;
            mb.enabled = false;
            foreach (LineRenderer lr in mb.GetComponents<LineRenderer>()) lr.enabled = false;
        }
    }

    private static int CountAlive(Team team)
    {
        int n = 0;
        foreach (Unit u in Unit.All)
            if (u != null && u.team == team && u.IsAlive) n++;
        return n;
    }

    private void SetState(GameState state)
    {
        State = state;
        Refresh();
    }

    private void Refresh()
    {
        if (ui != null) ui.Refresh(this);
    }
}
