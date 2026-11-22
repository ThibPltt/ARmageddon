using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Interface de la partie, construite par le code.
// Règles de mise en page :
// - tout tient dans la zone sûre de l'écran (encoche, barre de navigation) ;
// - en haut, ce qu'on lit (consigne, messages) ; en bas, sous le pouce, ce qu'on touche ;
// - une seule action principale par étape, en orange, tout en bas ; les outils de l'étape juste au-dessus ;
// - les boutons n'apparaissent qu'à l'étape où ils servent : relief, puis armées, puis bataille.
// Les boutons Soldats, Bâtiments et Lock de la scène sont repris ici, avec le même style que les autres.
public class GameUI : MonoBehaviour
{
    // Grille (pixels pour un écran de 1080 de large)
    private const float Margin = 30f;
    private const float Gap = 20f;
    private const float RowHeight = 140f;
    private const float FullWidth = 1080f - 2f * Margin;

    private RectTransform safeArea;
    private Rect lastSafeArea;

    private TextMeshProUGUI statusText;
    private TextMeshProUGUI flashText;

    // Actions principales (rangée du bas)
    private GameObject battleButton;
    private Button battleButtonComp;
    private GameObject blueBombButton;
    private GameObject redBombButton;
    private GameObject placeButton;
    private Button placeButtonComp;
    private GameObject sizePanel;
    private Slider sizeSlider;
    private TextMeshProUGUI sizeValue;

    // Outils
    private GameObject sculptButton;
    private TextMeshProUGUI sculptLabel;
    private GameObject raiseButton;
    private GameObject lowerButton;
    private GameObject brushPanel;
    private Slider brushSlider;
    private TextMeshProUGUI brushValue;
    private GameObject teamButton;
    private TextMeshProUGUI teamLabel;
    private GameObject undoButton;
    private GameObject restartButton;

    // Boutons de la scène, repris et restylés
    private GameObject soldiersButton;
    private GameObject buildingsButton;
    private GameObject lockButton;
    private TextMeshProUGUI lockLabel;
    private SpawnSoldier soldierSpawner;
    private SpawnBuilding buildingSpawner;

    private GameObject resultPanel;
    private GameObject trackingPanel;
    private TextMeshProUGUI trackingText;
    private bool welcomeSkipped;
    private TextMeshProUGUI resultTitle;
    private TextMeshProUGUI resultSubtitle;
    private Coroutine flashRoutine;
    private GameObject welcomePanel;
    private GameObject howToPanel;
    private readonly List<Canvas> hiddenCanvases = new List<Canvas>();

    // Style commun : le sprite arrondi et la police des boutons de la scène.
    private static Sprite roundSprite;
    private static TMP_FontAsset uiFont;

    // Logo de l'écran d'accueil : Assets/Resources/ARmageddon_logo.png (facultatif, sinon titre en texte).
    private const string LogoResource = "ARmageddon_logo";
    private static readonly Color Ink = new Color(0.055f, 0.063f, 0.133f);       // #0E1022, fond de l'accueil
    private static readonly Color Gold = new Color(0.95f, 0.70f, 0.24f);         // couleur du titre
    private static readonly Color Orange = new Color(0.91f, 0.45f, 0.17f);       // action principale
    private static readonly Color Lavender = new Color(0.76f, 0.77f, 1f);        // sous-titre
    private static readonly Color Paper = new Color(1f, 1f, 1f, 0.94f);          // bouton secondaire (comme ceux de la scène)
    private static readonly Color TextDark = new Color(0.196f, 0.196f, 0.196f);  // texte des boutons de la scène
    private static readonly Color Disabled = new Color(0.75f, 0.75f, 0.75f, 0.75f);
    private static readonly Color Danger = new Color(0.85f, 0.2f, 0.1f);

