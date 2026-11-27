using System.Collections.Generic;
using UnityEngine;

public enum ReplayPlanLayer
{
    Mine,
    Enemy,
    Both,
}

/// <summary>
/// Shows a recorded round on the live board, Chess.com style: step to a round and the board takes
/// its opening position, scrub and the round plays out. Nothing live is posed or moved — the real
/// units are only hidden, and stand-in copies stripped of every behaviour are posed in their place —
/// so closing the viewer hands back exactly the board that was there.
/// </summary>
public class ReplayViewer : MonoBehaviour
{
    private const float TimelapseSeconds = 0.35f;
    private const float ScrubPlanAlpha = 0.35f;
    private const float RoundStartTolerance = 0.05f;
    private const float TracerSeconds = 0.14f;
    private const float AbilityPulseSeconds = 0.45f;
    private const float TracerWidth = 0.07f;
    private const float PulseRingWidth = 0.09f;
    private const int PulseRingSegments = 40;
    private const float MovingThreshold = 0.05f;

    public static bool IsViewing { get; private set; }

    private GameLoop loop;
    private GameHUDController boundHud;
    private ReplayPlanLayer layer = ReplayPlanLayer.Both;
    private bool postMatch;

    private int shownRound = -1;
    private float shownTime;

    private readonly List<(int round, float from, float to)> timelapse = new();
    private float timelapseElapsed = -1f;
    private int timelapseSourceRound;
    private float timelapseSourceAlpha = 1f;
    private int landingRound = -1;
    private bool closeOnLanding;

    private Transform stage;
    private readonly Dictionary<(int team, int slot), StandIn> standIns = new();

    private GameObject planRoot;
    private GameObject minePlans;
    private GameObject enemyPlans;
    private int planRound = -1;
    private readonly List<(LineRenderer line, Color start, Color end)> planLines = new();
    private readonly List<(Material material, Color color)> planFills = new();

    private GameObject smokeVisual;
    private int smokeRound = -1;

    private readonly List<(Renderer renderer, bool wasOff)> hiddenRenderers = new();
    private readonly List<(Canvas canvas, bool wasEnabled)> hiddenCanvases = new();
    private readonly HashSet<Vector2Int> reshownWalls = new();
    private readonly HashSet<Vector2Int> wantedWalls = new();

    private readonly Dictionary<int, LineRenderer> effects = new();
    private int effectsRound = -1;
    private Material effectMaterial;

    private sealed class StandIn
    {
        public GameObject body;
        public Animator rig;
        public string state;
        public float standHeight;
    }

    public void Bind(GameLoop owner) => loop = owner;

    private void OnDestroy()
    {
        if (IsViewing)
            Close(false);
        if (effectMaterial != null)
            Destroy(effectMaterial);
    }

    private bool MatchOver => loop != null && loop.LastMatchResult.HasValue;

    private static bool RoundPlanningOpen =>
        PlanMovement.Instance != null && PlanMovement.Instance.IsRoundPlanningOpen;

    private ReplayArchive Archive => loop != null ? loop.Replays : null;

    private void Update()
    {
        if (loop == null)
            return;

        BindHud();

        if (IsViewing && !postMatch && (!RoundPlanningOpen || MatchOver))
            Close(false);
        else if (IsViewing && postMatch && !MatchOver)
            Close(false);

        if (IsViewing && timelapseElapsed >= 0f)
            AdvanceTimelapse(Time.unscaledDeltaTime);

        RefreshBar();
    }

    private void LateUpdate()
    {
        if (!IsViewing)
            return;

        // Fog and smoke concealment both write these flags on their own schedule.
        foreach ((Renderer renderer, bool _) in hiddenRenderers)
        {
            if (renderer != null)
                renderer.forceRenderingOff = true;
        }
    }

