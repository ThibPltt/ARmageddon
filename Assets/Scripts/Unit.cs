using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum Team { Blue, Red }

// Un soldat en bataille : cherche l'ennemi le plus proche, s'approche à portée, tire jusqu'à la mort de l'un des deux.
[RequireComponent(typeof(NavMeshAgent))]
public class Unit : MonoBehaviour
{
    // Registre de toutes les unités vivantes, pour éviter les FindGameObjectsWithTag à chaque frame.
    public static readonly List<Unit> All = new List<Unit>();

    [Header("Équipe")]
    public Team team = Team.Blue;

    [Header("Combat")]
    public float maxHealth = 100f;
    public float damage = 25f;
    [Range(0f, 1f)] public float hitChance = 0.7f;
    public float fireInterval = 1f;       // secondes entre deux tirs
    public float range = 0.6f;            // portée de tir en unités du monde (le terrain fait 4 unités de côté)
    public float moveSpeed = 0.3f;
    public float retargetInterval = 0.5f;

    [Header("Avancée")]
    public float hearingRange = 1.6f;     // au-delà de la portée de tir : le soldat entend l'ennemi et marche droit sur lui
    public float advanceJitter = 0.2f;    // petit écart au hasard sur sa route, pour ne pas avancer en file indienne
    public float advanceSpeedFactor = 0.75f;

    public float Health { get; private set; }
    public bool IsAlive => Health > 0f;
    public bool InBattle { get; private set; }

    private NavMeshAgent agent;
    private Unit target;
    private float nextFireTime;
    private float nextRetargetTime;
    private Vector3 lastDestination = Vector3.positiveInfinity;
    private float nextAdvanceTime;
    private Building targetBuilding;
    private LineRenderer tracer;
    private static Material lineMaterial;

    public static Color TeamColor(Team t) => t == Team.Blue ? new Color(0.2f, 0.45f, 1f) : new Color(1f, 0.25f, 0.2f);

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        Health = maxHealth;

        // L'Animator du prefab a « Apply Root Motion » coché : il réécrit la position à chaque frame
        // et empêche le NavMeshAgent de déplacer le soldat. C'est l'agent qui doit piloter le déplacement.
        foreach (Animator anim in GetComponentsInChildren<Animator>())
            anim.applyRootMotion = false;