    void Awake()
    {
        GrabSceneStyle();

        var canvasGo = new GameObject("GameUI", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0f;
        canvasGo.AddComponent<GraphicRaycaster>();
        Transform root = canvasGo.transform;

        // Zone sûre : tout le jeu y est rangé, les écrans plein cadre (accueil, fin) restent sur la racine.
        var safeGo = new GameObject("SafeArea", typeof(RectTransform));
        safeGo.transform.SetParent(root, false);
        safeArea = (RectTransform)safeGo.transform;
        ApplySafeArea();
        Transform safe = safeArea;

        // ----- Haut de l'écran : on lit -----
        restartButton = CreateButton(safe, "RestartButton", "Nouveau terrain", TopLeft(Margin, Margin), new Vector2(330, 90),
                                     Paper, () => GameManager.Instance.NewTerrain());
        Anchor(restartButton, new Vector2(0, 1), new Vector2(0, 1));
        StyleLabel(restartButton, TextDark, 34);

        var statusBg = CreatePanel(safe, "Status", Vector2.zero, new Vector2(FullWidth, 170), new Color(0f, 0f, 0f, 0.6f));
        Anchor(statusBg, new Vector2(0.5f, 1), new Vector2(0.5f, 1));
        ((RectTransform)statusBg.transform).anchoredPosition = new Vector2(0, -(Margin + 90 + Gap));
        statusBg.GetComponent<Image>().raycastTarget = false;
        statusText = CreateText(statusBg.transform, "StatusText", Vector2.zero, new Vector2(FullWidth - 50, 150), 40, FontStyles.Normal);
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 28;
        statusText.fontSizeMax = 40;

        flashText = CreateText(safe, "Flash", Vector2.zero, new Vector2(FullWidth, 120), 42, FontStyles.Bold);
        Anchor(flashText.gameObject, new Vector2(0.5f, 1), new Vector2(0.5f, 1));
        ((RectTransform)flashText.transform).anchoredPosition = new Vector2(0, -(Margin + 90 + Gap + 170 + Gap));
        flashText.color = new Color(1f, 0.85f, 0.3f);
        flashText.gameObject.SetActive(false);

        // ----- Bas de l'écran : on touche -----
        // Rangée 1 (tout en bas) : l'action principale de l'étape.
        battleButton = CreateButton(safe, "BattleButton", "Lancer la bataille", Row(1), new Vector2(FullWidth, RowHeight + 10),
                                    Orange, () => GameManager.Instance.StartBattle());
        battleButtonComp = battleButton.GetComponent<Button>();
        float halfRow = (FullWidth - Gap) / 2f;
        blueBombButton = CreateButton(safe, "BlueBombButton", "Bombe bleue", Row(1, -(halfRow + Gap) / 2f), new Vector2(halfRow, RowHeight + 10),
                                      Unit.TeamColor(Team.Blue), () => GameManager.Instance.Bombs.ToggleArmed(Team.Blue));
        redBombButton = CreateButton(safe, "RedBombButton", "Bombe rouge", Row(1, (halfRow + Gap) / 2f), new Vector2(halfRow, RowHeight + 10),
                                     Unit.TeamColor(Team.Red), () => GameManager.Instance.Bombs.ToggleArmed(Team.Red));

        // Pose du terrain : action principale de l'étape de détection du sol.
        placeButton = CreateButton(safe, "PlaceButton", "Poser le terrain", Row(1), new Vector2(FullWidth, RowHeight + 10),
                                   Orange, () => { if (ARPlacement.Instance != null) ARPlacement.Instance.PlaceTerrain(); });
        placeButtonComp = placeButton.GetComponent<Button>();

        // Rangée 2 : les outils de l'étape.
        float third = (FullWidth - 2 * Gap) / 3f;
        undoButton = CreateButton(safe, "UndoButton", "Annuler", Vector2.zero, new Vector2(third, RowHeight),
                                  Paper, () => GameManager.Instance.Undo());
        StyleLabel(undoButton, TextDark, 44);
        sculptButton = CreateButton(safe, "SculptButton", "Sculpter", Vector2.zero, new Vector2(FullWidth - third - Gap, RowHeight),
                                    Paper, () => GameManager.Instance.ToggleSculptMode());
        sculptLabel = sculptButton.GetComponentInChildren<TextMeshProUGUI>();
        teamButton = CreateButton(safe, "TeamButton", "Équipe bleue", Vector2.zero, new Vector2(third, RowHeight),
                                  Unit.TeamColor(Team.Blue), () => GameManager.Instance.TogglePlacingTeam());
        teamLabel = teamButton.GetComponentInChildren<TextMeshProUGUI>();

        // Rangées 3 et 4 (mode sculpture) : Creuser / Monter, puis la taille de la zone.
        float half = (FullWidth - Gap) / 2f;
        lowerButton = CreateHoldButton(safe, "LowerButton", "Creuser", new Vector2(half, RowHeight), -1);
        raiseButton = CreateHoldButton(safe, "RaiseButton", "Monter", new Vector2(half, RowHeight), +1);
        Place(lowerButton, Row(3, -(half + Gap) / 2f));
        Place(raiseButton, Row(3, (half + Gap) / 2f));
        brushPanel = CreateSliderRow(safe, "BrushPanel", "Zone", 0.25f, 1f, 0.5f, out brushSlider, out brushValue, v =>
        {
            if (TerrainHeightController.Active != null) TerrainHeightController.Active.SetBrushRadius(v);
            brushValue.text = $"{Mathf.RoundToInt(v * 100f)}";
        });
        Place(brushPanel, Row(4) + new Vector2(0, -15f));

        // Taille réelle du terrain dans la pièce (côté, en mètres).
        float startSize = ARPlacement.Instance != null ? ARPlacement.Instance.SizeMeters : 2f;
        sizePanel = CreateSliderRow(safe, "SizePanel", "Terrain", 1f, 3f, startSize, out sizeSlider, out sizeValue, v =>
        {
            if (ARPlacement.Instance != null) ARPlacement.Instance.SetTerrainSize(v);
            sizeValue.text = $"{v:0.0} m";
        });
        sizeValue.text = $"{startSize:0.0} m";

        // Les boutons de la scène rejoignent la même grille.
        AdoptSceneButtons(safe);

        // Écran de fin.
        resultPanel = CreatePanel(root, "Result", Vector2.zero, new Vector2(920, 920), new Color(0f, 0f, 0f, 0.82f));
        resultTitle = CreateText(resultPanel.transform, "Title", new Vector2(0, 300), new Vector2(860, 140), 80, FontStyles.Bold);
        resultTitle.enableAutoSizing = true;
        resultTitle.fontSizeMin = 50;
        resultTitle.fontSizeMax = 80;
        resultSubtitle = CreateText(resultPanel.transform, "Subtitle", new Vector2(0, 170), new Vector2(860, 120), 44, FontStyles.Normal);
        resultSubtitle.enableAutoSizing = true;
        resultSubtitle.fontSizeMin = 30;
        resultSubtitle.fontSizeMax = 44;
        CreateButton(resultPanel.transform, "ReplayButton", "Rejouer la même bataille", new Vector2(0, -20), new Vector2(760, RowHeight),
                     Orange, () => GameManager.Instance.Replay());
        StyleLabel(CreateButton(resultPanel.transform, "RedeployButton", "Nouvelles armées", new Vector2(0, -180), new Vector2(760, RowHeight - 10),
                     Paper, () => GameManager.Instance.ClearArmies()), TextDark, 44);
        StyleLabel(CreateButton(resultPanel.transform, "NewTerrainButton", "Nouveau terrain", new Vector2(0, -330), new Vector2(760, RowHeight - 10),
                     Paper, () => GameManager.Instance.NewTerrain()), TextDark, 44);
        resultPanel.SetActive(false);

        // Suivi AR perdu : la partie est en pause.
        trackingPanel = CreatePanel(root, "TrackingLost", new Vector2(0, 0), new Vector2(960, 320), new Color(0.6f, 0.1f, 0.05f, 0.9f));
        trackingPanel.GetComponent<Image>().raycastTarget = false;
        var trackingTitle = CreateText(trackingPanel.transform, "Title", new Vector2(0, 70), new Vector2(920, 100), 56, FontStyles.Bold);
        trackingTitle.text = "Pause : suivi perdu";
        trackingText = CreateText(trackingPanel.transform, "Advice", new Vector2(0, -50), new Vector2(920, 160), 40, FontStyles.Normal);
        trackingPanel.SetActive(false);

        BuildWelcome(root);
        BuildHowTo(root);
    }

    void Start()
    {
        if (welcomeSkipped) return;

        // Les autres interfaces de la scène sont cachées tant que l'accueil est affiché.
        Canvas own = GetComponentInChildren<Canvas>();
        foreach (Canvas c in FindObjectsOfType<Canvas>())
        {
            if (c == own || !c.isRootCanvas || !c.enabled) continue;
            c.enabled = false;
            hiddenCanvases.Add(c);
        }
    }

    void Update()
    {
        if (Screen.safeArea != lastSafeArea) ApplySafeArea();
    }

    // ---------- Mise en page ----------

    private void ApplySafeArea()
    {
        Rect r = Screen.safeArea;
        lastSafeArea = r;
        if (Screen.width <= 0 || Screen.height <= 0) return;
        safeArea.anchorMin = new Vector2(r.xMin / Screen.width, r.yMin / Screen.height);
        safeArea.anchorMax = new Vector2(r.xMax / Screen.width, r.yMax / Screen.height);
        safeArea.offsetMin = Vector2.zero;
        safeArea.offsetMax = Vector2.zero;
    }

    // Centre d'un élément de la rangée n (1 = tout en bas), ancré en bas de l'écran.
    private static Vector2 Row(int n, float x = 0f)
    {
        float y = Margin + (RowHeight + 10f) / 2f + (n - 1) * (RowHeight + Gap);
        return new Vector2(x, y);
    }

    private static Vector2 TopLeft(float x, float y) => new Vector2(x, -y);

    // Ancre un élément (pivot compris) et garde sa position relative à cette ancre.
    private static void Anchor(GameObject go, Vector2 anchor, Vector2 pivot)
    {
        var rt = (RectTransform)go.transform;
        Vector2 pos = rt.anchoredPosition;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
    }

    // Place un élément dans la grille du bas (ancré en bas au centre).
    private static void Place(GameObject go, Vector2 pos)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
    }