    private void BindHud()
    {
        GameHUDController hud = GameHUDController.Instance;
        if (hud == boundHud)
            return;

        boundHud = hud;
        if (hud == null)
            return;
        hud.SetReplayHandlers(Step, Scrub);
        hud.SetReplayLayerHandler(SetLayer);
        hud.SetWatchReplayHandler(OpenPostMatch);
    }

    private void RefreshBar()
    {
        if (boundHud == null)
            return;

        ReplayArchive archive = Archive;
        bool hasRounds = archive != null && archive.Count > 0;
        boundHud.SetWatchReplayAvailable(hasRounds);

        if (!IsViewing)
        {
            boundHud.SetReplayBar(hasRounds && RoundPlanningOpen && !MatchOver, false, false, 1f);
            boundHud.SetReplayLayer(layer, false);
            return;
        }

        float duration = archive.TryGet(shownRound, out ReplayRound round) ? round.Duration : 0f;
        bool canStepBack = PreviousRound(shownRound) >= 0 || shownTime > RoundStartTolerance;
        boundHud.SetReplayBar(
            canStepBack,
            true,
            timelapseElapsed < 0f,
            duration > 0f ? shownTime / duration : 0f
        );
        boundHud.SetReplayLayer(layer, true);
    }

    // === CONTROLS ===

    private void Step(int direction)
    {
        ReplayArchive archive = Archive;
        if (archive == null || archive.Count == 0)
            return;

        if (!IsViewing)
        {
            if (direction >= 0 || !RoundPlanningOpen || MatchOver)
                return;

            int latest = archive.LatestRoundNumber;
            if (!archive.TryGet(latest, out ReplayRound live))
                return;
            Open(false);
            ShowAt(latest, live.Duration);
            BeginTimelapse(latest, 0f, false);
            return;
        }

        if (direction < 0)
        {
            int previous = PreviousRound(shownRound);
            if (shownTime > RoundStartTolerance || previous < 0)
                BeginTimelapse(shownRound, 0f, false);
            else
                BeginTimelapse(previous, 0f, false);
            return;
        }

        int next = NextRound(shownRound);
        BeginTimelapse(next >= 0 ? next : shownRound, 0f, next < 0);
    }

    private void Scrub(float progress)
    {
        if (!IsViewing || !Archive.TryGet(shownRound, out ReplayRound round))
            return;

        timelapseElapsed = -1f;
        ShowAt(shownRound, Mathf.Clamp01(progress) * round.Duration);
    }

    private void SetLayer(ReplayPlanLayer chosen)
    {
        layer = chosen;
        ApplyLayer();
    }

    private void OpenPostMatch()
    {
        ReplayArchive archive = Archive;
        if (!MatchOver || archive == null || archive.Count == 0)
            return;

        int first = -1;
        foreach (int number in archive.RoundNumbers)
        {
            first = number;
            break;
        }

        boundHud?.HideResultsForReplay();
        Open(true);
        ShowAt(first, 0f);
    }

    private int PreviousRound(int roundNumber)
    {
        int previous = -1;
        foreach (int number in Archive.RoundNumbers)
        {
            if (number >= roundNumber)
                break;
            previous = number;
        }
        return previous;
    }

    private int NextRound(int roundNumber)
    {
        foreach (int number in Archive.RoundNumbers)
        {
            if (number > roundNumber)
                return number;
        }
        return -1;
    }

    // === OPEN / CLOSE ===

    private void Open(bool afterMatch)
    {
        postMatch = afterMatch;
        if (IsViewing)
            return;

        IsViewing = true;
        stage = new GameObject("ReplayStage").transform;
        PathSelection.Instance?.CancelCurrentDrag();
        PlanMovement.Instance?.SetPlanningVisualsHidden(true);
        loop.SetLiveBoardOverlaysVisible(false);
        HideLiveUnits();
    }