        ConfigureAgent();
    }

    // Aligne l'agent sur le NavMesh miniature du terrain (même type d'agent, même taille), puis le pose dessus.
    private void ConfigureAgent()
    {
        agent.enabled = false;
        agent.agentTypeID = ProceduralTerrain.NavAgentTypeId;
        float sx = Mathf.Max(0.0001f, Mathf.Max(transform.lossyScale.x, transform.lossyScale.z));
        float sy = Mathf.Max(0.0001f, transform.lossyScale.y);
        agent.radius = ProceduralTerrain.NavAgentRadius * 0.5f / sx;
        agent.height = ProceduralTerrain.NavAgentHeight / sy;
        agent.baseOffset = 0f;
        agent.enabled = true;
        SnapToNavMesh();
    }

    // Si le soldat n'est pas exactement sur le NavMesh, on le replace au point marchable le plus proche.
    public bool SnapToNavMesh()
    {
        if (agent == null || !agent.enabled) return false;
        if (agent.isOnNavMesh) return true;

        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, filter))
            return agent.Warp(hit.position);

        Debug.LogWarning($"{name} : aucun NavMesh à proximité, le soldat ne pourra pas bouger.");
        if (GameManager.Instance != null)
            GameManager.Instance.ShowMessage("Ce soldat est bloqué. Pose-le sur une zone plus plate.");
        return false;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        agent.speed = moveSpeed;
        agent.angularSpeed = 360f;
        agent.stoppingDistance = range * 0.8f;
        agent.acceleration = 2f;
        if (agent.isOnNavMesh) agent.isStopped = !InBattle;

        ApplyTeamVisuals();
        CreateTracer();

        // Petit décalage pour que tous les soldats ne tirent pas à la même frame.
        nextFireTime = Time.time + Random.Range(0f, fireInterval);
    }

    public void StartBattle()
    {
        InBattle = true;
        SnapToNavMesh();
        if (agent.isOnNavMesh) agent.isStopped = false;
    }

    public void StopBattle()
    {
        InBattle = false;
        target = null;
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
    }

    void Update()
    {
        if (!InBattle || !IsAlive) return;

        if (Time.time >= nextRetargetTime || target == null || !target.IsAlive)
        {
            Unit nearest = FindNearestEnemy();
            // Un ennemi assez proche pour être entendu attire le soldat ; sinon il avance vers le camp adverse.
            target = nearest != null && FlatDistance(nearest.transform.position) <= hearingRange ? nearest : null;
            nextRetargetTime = Time.time + retargetInterval;
            // Personne à portée d'oreille : il part détruire les bâtiments du camp adverse, sinon il avance vers l'ennemi.
            targetBuilding = target == null ? NextEnemyBuilding() : null;
            if (target == null && targetBuilding == null) Advance(nearest);
        }

        if (target == null)
        {
            if (targetBuilding != null && targetBuilding.IsStanding) AttackBuilding(targetBuilding);
            return;
        }

        // Distance à plat : une différence d'altitude n'empêche pas de tirer.
        float distance = FlatDistance(target.transform.position);

        // Si le relief empêche d'atteindre la cible (chemin incomplet et bout du chemin atteint),
        // le soldat tire de là où il est, dès qu'il l'entend : tir plongeant depuis la colline ou vers elle.
        float effectiveRange = range;
        if (agent.isOnNavMesh && !agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete
            && agent.remainingDistance <= agent.stoppingDistance + 0.05f)
            effectiveRange = hearingRange;

        if (distance > effectiveRange)
        {
            // Ne relance le calcul de chemin que si la cible a bougé.
            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.speed = moveSpeed;
                agent.stoppingDistance = range * 0.8f;
                if ((target.transform.position - lastDestination).sqrMagnitude > 0.0025f)
                {
                    agent.SetDestination(target.transform.position);
                    lastDestination = target.transform.position;
                }
            }
        }
        else
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            FaceTarget();

            if (Time.time >= nextFireTime)
            {
                Fire();
                nextFireTime = Time.time + fireInterval;
            }
        }
    }

    private float FlatDistance(Vector3 p)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    // Avancée vers l'ennemi le plus proche, avec un petit écart au hasard sur la route, sans errance.
    private void Advance(Unit nearestEnemy)
    {
        if (!agent.isOnNavMesh) return;
        agent.isStopped = false;
        agent.speed = moveSpeed * advanceSpeedFactor;

        bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.03f;
        if (!arrived && Time.time < nextAdvanceTime) return;

        if (nearestEnemy == null) return;
        Vector3 objective = nearestEnemy.transform.position;

        Vector2 r = Random.insideUnitCircle * advanceJitter;
        Vector3 goal = objective + new Vector3(r.x, 0f, r.y);

        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
        if (NavMesh.SamplePosition(goal, out NavMeshHit hit, 0.6f, filter))
        {
            agent.stoppingDistance = 0.05f;
            agent.SetDestination(hit.position);
            lastDestination = Vector3.positiveInfinity;
        }
        nextAdvanceTime = Time.time + Random.Range(2f, 3.5f);
    }

    // Le bâtiment ennemi le plus proche, encore debout.
    private Building NextEnemyBuilding()
    {
        Building best = null;
        float bestDist = float.MaxValue;
        foreach (Building b in Building.All)
        {
            if (b == null || !b.IsStanding) continue;
            if (b.team == team) continue;   // bâtiment de son propre camp
            float d = FlatDistance(b.transform.position);
            if (d < bestDist) { bestDist = d; best = b; }
        }
        return best;
    }

    // Marche jusqu'à portée du bâtiment, puis tire sur ses murs jusqu'à ce qu'il s'effondre (5 balles).
    private void AttackBuilding(Building b)
    {
        if (!agent.isOnNavMesh) return;
        Vector3 wall = b.WallPointToward(transform.position);
        float distance = FlatDistance(wall);

        // Relief infranchissable : il tire de là où il est, s'il est assez près pour viser.
        bool stuck = !agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete
                     && agent.remainingDistance <= agent.stoppingDistance + 0.05f;
        float reach = stuck ? hearingRange : range * 0.9f;

        if (distance > reach)
        {
            agent.isStopped = false;
            agent.speed = moveSpeed * advanceSpeedFactor;
            agent.stoppingDistance = range * 0.6f;
            if ((wall - lastDestination).sqrMagnitude > 0.0025f)
            {
                var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
                Vector3 goal = NavMesh.SamplePosition(wall, out NavMeshHit hit, 0.6f, filter) ? hit.position : wall;
                agent.SetDestination(goal);
                lastDestination = wall;
            }
            return;
        }

        agent.isStopped = true;
        FacePoint(wall);
        if (Time.time >= nextFireTime)
        {
            GameAudio.Shot();
            StartCoroutine(ShowTracer(wall, true));
            b.TakeShot();
            nextFireTime = Time.time + fireInterval;
        }
    }

    private Unit FindNearestEnemy()
    {
        Unit best = null;
        float bestDist = float.MaxValue;
        foreach (Unit u in All)
        {
            if (u == null || u.team == team || !u.IsAlive) continue;
            float d = (u.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = u;
            }
        }
        return best;
    }

    private void FaceTarget() => FacePoint(target.transform.position);

    private void FacePoint(Vector3 point)
    {
        Vector3 dir = point - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
    }

    private void Fire()
    {
        // Un soldat à l'abri d'un bâtiment est beaucoup plus dur à toucher :
        // les balles qui l'auraient touché sans le bâtiment s'écrasent sur les murs et l'abîment.
        Building cover = Building.CoverFor(target.transform.position);
        float roll = Random.value;
        bool hit = roll <= (cover != null ? hitChance * 0.4f : hitChance);
        bool blocked = cover != null && !hit && roll <= hitChance;
        GameAudio.Shot();

        if (blocked)
        {
            StartCoroutine(ShowTracer(cover.WallPointToward(transform.position), true));
            cover.TakeShot();
        }
        else
        {
            StartCoroutine(ShowTracer(target.transform.position, hit));
            if (hit) target.TakeDamage(damage);
        }
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive) return;
        Health -= amount;
        if (Health <= 0f)
        {
            Health = 0f;
            StartCoroutine(Die());
        }
    }

    private IEnumerator Die()
    {
        InBattle = false;
        All.Remove(this);
        if (agent.isOnNavMesh) agent.isStopped = true;
        agent.enabled = false;

        GameAudio.Death();
        if (GameManager.Instance != null)
            GameManager.Instance.OnUnitDied(this);

        // Le soldat bascule puis disparaît.
        Quaternion start = transform.rotation;
        Quaternion end = start * Quaternion.Euler(-90f, 0f, 0f);
        Vector3 startScale = transform.localScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.6f;
            transform.rotation = Quaternion.Slerp(start, end, t);
            if (t > 0.5f) transform.localScale = Vector3.Lerp(startScale, Vector3.zero, (t - 0.5f) * 2f);
            yield return null;
        }
        Destroy(gameObject);
    }

    // Teinte le soldat aux couleurs de son équipe et lui ajoute un disque de couleur sous les pieds.
    private void ApplyTeamVisuals()
    {
        Color c = TeamColor(team);
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is LineRenderer) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_Color", Color.Lerp(Color.white, c, 0.6f));
            block.SetColor("_BaseColor", Color.Lerp(Color.white, c, 0.6f));
            r.SetPropertyBlock(block);
        }

        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "TeamMarker";
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(transform, false);
        // Taille fixe dans le monde, quelle que soit l'échelle du prefab.
        float s = 1f / Mathf.Max(0.0001f, transform.lossyScale.x);
        disc.transform.localScale = new Vector3(0.09f * s, 0.003f * s, 0.09f * s);
        disc.transform.localPosition = new Vector3(0f, 0.005f * s, 0f);
        var discRenderer = disc.GetComponent<Renderer>();
        discRenderer.GetPropertyBlock(block);
        block.SetColor("_Color", c);
        block.SetColor("_BaseColor", c);
        discRenderer.SetPropertyBlock(block);
        discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void CreateTracer()
    {
        if (lineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            lineMaterial = new Material(shader);
        }

        var go = new GameObject("Tracer");
        go.transform.SetParent(transform, false);
        tracer = go.AddComponent<LineRenderer>();
        tracer.useWorldSpace = true;
        tracer.positionCount = 2;
        tracer.startWidth = 0.01f;
        tracer.endWidth = 0.004f;
        tracer.material = lineMaterial;
        tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tracer.enabled = false;
    }

    private IEnumerator ShowTracer(Vector3 targetPos, bool hit)
    {
        if (tracer == null) yield break;
        Color c = hit ? Color.yellow : new Color(1f, 1f, 1f, 0.5f);
        tracer.startColor = c;
        tracer.endColor = c;
        Vector3 muzzleOffset = Vector3.up * 0.06f;
        tracer.SetPosition(0, transform.position + muzzleOffset);
        tracer.SetPosition(1, targetPos + muzzleOffset);
        tracer.enabled = true;
        yield return new WaitForSeconds(0.06f);
        if (tracer != null) tracer.enabled = false;
    }
}