    private static void PlaceRow(GameObject go, int row, float x, float width)
    {
        Place(go, Row(row, x));
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        var label = go.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) ((RectTransform)label.transform).sizeDelta = new Vector2(width - 20f, rt.sizeDelta.y);
    }

    private static void GrabSceneStyle()
    {
        if (roundSprite != null) return;
        foreach (string name in new[] { "Soldats", "Bâtiments", "LockButton" })
        {
            GameObject go = GameObject.Find(name);
            if (go == null) continue;
            Image img = go.GetComponent<Image>();
            if (img != null && img.sprite != null) roundSprite = img.sprite;
            TextMeshProUGUI t = go.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null && t.font != null) uiFont = t.font;
            if (roundSprite != null) break;
        }
    }

    // Reprend les boutons Soldats, Bâtiments et Lock de la scène : même grille, même style.
    private void AdoptSceneButtons(Transform safe)
    {
        soldierSpawner = FindObjectOfType<SpawnSoldier>();
        buildingSpawner = FindObjectOfType<SpawnBuilding>();
        soldiersButton = Adopt(safe, "Soldats");
        buildingsButton = Adopt(safe, "Bâtiments");
        lockButton = Adopt(safe, "LockButton");
        if (lockButton != null) lockLabel = lockButton.GetComponentInChildren<TextMeshProUGUI>();
    }

    private static GameObject Adopt(Transform safe, string name)
    {
        GameObject go = GameObject.Find(name);
        if (go == null) return null;
        var rt = (RectTransform)go.transform;
        rt.SetParent(safe, false);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.sizeDelta = new Vector2(300, RowHeight);
        Image img = go.GetComponent<Image>();
        if (img != null) { img.color = Paper; img.type = Image.Type.Sliced; }
        TextMeshProUGUI t = go.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null)
        {
            var trt = (RectTransform)t.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.localScale = Vector3.one;
            trt.sizeDelta = new Vector2(280, RowHeight);
            t.fontSize = 44;
            t.fontStyle = FontStyles.Bold;
            t.color = TextDark;
            t.alignment = TextAlignmentOptions.Center;
            t.enableAutoSizing = true;
            t.fontSizeMin = 28;
            t.fontSizeMax = 44;
        }
        go.SetActive(false);
        return go;
    }

    // Une rangée « étiquette — curseur — valeur », sur fond sombre.
    private GameObject CreateSliderRow(Transform safe, string name, string labelText, float min, float max, float value,
                                       out Slider slider, out TextMeshProUGUI valueText, UnityAction<float> onChange)
    {
        var panel = CreatePanel(safe, name, Vector2.zero, new Vector2(FullWidth, 110), new Color(0f, 0f, 0f, 0.55f));
        Place(panel, Row(3));
        panel.GetComponent<Image>().raycastTarget = false;

        var label = CreateText(panel.transform, "Label", new Vector2(-FullWidth / 2f + 120, 0), new Vector2(200, 100), 36, FontStyles.Bold);
        label.text = labelText;
        valueText = CreateText(panel.transform, "Value", new Vector2(FullWidth / 2f - 100, 0), new Vector2(170, 100), 36, FontStyles.Normal);

        var sliderGo = new GameObject("Slider", typeof(RectTransform));
        sliderGo.transform.SetParent(panel.transform, false);
        var srt = (RectTransform)sliderGo.transform;
        srt.anchoredPosition = new Vector2(10, 0);
        srt.sizeDelta = new Vector2(FullWidth - 460, 60);

        var bg = CreatePanel(sliderGo.transform, "Background", Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.35f));
        Stretch((RectTransform)bg.transform);
        ((RectTransform)bg.transform).offsetMin = new Vector2(0, 18);
        ((RectTransform)bg.transform).offsetMax = new Vector2(0, -18);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fart = (RectTransform)fillArea.transform;
        Stretch(fart);
        fart.offsetMin = new Vector2(0, 18);
        fart.offsetMax = new Vector2(0, -18);
        var fill = CreatePanel(fillArea.transform, "Fill", Vector2.zero, Vector2.zero, Orange);
        Stretch((RectTransform)fill.transform);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        Stretch((RectTransform)handleArea.transform);
        var handle = CreatePanel(handleArea.transform, "Handle", Vector2.zero, new Vector2(70, 0), Color.white);
        var hrt = (RectTransform)handle.transform;
        hrt.anchorMin = new Vector2(0, 0);
        hrt.anchorMax = new Vector2(0, 1);
        hrt.offsetMin = new Vector2(-35, -5);
        hrt.offsetMax = new Vector2(35, 5);

        slider = sliderGo.AddComponent<Slider>();
        slider.fillRect = (RectTransform)fill.transform;
        slider.handleRect = hrt;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.onValueChanged.AddListener(onChange);
        return panel;
    }

    // ---------- Écran d'accueil ----------

    private void BuildWelcome(Transform root)
    {
        welcomePanel = CreatePanel(root, "Welcome", Vector2.zero, Vector2.zero, new Color(Ink.r, Ink.g, Ink.b, 0.96f));
        Stretch((RectTransform)welcomePanel.transform);
        welcomePanel.AddComponent<CanvasGroup>();
        Transform w = welcomePanel.transform;

        Texture2D logo = Resources.Load<Texture2D>(LogoResource);
        if (logo != null)
        {
            var logoGo = new GameObject("Logo", typeof(RectTransform), typeof(Image));
            logoGo.transform.SetParent(w, false);
            var rt = (RectTransform)logoGo.transform;
            rt.anchoredPosition = new Vector2(0, 430);
            rt.sizeDelta = new Vector2(980, 760);
            var img = logoGo.GetComponent<Image>();
            img.sprite = Sprite.Create(logo, new Rect(0, 0, logo.width, logo.height), new Vector2(0.5f, 0.5f));
            img.preserveAspect = true;
            img.raycastTarget = false;
        }
        else
        {
            var title = CreateText(w, "Title", new Vector2(0, 470), new Vector2(1000, 220), 150, FontStyles.Bold);
            title.text = "<color=#F2B33D>AR</color>mageddon";
            title.color = Color.white;
        }

        var tagline = CreateText(w, "Tagline", new Vector2(0, 80), new Vector2(940, 140), 48, FontStyles.Normal);
        tagline.text = "Des batailles miniatures sur ton sol, en réalité augmentée.";
        tagline.color = Lavender;

        CreateButton(w, "PlayButton", "Jouer", new Vector2(0, -220), new Vector2(720, 180), Orange, OnPlay)
            .GetComponentInChildren<TextMeshProUGUI>().fontSize = 72;

        var how = CreateButton(w, "HowToButton", "Comment jouer", new Vector2(0, -440), new Vector2(720, 130),
                               new Color(1f, 1f, 1f, 0.12f), () => howToPanel.SetActive(true));
        how.GetComponentInChildren<TextMeshProUGUI>().fontStyle = FontStyles.Normal;

        var hint = CreateText(w, "Hint", new Vector2(0, -620), new Vector2(940, 100), 36, FontStyles.Italic);
        hint.text = "Il faut environ 2 m × 2 m de sol libre.";
        hint.color = new Color(1f, 1f, 1f, 0.6f);

        var credit = CreateText(w, "Credit", new Vector2(0, -860), new Vector2(940, 80), 30, FontStyles.Normal);
        credit.text = "Thibaut Platet  ·  ENSIM  ·  Unity & AR Foundation";
        credit.color = new Color(1f, 1f, 1f, 0.45f);
    }

    private void BuildHowTo(Transform root)
    {
        howToPanel = CreatePanel(root, "HowTo", Vector2.zero, Vector2.zero, new Color(Ink.r, Ink.g, Ink.b, 0.98f));
        Stretch((RectTransform)howToPanel.transform);
        Transform h = howToPanel.transform;

        var title = CreateText(h, "HowToTitle", new Vector2(0, 640), new Vector2(940, 140), 70, FontStyles.Bold);
        title.text = "Comment jouer";
        title.color = Gold;

        var steps = CreateText(h, "Steps", new Vector2(0, 20), new Vector2(920, 1060), 38, FontStyles.Normal);
        steps.alignment = TextAlignmentOptions.TopLeft;
        steps.enableAutoSizing = true;
        steps.fontSizeMin = 28;
        steps.fontSizeMax = 38;
        steps.lineSpacing = 8f;
        steps.paragraphSpacing = 18f;
        const string b = "<color=#F2B33D><b>";   // nom d'un bouton
        const string e = "</b></color>";
        steps.text =
            "<b>But du jeu</b>\nDeux armées s'affrontent sur un terrain posé sur ton sol. Une équipe gagne quand l'autre n'a plus ni soldat ni bâtiment.\n" +
            "<b>1. Poser le terrain</b>\nVise le sol et bouge doucement le téléphone. Quand le cadre orange apparaît, règle la taille et touche " + b + "Poser le terrain" + e + ".\n" +
            "<b>2. Façonner le relief</b> (facultatif)\nTouche " + b + "Sculpter" + e + ", puis un point du terrain. Maintiens " + b + "Monter" + e + " ou " + b + "Creuser" + e + ". Les creux deviennent des lacs. Touche " + b + "Valider le terrain" + e + " pour passer à la suite.\n" +
            "<b>3. Placer les armées</b>\nUne ligne blanche coupe le terrain : les bleus de ton côté, les rouges en face. Le bouton d'équipe change la couleur. Chaque équipe a jusqu'à 30 soldats et 3 bâtiments, qui servent d'abri.\n" +
            "<b>4. Lancer la bataille</b>\nLes soldats avancent et tirent seuls. Chaque équipe a 2 bombes : touche son bouton, puis l'endroit visé. Tourne autour du terrain pour suivre l'action.";

        CreateButton(h, "GotIt", "Compris", new Vector2(0, -720), new Vector2(600, 150), Orange, () => howToPanel.SetActive(false));
        howToPanel.SetActive(false);
    }

    // Après « Nouveau terrain » : pas d'accueil, la partie reprend directement.
    public void SkipWelcome()
    {
        welcomeSkipped = true;
        welcomePanel.SetActive(false);
        foreach (Canvas c in hiddenCanvases)
            if (c != null) c.enabled = true;
        hiddenCanvases.Clear();
    }

    public void ShowTrackingLost(bool lost, string advice)
    {
        trackingPanel.SetActive(lost);
        if (lost) trackingText.text = advice;
    }

    private void OnPlay()
    {
        StartCoroutine(FadeOutWelcome());
        foreach (Canvas c in hiddenCanvases)
            if (c != null) c.enabled = true;
        hiddenCanvases.Clear();
        GameManager.Instance.StartGame();
    }

    private IEnumerator FadeOutWelcome()
    {
        var group = welcomePanel.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
        {
            group.alpha = 1f - t;
            yield return null;
        }
        welcomePanel.SetActive(false);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public void Refresh(GameManager gm)
    {
        if (statusText == null) return;

        bool started = gm.HasStarted;
        GameState state = gm.State;
        TerrainHeightController sculptor = TerrainHeightController.Active;
        bool sculptMode = sculptor != null && sculptor.SculptMode;

        bool sculpting = started && state == GameState.Sculpting;
        bool deploying = started && state == GameState.Deployment;
        bool battle = started && state == GameState.Battle;

        statusText.transform.parent.gameObject.SetActive(started);
        restartButton.SetActive(started && (sculpting || deploying || battle));

        // ----- Étape 1 : le relief -----
        // Rangée 1 : Valider le terrain. Rangée 2 : Annuler + Sculpter. Mode sculpture : Creuser / Monter + Taille.
        // ----- Étape 2 : les armées -----
        // Rangée 1 : Lancer la bataille. Rangée 2 : Soldats, Bâtiments, Équipe. Rangée 3 : Annuler + Modifier le terrain.
        float third = (FullWidth - 2 * Gap) / 3f;
        float wide = FullWidth - third - Gap;

        if (lockButton != null)
        {
            lockButton.SetActive(sculpting || deploying);
            if (sculpting)
            {
                PlaceRow(lockButton, 1, 0f, FullWidth);
                ((RectTransform)lockButton.transform).sizeDelta = new Vector2(FullWidth, RowHeight + 10);
                SetButtonLook(lockButton, Orange, Color.white);
                lockLabel.text = "Valider le terrain";
            }
            else if (deploying)
            {
                PlaceRow(lockButton, 3, (third + Gap) / 2f, wide);
                ((RectTransform)lockButton.transform).sizeDelta = new Vector2(wide, RowHeight);
                SetButtonLook(lockButton, Paper, TextDark);
                lockLabel.text = "Modifier le terrain";
            }
        }

        sculptButton.SetActive(sculpting);
        if (sculpting)
        {
            PlaceRow(sculptButton, 2, (third + Gap) / 2f, wide);
            SetButtonLook(sculptButton, sculptMode ? Orange : Paper, sculptMode ? Color.white : TextDark);
            sculptLabel.text = sculptMode ? "Terminer" : "Sculpter";
        }

        bool showArrows = sculpting && sculptMode && sculptor.HasSelection;
        raiseButton.SetActive(showArrows);
        lowerButton.SetActive(showArrows);
        brushPanel.SetActive(sculpting && sculptMode);
        if (sculpting && sculptMode && sculptor != null && !Mathf.Approximately(brushSlider.value, sculptor.brushRadius))
            brushSlider.SetValueWithoutNotify(sculptor.brushRadius);
        if (sculptor != null) brushValue.text = $"{Mathf.RoundToInt(brushSlider.value * 100f)}";

        // Taille du terrain : avant la pose (le viseur suit), et pendant le relief hors mode sculpture.
        bool scanning = started && state == GameState.Scanning;
        sizePanel.SetActive(scanning || (sculpting && !sculptMode));
        if (scanning) Place(sizePanel, Row(2));
        else if (sculpting) Place(sizePanel, Row(3) + new Vector2(0, -15f));

        bool spot = ARPlacement.Instance != null && ARPlacement.Instance.HasValidSpot;
        placeButton.SetActive(scanning);
        placeButtonComp.interactable = spot;
        SetButtonLook(placeButton, spot ? Orange : Disabled, Color.white);

        undoButton.SetActive((sculpting || deploying) && gm.CanUndo);
        if (sculpting) PlaceRow(undoButton, 2, -(wide + Gap) / 2f, third);
        else if (deploying) PlaceRow(undoButton, 3, -(wide + Gap) / 2f, third);

        if (soldiersButton != null)
        {
            soldiersButton.SetActive(deploying);
            PlaceRow(soldiersButton, 2, -(third + Gap), third);
            bool on = soldierSpawner != null && soldierSpawner.IsPlacing;
            SetButtonLook(soldiersButton, on ? Orange : Paper, on ? Color.white : TextDark);
        }
        if (buildingsButton != null)
        {
            buildingsButton.SetActive(deploying);
            PlaceRow(buildingsButton, 2, 0f, third);
            bool on = buildingSpawner != null && buildingSpawner.IsPlacing;
            SetButtonLook(buildingsButton, on ? Orange : Paper, on ? Color.white : TextDark);
        }
        teamButton.SetActive(deploying);
        if (deploying)
        {
            PlaceRow(teamButton, 2, third + Gap, third);
            SetButtonLook(teamButton, Unit.TeamColor(gm.PlacingTeam), Color.white);
            teamLabel.text = gm.PlacingTeam == Team.Blue ? "Équipe bleue" : "Équipe rouge";
        }

        bool armiesReady = gm.BlueCount > 0 && gm.RedCount > 0;
        battleButton.SetActive(deploying);
        battleButtonComp.interactable = armiesReady;
        SetButtonLook(battleButton, armiesReady ? Orange : Disabled, Color.white);

        // ----- Étape 3 : la bataille -----
        UpdateBombButton(blueBombButton, gm, Team.Blue, battle);
        UpdateBombButton(redBombButton, gm, Team.Red, battle);

        if (!started) return;

        switch (state)
        {
            case GameState.Scanning:
                statusText.text = "<b>1 · Terrain</b>\n" + (spot
                    ? "Règle la taille, puis pose le terrain."
                    : "Vise le sol et balaye doucement jusqu'au cadre orange.");
                break;
            case GameState.Sculpting:
                statusText.text = "<b>2 · Relief</b>\n" + (!sculptMode
                    ? "Sculpte si tu veux, puis valide le terrain."
                    : sculptor.HasSelection
                        ? "Maintiens Monter ou Creuser. Touche ailleurs pour changer de point."
                        : "Touche le terrain pour choisir un point.");
                break;
            case GameState.Deployment:
                statusText.text = "<b>3 · Armées</b>\n" + (gm.BlueCount + gm.RedCount + gm.BuildingsOf(Team.Blue) + gm.BuildingsOf(Team.Red) == 0
                    ? "Bleus de ton côté de la ligne, rouges en face."
                    : $"<color=#4D8CFF>Bleus {gm.BlueCount}/{gm.maxSoldiersPerTeam} · bât. {gm.BuildingsOf(Team.Blue)}/{gm.maxBuildingsPerTeam}</color>    " +
                      $"<color=#FF5A4D>Rouges {gm.RedCount}/{gm.maxSoldiersPerTeam} · bât. {gm.BuildingsOf(Team.Red)}/{gm.maxBuildingsPerTeam}</color>");
                break;
            case GameState.Battle:
                statusText.text = "<b>4 · Bataille</b>" + (gm.Bombs.Armed ? "  ·  touche l'endroit à bombarder" : "") + "\n" +
                                  $"<color=#4D8CFF>Bleus {gm.BlueCount} · bât. {gm.BuildingsOf(Team.Blue)}</color>    " +
                                  $"<color=#FF5A4D>Rouges {gm.RedCount} · bât. {gm.BuildingsOf(Team.Red)}</color>";
                break;
            case GameState.GameOver:
                statusText.text = "Partie terminée.";
                break;
        }
    }

    // Bouton de bombe d'une équipe : couleur de l'équipe, orange quand elle est armée, caché quand le stock est vide.
    private static void UpdateBombButton(GameObject button, GameManager gm, Team team, bool battle)
    {
        int left = gm.Bombs.RemainingFor(team);
        button.SetActive(battle && left > 0);
        bool armed = gm.Bombs.IsArmed(team);
        string name = team == Team.Blue ? "bleue" : "rouge";
        button.GetComponentInChildren<TextMeshProUGUI>().text = armed ? $"Bombe {name} armée" : $"Bombe {name} ({left})";
        SetButtonLook(button, armed ? Orange : Unit.TeamColor(team), Color.white);
    }

    private static void SetButtonLook(GameObject button, Color background, Color textColor)
    {
        if (button == null) return;
        Image img = button.GetComponent<Image>();
        if (img != null) img.color = background;
        TextMeshProUGUI t = button.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.color = textColor;
    }

    private static void StyleLabel(GameObject button, Color color, float size)
    {
        TextMeshProUGUI t = button.GetComponentInChildren<TextMeshProUGUI>();
        if (t == null) return;
        t.color = color;
        t.fontSize = size;
        t.fontSizeMax = size;
    }

    public void Flash(string message)
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine(message));
    }

    private IEnumerator FlashRoutine(string message)
    {
        flashText.text = message;
        flashText.gameObject.SetActive(true);
        yield return new WaitForSeconds(2.5f);
        flashText.gameObject.SetActive(false);
    }

    public void ShowResult(string title, string subtitle)
    {
        resultTitle.text = title;
        resultSubtitle.text = subtitle;
        resultPanel.SetActive(true);
    }

    public void HideResult() => resultPanel.SetActive(false);

    // ---------- Fabrique d'éléments ----------

    private static GameObject CreatePanel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        if (roundSprite != null)
        {
            img.sprite = roundSprite;
            img.type = Image.Type.Sliced;
        }
        return go;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, Vector2 pos, Vector2 size, float fontSize, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var text = go.AddComponent<TextMeshProUGUI>();
        if (uiFont != null) text.font = uiFont;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateHoldButton(Transform parent, string name, string label, Vector2 size, int direction)
    {
        var go = CreatePanel(parent, name, Vector2.zero, size, Paper);
        var img = go.GetComponent<Image>();
        var text = CreateText(go.transform, "Label", Vector2.zero, size, 46, FontStyles.Bold);
        text.text = label;
        text.color = TextDark;

        // Retour visuel pendant l'appui : le bouton passe en orange.
        var hold = go.AddComponent<HoldButton>();
        hold.onPress.AddListener(() =>
        {
            img.color = Orange; text.color = Color.white;
            if (TerrainHeightController.Active != null) TerrainHeightController.Active.SetHold(direction);
        });
        hold.onRelease.AddListener(() =>
        {
            img.color = Paper; text.color = TextDark;
            if (TerrainHeightController.Active != null) TerrainHeightController.Active.SetHold(0);
        });
        return go;
    }

    private static GameObject CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color, UnityAction onClick)
    {
        var go = CreatePanel(parent, name, pos, size, color);
        var button = go.AddComponent<Button>();
        button.onClick.AddListener(onClick);
        var text = CreateText(go.transform, "Label", Vector2.zero, new Vector2(size.x - 20f, size.y), 46, FontStyles.Bold);
        text.text = label;

        // Boutons du jeu : ancrés en bas de l'écran, sous le pouce, et le texte s'adapte à la largeur.
        if (parent.name == "SafeArea")
        {
            Place(go, pos);
            text.enableAutoSizing = true;
            text.fontSizeMin = 26;
            text.fontSizeMax = 46;
        }
        return go;
    }
}