    private void Close(bool returnToResults)
    {
        if (!IsViewing)
            return;

        IsViewing = false;
        timelapseElapsed = -1f;
        timelapse.Clear();
        closeOnLanding = false;

        RestoreLiveUnits();
        foreach (Vector2Int cell in reshownWalls)
        {
            if (loop != null && loop.TryGetWallInstance(cell, out GameObject wall))
                wall.SetActive(false);
        }
        reshownWalls.Clear();

        if (loop != null)
            loop.SetLiveBoardOverlaysVisible(true);
        PlanMovement.Instance?.SetPlanningVisualsHidden(false);

        if (stage != null)
            Destroy(stage.gameObject);
        stage = null;
        standIns.Clear();
        planRoot = null;
        planRound = -1;
        planLines.Clear();
        planFills.Clear();
        smokeVisual = null;
        smokeRound = -1;
        effects.Clear();
        effectsRound = -1;
        shownRound = -1;

        if (returnToResults && postMatch)
            boundHud?.ReturnToResults();
        postMatch = false;
    }

    private void HideLiveUnits()
    {
        hiddenRenderers.Clear();
        hiddenCanvases.Clear();
        foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            foreach (Renderer renderer in unit.GetComponentsInChildren<Renderer>(true))
            {
                hiddenRenderers.Add((renderer, renderer.forceRenderingOff));
                renderer.forceRenderingOff = true;
            }
            foreach (Canvas canvas in unit.GetComponentsInChildren<Canvas>(true))
            {
                hiddenCanvases.Add((canvas, canvas.enabled));
                canvas.enabled = false;
            }
        }
    }

    private void RestoreLiveUnits()
    {
        foreach ((Renderer renderer, bool wasOff) in hiddenRenderers)
        {
            if (renderer != null)
                renderer.forceRenderingOff = wasOff;
        }
        foreach ((Canvas canvas, bool wasEnabled) in hiddenCanvases)
        {
            if (canvas != null)
                canvas.enabled = wasEnabled;
        }
        hiddenRenderers.Clear();
        hiddenCanvases.Clear();
    }

    // === TIMELAPSE ===

    /// <summary>
    /// Runs the board from where it is now to the start of <paramref name="targetRound"/> through
    /// the recorded frames in between, so stepping reads as time moving rather than a cut. Asked
    /// again mid-flight, it simply sets off from wherever the board has got to.
    /// </summary>
    private void BeginTimelapse(int targetRound, float targetTime, bool closeAfter)
    {
        timelapse.Clear();
        timelapseSourceRound = shownRound;
        timelapseSourceAlpha = PlanAlphaAt(shownTime);
        landingRound = targetRound;
        closeOnLanding = closeAfter;

        if (closeAfter)
        {
            if (Archive.TryGet(shownRound, out ReplayRound current))
                timelapse.Add((shownRound, shownTime, current.Duration));
        }
        else if (targetRound == shownRound)
        {
            timelapse.Add((shownRound, shownTime, targetTime));
        }
        else if (targetRound < shownRound)
        {
            timelapse.Add((shownRound, shownTime, 0f));
            if (Archive.TryGet(targetRound, out ReplayRound earlier))
                timelapse.Add((targetRound, earlier.Duration, targetTime));
        }
        else if (Archive.TryGet(shownRound, out ReplayRound current))
        {
            timelapse.Add((shownRound, shownTime, current.Duration));
            timelapse.Add((targetRound, 0f, targetTime));
        }

        timelapseElapsed = 0f;
        AdvanceTimelapse(0f);
    }

    private void AdvanceTimelapse(float deltaTime)
    {
        timelapseElapsed += deltaTime;
        float linear = Mathf.Clamp01(timelapseElapsed / TimelapseSeconds);
        float eased = linear * linear * (3f - 2f * linear);

        float total = 0f;
        foreach ((int _, float from, float to) in timelapse)
            total += Mathf.Abs(to - from);

        float travelled = eased * total;
        int round = landingRound;
        float time = 0f;
        foreach ((int segmentRound, float from, float to) in timelapse)
        {
            float length = Mathf.Abs(to - from);
            round = segmentRound;
            if (travelled <= length)
            {
                time = length > 0f ? Mathf.Lerp(from, to, travelled / length) : to;
                break;
            }
            travelled -= length;
            time = to;
        }

        float fade = eased < 0.5f ? 1f - eased * 2f : eased * 2f - 1f;
        int planFor = eased < 0.5f ? timelapseSourceRound : closeOnLanding ? -1 : landingRound;
        float planAlpha = fade * (eased < 0.5f ? timelapseSourceAlpha : 1f);
        ShowAt(round, time, planFor, planAlpha);

        if (linear < 1f)
            return;

        timelapseElapsed = -1f;
        timelapse.Clear();
        if (closeOnLanding)
        {
            Close(true);
            return;
        }
        ShowAt(landingRound, 0f);
    }

    // === POSING ===

    private static float PlanAlphaAt(float time) => time <= RoundStartTolerance ? 1f : ScrubPlanAlpha;

    private void ShowAt(int roundNumber, float time) =>
        ShowAt(roundNumber, time, roundNumber, PlanAlphaAt(time));

    private void ShowAt(int roundNumber, float time, int planForRound, float planAlpha)
    {
        if (stage == null || !Archive.TryGet(roundNumber, out ReplayRound round))
            return;

        shownRound = roundNumber;
        shownTime = Mathf.Clamp(time, 0f, round.Duration);

        PoseStandIns(round, shownTime);
        ShowWallsAt(roundNumber, shownTime);
        ShowSmoke(round);
        ShowEffects(round, shownTime);
        ShowPlans(planForRound, planAlpha);
    }

    private void PoseStandIns(ReplayRound round, float time)
    {
        foreach (StandIn standIn in standIns.Values)
        {
            if (standIn.body != null)
                standIn.body.SetActive(false);
        }

        for (int i = 0; i < round.units.Count; i++)
        {
            ReplayUnit unit = round.units[i];
            if (!round.TrySample(i, time, out Vector3 position, out float yaw, out ReplaySample nearest) || !nearest.Alive)
                continue;

            StandIn standIn = GetStandIn(unit);
            if (standIn == null)
                continue;

            bool moving =
                round.TrySample(i, Mathf.Max(0f, time - ReplayRound.FrameInterval), out Vector3 earlier, out _, out _)
                && (position - earlier).sqrMagnitude > MovingThreshold * MovingThreshold;
            standIn.body.SetActive(true);
            standIn.body.transform.SetPositionAndRotation(
                new Vector3(position.x, standIn.standHeight, position.z),
                Quaternion.Euler(0f, yaw, 0f)
            );

            string state = moving ? "Moving" : "Idle";
            if (standIn.state != state)
            {
                standIn.state = state;
                AnimationHandler.EnterState(standIn.rig, state);
            }
        }
    }

    private StandIn GetStandIn(ReplayUnit unit)
    {
        if (standIns.TryGetValue((unit.teamIndex, unit.rosterSlot), out StandIn existing))
            return existing;

        GameObject prefab =
            loop.allUnits != null
            && unit.catalogIndex >= 0
            && unit.catalogIndex < loop.allUnits.units.Count
                ? loop.allUnits.units[unit.catalogIndex].unitModel
                : null;
        if (prefab == null)
            return null;

        // Built under an inactive parent so not one of the prefab's behaviours wakes up — a stand-in
        // that ran Unit.Awake or spawned a NetworkObject would join the match it is only depicting.
        GameObject holder = new("ReplayStandInHolder");
        holder.SetActive(false);
        holder.transform.SetParent(stage, false);
        GameObject body = Instantiate(prefab, holder.transform);
        body.name = $"ReplayStandIn_{unit.teamIndex}_{unit.rosterSlot}";
        List<Material> teamMaterials = prefab.GetComponent<Unit>()?.teamMaterials;
        CharacterCarousel.StripForDisplay(body);
        body.transform.SetParent(stage, false);
        Destroy(holder);
        Unit.ApplyTeamIndicators(
            body,
            teamMaterials,
            GameLoop.IsTeamFriendlyToLocalPlayer(unit.teamIndex)
        );

        StandIn standIn = new()
        {
            body = body,
            rig = body.GetComponentInChildren<Animator>(),
            standHeight = unit.standHeight / ReplaySample.PositionScale,
        };
        standIns[(unit.teamIndex, unit.rosterSlot)] = standIn;
        return standIn;
    }

    /// <summary>
    /// Stands back up every wall that was still standing at this moment: the ones brought down
    /// later in this round, and every one brought down in a later round.
    /// </summary>
    private void ShowWallsAt(int roundNumber, float time)
    {
        wantedWalls.Clear();
        foreach (int number in Archive.RoundNumbers)
        {
            if (number < roundNumber || !Archive.TryGet(number, out ReplayRound round))
                continue;
            foreach (ReplayEvent replayEvent in round.events)
            {
                if (
                    replayEvent.kind == ReplayEventKind.WallDestroyed
                    && (number > roundNumber || replayEvent.Time > time)
                )
                {
                    wantedWalls.Add(GridSystem.ConvertToGridCoords(replayEvent.from));
                }
            }
        }

        reshownWalls.RemoveWhere(cell =>
        {
            if (wantedWalls.Contains(cell))
                return false;
            if (loop.TryGetWallInstance(cell, out GameObject wall))
                wall.SetActive(false);
            return true;
        });

        foreach (Vector2Int cell in wantedWalls)
        {
            if (
                !reshownWalls.Contains(cell)
                && loop.TryGetWallInstance(cell, out GameObject wall)
                && !wall.activeSelf
            )
            {
                wall.SetActive(true);
                reshownWalls.Add(cell);
            }
        }
    }

    private void ShowSmoke(ReplayRound round)
    {
        if (smokeRound == round.roundNumber)
            return;

        smokeRound = round.roundNumber;
        if (smokeVisual != null)
            Destroy(smokeVisual);
        smokeVisual = null;
        if (round.smokeCells.Count == 0)
            return;

        List<Vector3> positions = new(round.smokeCells.Count);
        foreach (Vector2Int cell in round.smokeCells)
            positions.Add(GameLoop.gridCoordToWorld(cell));
        smokeVisual = SmokeScreenVisual.Create(stage, positions, GameLoop.cellSize).gameObject;
    }

    // === EFFECTS ===

    /// <summary>
    /// Tracers and ability pulses are a pure function of the time shown, so a scrub in either
    /// direction draws the same frame and nothing has to be spawned and cleaned up as it plays.
    /// </summary>
    private void ShowEffects(ReplayRound round, float time)
    {
        if (effectsRound != round.roundNumber)
        {
            foreach (LineRenderer effect in effects.Values)
            {
                if (effect != null)
                    Destroy(effect.gameObject);
            }
            effects.Clear();
            effectsRound = round.roundNumber;
        }

        for (int i = 0; i < round.events.Count; i++)
        {
            ReplayEvent replayEvent = round.events[i];
            float life = replayEvent.kind switch
            {
                ReplayEventKind.Shot => TracerSeconds,
                ReplayEventKind.Ability => AbilityPulseSeconds,
                _ => 0f,
            };
            float age = time - replayEvent.Time;
            bool live = life > 0f && age >= 0f && age < life;

            if (!effects.TryGetValue(i, out LineRenderer effect))
            {
                if (!live)
                    continue;
                effect = CreateEffect(round, replayEvent);
                effects[i] = effect;
            }

            effect.gameObject.SetActive(live);
            if (!live)
                continue;

            float remaining = 1f - age / life;
            Color color = effect.startColor;
            color.a = remaining;
            effect.startColor = effect.endColor = color;
            if (replayEvent.kind == ReplayEventKind.Ability)
                SetRing(effect, replayEvent.to, GameLoop.cellSize * (0.3f + 0.9f * (1f - remaining)));
        }
    }

    private LineRenderer CreateEffect(ReplayRound round, ReplayEvent replayEvent)
    {
        effectMaterial ??= new Material(Shader.Find("Sprites/Default"));

        GameObject host = new(replayEvent.kind == ReplayEventKind.Shot ? "ReplayTracer" : "ReplayPulse");
        host.transform.SetParent(stage, false);
        LineRenderer line = host.AddComponent<LineRenderer>();
        line.sharedMaterial = effectMaterial;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        bool friendly = GameLoop.IsTeamFriendlyToLocalPlayer(round.units[replayEvent.unitIndex].teamIndex);
        Color color = replayEvent.kind == ReplayEventKind.Ability
            ? TeamPalette.AbilityBright
            : TeamPalette.BrightForViewer(friendly);
        line.startColor = line.endColor = color;

        if (replayEvent.kind == ReplayEventKind.Shot)
        {
            line.widthMultiplier = TracerWidth;
            line.positionCount = 2;
            line.SetPosition(0, replayEvent.from);
            line.SetPosition(1, replayEvent.to);
        }
        else
        {
            line.widthMultiplier = PulseRingWidth;
            line.loop = true;
            line.positionCount = PulseRingSegments;
        }
        return line;
    }

    private static void SetRing(LineRenderer ring, Vector3 centre, float radius)
    {
        for (int i = 0; i < PulseRingSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / PulseRingSegments;
            ring.SetPosition(
                i,
                new Vector3(
                    centre.x + Mathf.Cos(angle) * radius,
                    PlanPathStyle.AbilityPathHeight,
                    centre.z + Mathf.Sin(angle) * radius
                )
            );
        }
    }

    // === PLANS ===

    private void ShowPlans(int roundNumber, float alpha)
    {
        if (planRound != roundNumber)
            BuildPlans(roundNumber);

        if (planRoot == null)
            return;

        planRoot.SetActive(alpha > 0.01f);
        foreach ((LineRenderer line, Color start, Color end) in planLines)
        {
            if (line == null)
                continue;
            line.startColor = start.WithAlpha(start.a * alpha);
            line.endColor = end.WithAlpha(end.a * alpha);
        }
        foreach ((Material material, Color color) in planFills)
        {
            if (material != null)
                material.color = color.WithAlpha(color.a * alpha);
        }
    }

    private void ApplyLayer()
    {
        if (minePlans != null)
            minePlans.SetActive(layer != ReplayPlanLayer.Enemy);
        if (enemyPlans != null)
            enemyPlans.SetActive(layer != ReplayPlanLayer.Mine);
    }

    private void BuildPlans(int roundNumber)
    {
        if (planRoot != null)
            Destroy(planRoot);
        planRoot = null;
        planLines.Clear();
        planFills.Clear();
        planRound = roundNumber;
        if (roundNumber < 0 || stage == null || !Archive.TryGet(roundNumber, out ReplayRound round))
            return;

        planRoot = new GameObject("ReplayPlans");
        planRoot.transform.SetParent(stage, false);
        minePlans = new GameObject("Mine");
        minePlans.transform.SetParent(planRoot.transform, false);
        enemyPlans = new GameObject("Enemy");
        enemyPlans.transform.SetParent(planRoot.transform, false);

        foreach (ReplayPlan plan in round.plans)
        {
            if (plan.kind is ReplayPlanKind.Held or ReplayPlanKind.Eliminated)
                continue;

            ReplayUnit unit = round.units[plan.unitIndex];
            bool friendly = GameLoop.IsTeamFriendlyToLocalPlayer(unit.teamIndex);
            Transform parent = (friendly ? minePlans : enemyPlans).transform;
            Color color = friendly
                ? PlanPathStyle.GetRouteColor(unit.rosterSlot, true)
                : TeamPalette.EnemyBright;

            if (plan.IsAbility)
                BuildAbilityPlan(plan, parent, color);
            else
                BuildRoutePlan(plan, unit, parent, friendly, color);
        }

        foreach (LineRenderer line in planRoot.GetComponentsInChildren<LineRenderer>(true))
            planLines.Add((line, line.startColor, line.endColor));
        foreach (Renderer renderer in planRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is LineRenderer)
                continue;
            Material material = renderer.material;
            planFills.Add((material, material.color));
        }
        ApplyLayer();
    }

    private static void BuildRoutePlan(
        ReplayPlan plan,
        ReplayUnit unit,
        Transform parent,
        bool friendly,
        Color color
    )
    {
        if (plan.route.Count < 2)
            return;

        List<Vector3> cells = new(plan.route.Count);
        foreach (Vector2Int cell in plan.route)
            cells.Add(GameLoop.gridCoordToWorld(cell));

        PathRibbon ribbon = PathRibbon.Create(parent, $"ReplayRoute_{unit.teamIndex}_{unit.rosterSlot}", unit.rosterSlot);
        ribbon.SetSelected(true);
        ribbon.SetRoute(cells, null);
        if (friendly)
            return;

        foreach (LineRenderer line in ribbon.GetComponentsInChildren<LineRenderer>(true))
            line.startColor = line.endColor = color;
    }

    private static void BuildAbilityPlan(ReplayPlan plan, Transform parent, Color color)
    {
        if (plan.abilityPath.Count >= 2)
        {
            AbilityPathIndicator
                .Create(parent, "ReplayAbilityPath")
                .Show(plan.abilityPath, plan.abilityPathKind, color);
        }

        Vector3 square = plan.abilitySquare;
        if (plan.abilityCone)
        {
            if (!plan.originKnown)
                return;
            Vector3 direction = square - plan.abilityOrigin;
            direction.y = 0f;
            if (direction.sqrMagnitude > 1e-6f)
            {
                SuppressingFire.CreateConePreview(
                    parent,
                    plan.abilityOrigin,
                    direction,
                    color.WithAlpha(0.8f),
                    plan.abilityRadiusCells,
                    "ReplayAbilityCone",
                    plan.abilityConeHalfSpread
                );
            }
        }
        else if (plan.abilityLine)
        {
            if (plan.originKnown && plan.abilityPath.Count < 2)
            {
                GameObject laser = new("ReplayAbilityLine");
                laser.transform.SetParent(parent, false);
                LineRenderer line = laser.AddComponent<LineRenderer>();
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = line.endColor = color.WithAlpha(0.8f);
                line.startWidth = line.endWidth = 0.15f;
                line.positionCount = 2;
                line.SetPosition(0, plan.abilityOrigin);
                line.SetPosition(1, square);
            }
            if (plan.abilityRadiusCells > 0f)
                GameLoop.BuildTelegraphBlastDisc(parent, square, plan.abilityRadiusCells, color);
        }
        else if (plan.abilitySmoke)
        {
            Vector2Int centre = GridSystem.ConvertToGridCoords(square);
            foreach (Vector2Int cell in GridSystem.GetSquareFootprint(centre, Smoke.FootprintRadius))
                GameLoop.BuildTelegraphCellOutline(parent, cell, color);
        }
        else if (plan.abilityShield)
        {
            GameLoop.BuildTelegraphCellOutline(parent, GridSystem.ConvertToGridCoords(plan.abilityPicked), color);
        }
        else if (plan.abilityRadiusCells > 0f)
        {
            GameLoop.BuildTelegraphBlastDisc(parent, square, plan.abilityRadiusCells, color);
        }
        else
        {
            GameLoop.BuildTelegraphCellOutline(parent, GridSystem.ConvertToGridCoords(square), color);
        }
    }
}
