# Fog of War + Damage Rebalance — Implementation Plan

**Prepared:** 2026-07-17  
**Phase:** Phase 1 only (read/reconcile/plan). No Unity-loaded file was changed while preparing this document.  
**Target:** Unity 6000.3.1f1, Netcode for GameObjects 2.7.0, host-based 1v1.

This is the mechanical source for Phase 2. It reconciles
`FogOfWar-and-DamageRebalance.md` with the current ability/dodge pipeline and dev harness. The
approved gameplay choices remain the same unless a deviation is explicitly called out under
“Risks and recommendations.”

## 1. Current-code reconciliation

### 1.1 Stale or omitted statements in the design spec

| Spec statement or omission | Current code | Phase 2 resolution |
|---|---|---|
| Ability activation is disabled; `PlanMovement.AbilitySelection` is a TODO. | Ability selection is live. Plans use `(true, [startCell, targetSquare])`; `SanitizeAbilityPlan` validates them; `CollectAbilityActivations`, `RunDodgePhase`, and `RunAbility` execute them. | Treat abilities as shipping behavior. Fog integration and RPC auditing cover the complete live path. Do not edit the commented historical `ActivateAbility.cs` or `Reroute.cs`. |
| Dodge/response is only a future interaction. | `RunDodgePhase` telegraphs every activation, identifies threatened units, enables their `UnitCanvas/Alert`, accepts sanitized dive paths, replaces planned movement, and cancels a dodger’s own ability. | Keep all telegraphs fog-piercing. Defender-owned dodge objects remain network-visible to that defender. Add no fog gate to dodge path submission or validation. |
| Area Lock is the only important new ability RPC audit. | `Shield.ToggleShieldClientRpc`, three live Area Lock ClientRpcs, GameLoop’s telegraph/dodge ClientRpcs, and NetworkHelper’s alert RPC are all active. | Convert Shield state to a NetworkVariable; force-reveal Area Lock long enough for all three object-scoped RPCs; leave the GameLoop/NetworkHelper scene-object RPCs in place after verifying reference resolution rules. |
| `Shooting` laser NetworkVariables automatically self-heal after `NetworkShow`. | The state is networked, but the `LineRenderer` is created in `Start`. A newly shown object receives initial NetworkVariable values around `OnNetworkSpawn`; initial synchronization is not safely handled by the current callbacks, and callback order can encounter a null renderer. | Move network-dependent setup into `OnNetworkSpawn`, create/cache the renderer before subscribing, and explicitly apply all current values on spawn. |
| `Shield.ToggleShieldClientRpc` can remain a low-priority Phase 2 enhancement. | Shield is now reachable every round. A unit can hide while the shield is active or reveal after the “on” RPC was dropped. | This is no longer safely deferrable. Replace the RPC with `NetworkVariable<bool> shieldActive` in this implementation. |
| Health’s death ClientRpc is the only death concern. | `Health.TakeDamage` relies on host-inline ClientRpc execution to update the private health field before testing for death. It then calls a scene-object RPC containing a reference to the unit. Hidden clients cannot resolve that reference. | Make health and alive state NetworkVariables. Apply death locally from `isAlive`; keep the server root active through one network-update opportunity, then deactivate it server-side so `teamSize` remains correct. No unit-reference death RPC is needed. |
| One-time `SetGroupLayerClientRpc` only needs to happen before the initial hide. | A hidden dynamic prefab can run its spawn lifecycle again on `NetworkShow`, so non-networked layer writes are not guaranteed to be replayed. | No current client-authoritative gameplay depends on an enemy’s team layer: shooting/abilities are server-only, planning raycasts use `Grid`, and `Unit.OnNetworkSpawn` restores team-indicator materials. Leave this unchanged for scope control, but verify re-shown objects and record it as a future hardening risk. NetworkTransform supplies current position on show. |
| Fog can toggle `Renderer.enabled` on the host. | Unit variants contain intentionally disabled renderers, Shield toggles a child at runtime, and the target-lock `LineRenderer` also changes enabled state. Setting every renderer back to `true` would corrupt those states. | Explicit deviation: use `Renderer.forceRenderingOff` for host fog and `Canvas.enabled` for `UnitCanvas`. This preserves each renderer/GameObject’s actual state while enforcing local visual hiding. |
| Changing `Grenade.cs` from 50 to 80 is sufficient. | `Grenade.damage` is public/serialized and `Soldier.prefab` explicitly stores `damage: 50`. A new field initializer does not overwrite serialized prefab data. | Change both the code default and `Assets/Prefabs/Units/Soldier.prefab` serialized component value, and make the field `[SerializeField] private`. |
| Server movement sanitization may need wall validation. | `SanitizePaths` already rejects `wallLayout` cells; `SanitizeAbilityPlan` rejects wall targets except for line abilities. | No sanitizer change. Add regression assertions only. |
| There is no dev harness to preserve. | `GameLoop.devMode` defaults false, dev planning and dodge windows wait for explicit submission, and `DevInput` can submit both teams’ plans and dump authoritative state. | Do not change `DevInput`, its phase strings, indefinite waits, fast-forward behavior, or server arrays. Host-local fog uses visual suppression only, so `DevInput.Dump()` still sees both teams. |
| `TESTING` and dev mode are effectively one mode. | `TESTING` remains `true`; `devMode` is independent and defaults `false`. | Leave both values unchanged. Fog-specific tests must reposition units because TESTING spawns are mutually visible. |
| The canonical design doc uniformly describes the completed ability rework. | `GAME_DESIGN.md` accurately describes the live flow in §2 and §8, but still says abilities are “planned/not functional” in §1 and “activation pipeline disabled” in the §4 lead-in. | Update those stale paragraphs after Phase 2 verification, together with the fog and damage tables. |
| A baseline testing document exists at `docs/TESTING_BASELINE.md`. | That file is not present in the current tree. The single rerunnable E2E artifact mentioned in the task was also not present while this plan was prepared. | At Phase 2 start, locate the artifact after the other agent finishes it and use its actual steps as the regression checklist. Do not invent or overwrite it. |

### 1.2 Live RPC/replicated-state audit

The key distinction is the NetworkBehaviour that owns the RPC:

| Flow | Owner/scope | Hidden-client behavior | Resolution |
|---|---|---|---|
| `GameLoop.ShowAbilityTelegraphClientRpc` / `HideAbilityTelegraphsClientRpc` | GameManager scene NetworkObject | Delivered even if the caster unit is hidden. Payload contains world positions, not a unit reference. | Keep. These are the authoritative fog-piercing dodge telegraphs. |
| `GameLoop.StartDodgePlanningClientRpc` | GameManager scene NetworkObject, targeted to defender | Delivered. Its unit references are defender-owned units, which fog never hides from that defender. | Keep. Verify all refs resolve on the defender. |
| `NetworkHelper.SetActiveClientRpc(..., "UnitCanvas/Alert", ...)` | NetworkHelper scene NetworkObject, but payload references a unit | Defender resolves its own threatened units and sees alerts. An attacker may not resolve a hidden defender; that dropped attacker-side icon is harmless. Host attacker can resolve the object, but its enemy UnitCanvas remains canvas-disabled by host fog. | Keep. The required recipient always has the object. Clear RPC follows the same rule. Expected hidden-reference warnings, if emitted, should be assessed during verification. |
| `GameLoop.setOverlayUITextClientRpc`, camera flash/shake, card setup/disable, and end-game RPCs | Always-visible scene NetworkObjects | Delivered. | No fog change. |
| `Health.SetHealthClientRpc` / `SetHealthBarClientRpc` | Unit NetworkObject | Dropped while hidden and not replayed. | Remove; use NetworkVariables and local spawn/value handlers. |
| Existing death `NetworkHelper.SetActiveClientRpc` with unit reference | Scene object with hidden unit reference | Hidden client cannot resolve; a later show could produce stale state. | Remove from death path; use `isAlive`. |
| `Shooting` laser NetworkVariables | Unit NetworkObject | Values resync on show, but current renderer setup does not reliably apply initial values. | Retain variables; repair `OnNetworkSpawn` initialization/application. |
| `Shield.ToggleShieldClientRpc` | Hidden unit NetworkObject | Dropped; active shield state can be wrong on reveal. | Replace with `shieldActive` NetworkVariable. |
| `AreaLock.ShowLaserClientRpc`, `StartRushClientRpc`, `HideLaserClientRpc` | Hidden sniper NetworkObject | All can be dropped. The rush/cleanup may outlast the spec’s simple `abilityTime + 1` estimate. | Call `ForceRevealToEnemyTeams` before any delay/RPC for `delayForDodge + abilityTime + 1.5f` (5 seconds with current constants). The team-aware deadline drives bot targeting too; the initial orange GameLoop line remains independently visible during dodge. |
| Grenade projectile and explosion | Independent spawned NetworkObjects; camera shake is scene-scoped | Visible globally even if caster is hidden. | Keep; blind grenade cues are intentional. |
| Pogo movement | Unit NetworkTransform | Hidden clients receive no transform; a reveal respawns/syncs current position. | Keep. No forced reveal on landing. |
| `SetGroupLayerClientRpc` / `SyncHeightAdjustedPositionClientRpc` | Scene objects with references | Initial calls happen before the first delayed fog pass. They are not state containers. | Keep initial order. NetworkTransform handles position on show. Team-layer replay is a documented low-risk gap. |

### 1.3 Resulting implementation invariants

1. Terrain is never hidden; pooled dark cells only dim cells outside local team vision.
2. A remote enemy unit outside vision is absent from that client’s NGO spawn table.
3. The host always owns full server data; enemy presentation is suppressed with
   `forceRenderingOff` and `Canvas.enabled`.
4. Own units are never hidden and always contribute vision while alive.
5. Team vision is the union of current friendly cells using the configured Manhattan range and
   deterministic permissive-corner grid LoS.
6. Ability telegraphs are always visible. They can disclose a target cell or line even while the
   caster model remains hidden.
7. Area Lock and sniper weapon target lock additionally force-reveal the caster to the affected
   enemy client.
8. Health, death, Shield, and sniper-laser state survive hide/show because they are replicated
   state, not one-shot unit RPC state.
9. Dev input remains server-authoritative and can inspect both teams regardless of host visuals.

## 2. Apply order and exact edits

### Step 1 — `Assets/Scripts/Units/Health.cs`

Replace the current class body with the following. This is intentionally first because every later
visibility path relies on resynchronizable health/alive state.

```csharp
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Health : NetworkBehaviour
{
    public UnitData unitData;

    private NetworkVariable<float> currentHealth = new();
    private NetworkVariable<bool> isAlive = new(true);

    private Transform unitCanvas;
    private Transform healthBar;
    private Transform healthFill;

    private const float TypicalMaxHealth = 100f;

    public float CurrentHealth => currentHealth.Value;
    public bool IsAlive => isAlive.Value;

    public override void OnNetworkSpawn()
    {
        unitCanvas = transform.Find("UnitCanvas");
        healthBar = unitCanvas?.Find("HealthBar");
        healthFill = healthBar?.Find("HealthFill");

        currentHealth.OnValueChanged += OnHealthChanged;
        isAlive.OnValueChanged += OnAliveChanged;

        if (IsServer)
        {
            isAlive.Value = true;
            currentHealth.Value = unitData.maxHealth;
        }

        UpdateMaxHealthScale();
        UpdateHealthFill(currentHealth.Value);

        // A hidden unit that died before NetworkShow must immediately stay absent on this client.
        if (!IsServer && !isAlive.Value)
            gameObject.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        isAlive.OnValueChanged -= OnAliveChanged;
        base.OnNetworkDespawn();
    }

    void Update()
    {
        if (!IsClient)
            return;

        if (GameLoop.Instance?.TeamCamera != null && unitCanvas != null)
            unitCanvas.forward = GameLoop.Instance.TeamCamera.transform.forward;
    }

    public void TakeDamage(float damage)
    {
        if (!IsServer || !isAlive.Value)
            return;

        currentHealth.Value = Mathf.Max(0f, currentHealth.Value - damage);
        if (currentHealth.Value > 0f)
            return;

        isAlive.Value = false;
        GameLoop.Instance.unitCards.GetComponent<UnitCardContainer>().DisableUnitCard(gameObject);

        // Leave the NetworkObject active through this frame's network update so the final
        // NetworkVariable values can be sent, then preserve the existing tag-based teamSize rule.
        StartCoroutine(DeactivateOnServerNextFrame());
    }

    private IEnumerator DeactivateOnServerNextFrame()
    {
        yield return null;
        if (IsServer && !isAlive.Value)
            gameObject.SetActive(false);
    }

    private void OnHealthChanged(float previousValue, float newValue)
    {
        UpdateHealthFill(newValue);
    }

    private void OnAliveChanged(bool previousValue, bool newValue)
    {
        // The host/server deactivates after one network-update opportunity above.
        if (!IsServer && !newValue)
            gameObject.SetActive(false);
    }

    private void UpdateHealthFill(float health)
    {
        if (healthFill == null)
            return;

        healthFill.localScale = new Vector3(
            Mathf.Clamp(health / unitData.maxHealth, 0f, 1f),
            1f,
            1f
        );
    }

    private void UpdateMaxHealthScale()
    {
        if (healthBar == null)
            return;

        healthBar.localScale = new Vector3(
            Mathf.Clamp(unitData.maxHealth / TypicalMaxHealth, 0f, 1f),
            1f,
            1f
        );
    }
}
```

Delete `SetHealthClientRpc` and `SetHealthBarClientRpc`. `CurrentHealth` and `IsAlive` are read-only
public observability properties for GameLoop and Phase 2 assertions; writes remain server-only.

### Step 2 — `Assets/Scripts/Units/UnitData.cs`

Insert this section after targeting parameters and before health parameters:

```csharp
    [Header("=== VISION PARAMETERS ===")]
    [Tooltip("Manhattan vision range in cells; must be at least targetRange")]
    public int visionRange = 5;
```

No new script is introduced.

### Step 3 — five `Assets/UnitStats/*.asset` files

Add an explicit `visionRange` entry after `aimRotationSpeed` and change only `damage` as follows:

| Asset | `damage` current → new | `visionRange` missing/default → new | `targetRange` (unchanged) |
|---|---:|---:|---:|
| `Commander.asset` | 5 → **12** | → **6** | 5 |
| `PogoRider.asset` | 15 → **30** | → **7** | 7 |
| `Shotgunner.asset` | 10 → **18** | → **3** | 2 |
| `Sniper.asset` | 70 → **110** | → **8** | 7 |
| `Soldier.asset` | 10 → **25** | → **5** | 4 |

Do not change health, fire rate, magazine, reload, range, spread, movement, backstab, ability fields,
GUIDs, `.meta` files, or `AllUnits.asset`. Every configured vision range remains at least its
weapon target range.

### Step 4 — `Assets/Scripts/GameManager/GridSystem.cs`

Add these constants and methods. Existing grid snapping/range-display methods stay unchanged.

```csharp
    public const int ColumnCount = 15;
    public const int RowCount = 10;

    public static bool IsCellInBounds(Vector2Int cell)
    {
        return cell.x >= 0
            && cell.x < ColumnCount
            && cell.y >= 0
            && cell.y < RowCount;
    }

    public static bool HasGridLineOfSight(Vector2Int viewer, Vector2Int target)
    {
        int x = viewer.x;
        int y = viewer.y;
        int dx = Mathf.Abs(target.x - viewer.x);
        int dy = Mathf.Abs(target.y - viewer.y);
        int stepX = target.x >= viewer.x ? 1 : -1;
        int stepY = target.y >= viewer.y ? 1 : -1;
        int crossedX = 0;
        int crossedY = 0;

        while (crossedX < dx || crossedY < dy)
        {
            int horizontalDecision = (1 + 2 * crossedX) * dy;
            int verticalDecision = (1 + 2 * crossedY) * dx;

            if (horizontalDecision == verticalDecision)
            {
                // The ideal line crosses a corner. It is blocked only when both side cells
                // are walls; one open side permits diagonal sight around the corner.
                Vector2Int horizontalSide = new(x + stepX, y);
                Vector2Int verticalSide = new(x, y + stepY);
                if (
                    IsBlockingIntermediateCell(horizontalSide, viewer, target)
                    && IsBlockingIntermediateCell(verticalSide, viewer, target)
                )
                {
                    return false;
                }

                x += stepX;
                y += stepY;
                crossedX++;
                crossedY++;
            }
            else if (horizontalDecision < verticalDecision)
            {
                x += stepX;
                crossedX++;
            }
            else
            {
                y += stepY;
                crossedY++;
            }

            if (IsBlockingIntermediateCell(new Vector2Int(x, y), viewer, target))
                return false;
        }

        return true;
    }

    private static bool IsBlockingIntermediateCell(
        Vector2Int cell,
        Vector2Int viewer,
        Vector2Int target
    )
    {
        return cell != viewer
            && cell != target
            && GameLoop.wallLayout.Contains(cell);
    }

    public static HashSet<Vector2Int> ComputeVisibleCells(
        IEnumerable<(Vector2Int cell, int range)> viewers
    )
    {
        HashSet<Vector2Int> visible = new();
        if (viewers == null)
            return visible;

        foreach ((Vector2Int viewerCell, int configuredRange) in viewers)
        {
            int range = Mathf.Max(0, configuredRange);
            for (int rowOffset = -range; rowOffset <= range; rowOffset++)
            {
                int horizontalRange = range - Mathf.Abs(rowOffset);
                for (
                    int columnOffset = -horizontalRange;
                    columnOffset <= horizontalRange;
                    columnOffset++
                )
                {
                    Vector2Int target = new(
                        viewerCell.x + columnOffset,
                        viewerCell.y + rowOffset
                    );
                    if (!IsCellInBounds(target))
                        continue;
                    if (HasGridLineOfSight(viewerCell, target))
                        visible.Add(target);
                }
            }
        }

        return visible;
    }
```

Pure-function verification cases:

- viewer equals target: visible;
- a wall at the target endpoint: visible;
- one wall strictly between horizontal/vertical endpoints: blocked;
- exact diagonal corner with one adjacent wall: visible;
- exact diagonal corner with both adjacent walls: blocked;
- returned cells never leave x 0–14 or y 0–9.

### Step 5 — `Assets/Scripts/GameManager/GameLoop.cs`

#### 5.1 Exact fields

Add near the other serialized visual references/server round state:

```csharp
    // === FOG OF WAR ===
    [SerializeField]
    private GameObject fogOverlayCellPrefab;

    private const float FogUpdateIntervalSeconds = 0.15f;

    private readonly Dictionary<(GameObject unit, ulong clientId), double>
        forceRevealUntil = new();
    private readonly Dictionary<GameObject, Vector2Int> lastFogCells = new();
    private bool serverFogDirty = true;
    private Coroutine serverFogCoroutine;

    private readonly Dictionary<Vector2Int, GameObject> fogOverlayTiles = new();
    private GameObject fogOverlayRoot;
    private Coroutine clientFogCoroutine;
```

#### 5.2 Replace `OnNetworkSpawn`; add ordered startup and teardown

```csharp
    public override void OnNetworkSpawn()
    {
        Instance = this;
        TeamCamera = teamCameraParent.GetComponent<Camera>();

        if (IsClient)
            StartClientFog();

        if (IsServer)
        {
            StartGame();
            StartCoroutine(StartGameLoopAfterFogSetup());
            InitializeCameraPosition();
        }
    }

    private IEnumerator StartGameLoopAfterFogSetup()
    {
        // Allow unit spawns and one-time setup RPCs to be queued before the first hide pass.
        yield return null;
        StartServerFog();
        yield return StartCoroutine(GameLoopTemp());
    }

    public override void OnNetworkDespawn()
    {
        StopServerFog(false);
        StopClientFog();

        if (Instance == this)
            Instance = null;

        base.OnNetworkDespawn();
    }
```

`GameLoopTemp` itself is not changed; therefore dev planning/dodge waits, `currentPhase`, path
flattening, ability collection, and fast-forward behavior remain byte-for-byte intact.

#### 5.3 Add server visibility and force-reveal methods

```csharp
    private void StartServerFog()
    {
        if (!IsServer || serverFogCoroutine != null)
            return;

        serverFogDirty = true;
        RefreshFogCellCache();
        RemoveExpiredForceReveals();
        UpdateAllUnitVisibility();
        serverFogDirty = false;
        serverFogCoroutine = StartCoroutine(ServerFogLoop());
    }

    private IEnumerator ServerFogLoop()
    {
        while (IsServer && IsSpawned)
        {
            yield return new WaitForSeconds(FogUpdateIntervalSeconds);

            bool cellsChanged = RefreshFogCellCache();
            bool revealsExpired = RemoveExpiredForceReveals();
            if (!cellsChanged && !revealsExpired && !serverFogDirty)
                continue;

            UpdateAllUnitVisibility();
            serverFogDirty = false;
        }
    }

    private void StopServerFog(bool restoreVisibility)
    {
        if (serverFogCoroutine != null)
        {
            StopCoroutine(serverFogCoroutine);
            serverFogCoroutine = null;
        }

        if (restoreVisibility && IsServer)
            ShowAllLivingUnits();

        forceRevealUntil.Clear();
        lastFogCells.Clear();
        serverFogDirty = true;
    }

    private bool RefreshFogCellCache()
    {
        bool changed = false;
        HashSet<GameObject> livingUnits = new();

        foreach (GameObject[] teamUnits in allTeamUnitObjects.Values)
        {
            if (teamUnits == null)
                continue;

            foreach (GameObject unit in teamUnits)
            {
                if (!IsLivingUnit(unit))
                    continue;

                livingUnits.Add(unit);
                Vector2Int cell = GridSystem.ConvertToGridCoords(unit.transform.position);
                if (!lastFogCells.TryGetValue(unit, out Vector2Int previous) || previous != cell)
                {
                    lastFogCells[unit] = cell;
                    changed = true;
                }
            }
        }

        foreach (GameObject stale in lastFogCells.Keys.Where(unit => !livingUnits.Contains(unit)).ToList())
        {
            lastFogCells.Remove(stale);
            changed = true;
        }

        return changed;
    }

    private bool RemoveExpiredForceReveals()
    {
        if (NetworkManager.Singleton == null)
            return false;

        double now = NetworkManager.Singleton.ServerTime.Time;
        List<(GameObject unit, ulong clientId)> expired = forceRevealUntil
            .Where(entry => entry.Key.unit == null || entry.Value <= now)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var key in expired)
            forceRevealUntil.Remove(key);

        return expired.Count > 0;
    }

    private void UpdateAllUnitVisibility()
    {
        foreach (ulong viewerClientId in allTeamUnits.Keys.ToList())
            UpdateUnitVisibilityForClient(viewerClientId);
    }

    private void UpdateUnitVisibilityForClient(ulong viewerClientId)
    {
        if (
            NetworkManager.Singleton == null
            || !NetworkManager.Singleton.ConnectedClients.ContainsKey(viewerClientId)
        )
        {
            return;
        }

        HashSet<Vector2Int> visibleCells = ComputeServerVisibleCells(viewerClientId);

        foreach (var targetTeam in allTeamUnitObjects)
        {
            if (targetTeam.Key == viewerClientId || targetTeam.Value == null)
                continue;

            foreach (GameObject unit in targetTeam.Value)
            {
                if (unit == null)
                    continue;

                bool shouldSee =
                    IsLivingUnit(unit)
                    && (
                        visibleCells.Contains(
                            GridSystem.ConvertToGridCoords(unit.transform.position)
                        )
                        || IsForceRevealed(unit, viewerClientId)
                    );

                if (viewerClientId == NetworkManager.ServerClientId)
                {
                    SetUnitVisualsLocal(unit, shouldSee);
                    continue;
                }

                NetworkObject netObj = unit.GetComponent<NetworkObject>();
                if (netObj == null || !netObj.IsSpawned)
                    continue;

                bool currentlyVisible = netObj.IsNetworkVisibleTo(viewerClientId);
                if (shouldSee && !currentlyVisible)
                    netObj.NetworkShow(viewerClientId);
                else if (!shouldSee && currentlyVisible)
                    netObj.NetworkHide(viewerClientId);
            }
        }
    }

    private HashSet<Vector2Int> ComputeServerVisibleCells(ulong viewerClientId)
    {
        List<(Vector2Int cell, int range)> viewers = new();
        if (!allTeamUnitObjects.TryGetValue(viewerClientId, out GameObject[] units) || units == null)
            return new HashSet<Vector2Int>();

        foreach (GameObject unit in units)
        {
            if (!IsLivingUnit(unit))
                continue;

            Movement movement = unit.GetComponent<Movement>();
            if (movement?.unitData == null)
                continue;

            viewers.Add((
                GridSystem.ConvertToGridCoords(unit.transform.position),
                movement.unitData.visionRange
            ));
        }

        return GridSystem.ComputeVisibleCells(viewers);
    }

    private static bool IsLivingUnit(GameObject unit)
    {
        if (unit == null)
            return false;

        Health health = unit.GetComponent<Health>();
        return health != null ? health.IsAlive : unit.activeSelf;
    }

    private bool IsForceRevealed(GameObject unit, ulong clientId)
    {
        return NetworkManager.Singleton != null
            && forceRevealUntil.TryGetValue((unit, clientId), out double until)
            && NetworkManager.Singleton.ServerTime.Time < until;
    }

    public void ForceReveal(GameObject unit, ulong toClientId, float seconds)
    {
        if (!IsServer || unit == null || seconds <= 0f || NetworkManager.Singleton == null)
            return;

        double until = NetworkManager.Singleton.ServerTime.Time + seconds;
        var key = (unit, toClientId);
        if (!forceRevealUntil.TryGetValue(key, out double existing) || until > existing)
            forceRevealUntil[key] = until;

        serverFogDirty = true;

        // Show immediately so state changes/RPCs queued after this call are not sent to a hidden
        // object. The regular pass still re-evaluates everyone on the next 0.15 s tick.
        UpdateUnitVisibilityForClient(toClientId);
    }

    public void ForceRevealToEnemyTeams(GameObject unit, float seconds)
    {
        if (!IsServer || unit == null)
            return;

        Unit identity = unit.GetComponent<Unit>();
        if (identity == null)
            return;

        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            if (teamIndex != identity.TeamIndex)
                ForceRevealToTeam(unit, teamIndex, seconds);
        }
    }

    private void SetUnitVisualsLocal(GameObject unit, bool visible)
    {
        if (unit == null)
            return;

        // forceRenderingOff preserves intentionally disabled renderers and later Shield/laser
        // state changes; blindly assigning Renderer.enabled would not.
        foreach (Renderer renderer in unit.GetComponentsInChildren<Renderer>(true))
            renderer.forceRenderingOff = !visible;

        Canvas unitCanvas = unit.transform.Find("UnitCanvas")?.GetComponent<Canvas>();
        if (unitCanvas != null)
            unitCanvas.enabled = visible;
    }

    private void ShowAllLivingUnits()
    {
        foreach (ulong viewerClientId in allTeamUnits.Keys.ToList())
        {
            if (
                NetworkManager.Singleton == null
                || !NetworkManager.Singleton.ConnectedClients.ContainsKey(viewerClientId)
            )
            {
                continue;
            }

            foreach (var targetTeam in allTeamUnitObjects)
            {
                if (targetTeam.Key == viewerClientId || targetTeam.Value == null)
                    continue;

                foreach (GameObject unit in targetTeam.Value)
                {
                    if (!IsLivingUnit(unit))
                        continue;

                    if (viewerClientId == NetworkManager.ServerClientId)
                    {
                        SetUnitVisualsLocal(unit, true);
                        continue;
                    }

                    NetworkObject netObj = unit.GetComponent<NetworkObject>();
                    if (
                        netObj != null
                        && netObj.IsSpawned
                        && !netObj.IsNetworkVisibleTo(viewerClientId)
                    )
                    {
                        netObj.NetworkShow(viewerClientId);
                    }
                }
            }
        }
    }
```

The loop honors the spec’s “skip when no cell changed” optimization while also treating force
reveal addition/expiry and death as dirty state.

#### 5.4 Add client-local pooled overlay methods

```csharp
    private void StartClientFog()
    {
        if (!IsClient || clientFogCoroutine != null)
            return;
        if (fogOverlayCellPrefab == null)
        {
            Debug.LogError("[GameLoop] Fog overlay cell prefab is not assigned.");
            return;
        }

        fogOverlayRoot = new GameObject("FogOverlay");
        fogOverlayTiles.Clear();

        for (int row = 0; row < GridSystem.RowCount; row++)
        {
            for (int column = 0; column < GridSystem.ColumnCount; column++)
            {
                Vector2Int cell = new(column, row);
                GameObject tile = Instantiate(
                    fogOverlayCellPrefab,
                    gridCoordToWorld(cell) + Vector3.up * 0.05f,
                    Quaternion.identity,
                    fogOverlayRoot.transform
                );
                tile.name = $"FogCell_{column}_{row}";
                tile.SetActive(true);
                fogOverlayTiles[cell] = tile;
            }
        }

        clientFogCoroutine = StartCoroutine(ClientFogLoop());
    }

    private IEnumerator ClientFogLoop()
    {
        while (IsClient && IsSpawned)
        {
            UpdateClientFogOverlay();
            yield return new WaitForSeconds(FogUpdateIntervalSeconds);
        }
    }

    private void UpdateClientFogOverlay()
    {
        if (NetworkManager.Singleton?.SpawnManager == null)
            return;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        List<(Vector2Int cell, int range)> viewers = new();

        foreach (NetworkObject netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (
                netObj == null
                || netObj.OwnerClientId != localClientId
                || netObj.GetComponent<Unit>() == null
            )
            {
                continue;
            }

            GameObject unit = netObj.gameObject;
            Health health = unit.GetComponent<Health>();
            Movement movement = unit.GetComponent<Movement>();
            if (
                !unit.activeInHierarchy
                || (health != null && !health.IsAlive)
                || movement?.unitData == null
            )
            {
                continue;
            }

            viewers.Add((
                GridSystem.ConvertToGridCoords(unit.transform.position),
                movement.unitData.visionRange
            ));
        }

        HashSet<Vector2Int> visibleCells = GridSystem.ComputeVisibleCells(viewers);
        foreach (var tile in fogOverlayTiles)
        {
            if (tile.Value != null)
                tile.Value.SetActive(!visibleCells.Contains(tile.Key));
        }
    }

    private void StopClientFog()
    {
        if (clientFogCoroutine != null)
        {
            StopCoroutine(clientFogCoroutine);
            clientFogCoroutine = null;
        }

        if (fogOverlayRoot != null)
        {
            fogOverlayRoot.SetActive(false);
            Destroy(fogOverlayRoot);
            fogOverlayRoot = null;
        }

        fogOverlayTiles.Clear();
    }

    [ClientRpc]
    private void StopFogClientRpc()
    {
        StopClientFog();
    }
```

No enemy object or server visibility set is used by this overlay. The only inputs are local-owned
units, their prefab UnitData, and static wall geometry.

#### 5.5 Replace `EndGame`

```csharp
    void EndGame()
    {
        Time.timeScale = 1f;
        StopServerFog(true);
        StopFogClientRpc();

        ulong? winner = getWinner();
        bool hasWinner = winner.HasValue;
        ulong winnerValue = winner.GetValueOrDefault();
        EndGameClientRpc(hasWinner, winnerValue);
    }
```

This restores all living models before the end-game UI and removes the local overlay. Dead roots
remain inactive.

### Step 6 — `Assets/Scripts/Units/Shooting.cs`

#### 6.1 Replace `Start`, `OnNetworkSpawn`, `OnNetworkDespawn`, and laser handlers

Delete `Start` entirely and use:

```csharp
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        EnsureTargetLaser();

        isLaserEnabled.OnValueChanged += OnLaserEnabledChanged;
        laserStartPos.OnValueChanged += OnLaserPositionChanged;
        laserEndPos.OnValueChanged += OnLaserPositionChanged;
        laserWidth.OnValueChanged += OnLaserWidthChanged;
        laserColor.OnValueChanged += OnLaserColorChanged;
        ApplyCurrentLaserState();

        if (!IsServer)
        {
            enabled = false;
            return;
        }

        GameLoop.OrderAllowShooting += SetAllowShooting;
        GameLoop.OrderStillShooting += SetStillShooting;
        GameLoop.OrderContinueShooting += ContinueShooting;
        enemyTeam = GameLoop.GetEnemyTeam(transform.tag);
    }

    public override void OnNetworkDespawn()
    {
        isLaserEnabled.OnValueChanged -= OnLaserEnabledChanged;
        laserStartPos.OnValueChanged -= OnLaserPositionChanged;
        laserEndPos.OnValueChanged -= OnLaserPositionChanged;
        laserWidth.OnValueChanged -= OnLaserWidthChanged;
        laserColor.OnValueChanged -= OnLaserColorChanged;

        if (IsServer)
        {
            GameLoop.OrderAllowShooting -= SetAllowShooting;
            GameLoop.OrderStillShooting -= SetStillShooting;
            GameLoop.OrderContinueShooting -= ContinueShooting;
        }

        base.OnNetworkDespawn();
    }

    private void EnsureTargetLaser()
    {
        if (targetLaser != null)
            return;

        targetLaser = GetComponent<LineRenderer>();
        if (targetLaser == null)
            targetLaser = gameObject.AddComponent<LineRenderer>();
        targetLaser.enabled = false;
    }

    private void ApplyCurrentLaserState()
    {
        EnsureTargetLaser();
        targetLaser.SetPositions(new[] { laserStartPos.Value, laserEndPos.Value });
        targetLaser.startWidth = targetLaser.endWidth = laserWidth.Value;
        targetLaser.startColor = targetLaser.endColor = laserColor.Value;
        targetLaser.enabled = isLaserEnabled.Value;
    }

    private void SetAllowShooting(bool toggle)
    {
        allowShooting = toggle;
    }

    private void SetStillShooting(bool toggle)
    {
        stillShooting = toggle;
    }

    private void OnLaserEnabledChanged(bool previousValue, bool newValue)
    {
        EnsureTargetLaser();
        targetLaser.enabled = newValue;
    }

    private void OnLaserPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        EnsureTargetLaser();
        targetLaser.SetPositions(new[] { laserStartPos.Value, laserEndPos.Value });
    }

    private void OnLaserWidthChanged(float previousValue, float newValue)
    {
        EnsureTargetLaser();
        targetLaser.startWidth = targetLaser.endWidth = newValue;
    }

    private void OnLaserColorChanged(Color previousValue, Color newValue)
    {
        EnsureTargetLaser();
        targetLaser.startColor = targetLaser.endColor = newValue;
    }
```

Named action handlers also fix the current inability to unsubscribe anonymous lambdas.

#### 6.2 Add the target-lock force-reveal helper

```csharp
    private void ForceRevealForTargetLock(GameObject target)
    {
        if (!IsServer || target == null || unitData.targetLockDuration <= 0f)
            return;

        NetworkObject targetNetObj = target.GetComponent<NetworkObject>();
        if (targetNetObj == null)
            return;

        GameLoop.Instance?.ForceReveal(
            gameObject,
            targetNetObj.OwnerClientId,
            unitData.targetLockDuration + 0.5f
        );
    }
```

#### 6.3 Replace `InitiateShooting`

The only gameplay change in this full body is calling the helper immediately after rotation and
before each new lock countdown.

```csharp
    public IEnumerator InitiateShooting()
    {
        if (GetComponent<AnimationHandler>() != null)
            GetComponent<AnimationHandler>().PlayAnimation("Aiming");

        currentAmmo = unitData.magazineSize;
        float remainingTargetLockTime = unitData.targetLockDuration;

        while (allowShooting)
        {
            GameObject target = FindNearestEnemy();
            if (target)
            {
                yield return StartCoroutine(RotateToFaceTarget(target));
                ForceRevealForTargetLock(target);
            }

            remainingTargetLockTime = unitData.targetLockDuration;

            while (currentAmmo > 0)
            {
                if (target == null || !lineOfSight(target))
                {
                    isLaserEnabled.Value = false;
                    target = FindNearestEnemy();
                    if (target)
                    {
                        remainingTargetLockTime = unitData.targetLockDuration;
                        yield return StartCoroutine(RotateToFaceTarget(target));
                        ForceRevealForTargetLock(target);
                    }
                    if (!allowShooting)
                        break;

                    yield return null;
                    continue;
                }

                if (remainingTargetLockTime > 0f)
                {
                    transform.rotation = Quaternion.LookRotation(
                        (target.transform.position - transform.position).normalized
                    );

                    float lockProgress =
                        1 - remainingTargetLockTime / unitData.targetLockDuration;

                    isLaserEnabled.Value = true;
                    laserWidth.Value = Mathf.Lerp(startAnimWidth, endAnimWidth, lockProgress);
                    laserColor.Value = Color.Lerp(startAnimColor, endAnimColor, lockProgress);
                    laserStartPos.Value = transform.position;
                    laserEndPos.Value = target.transform.position;

                    remainingTargetLockTime -= Time.deltaTime;
                    yield return null;
                    continue;
                }

                isLaserEnabled.Value = false;
                transform.rotation = Quaternion.LookRotation(
                    (target.transform.position - transform.position).normalized
                );
                FireBullet();
                yield return new WaitForSeconds(unitData.timeBetweenShots);
            }

            if (allowShooting)
                yield return StartCoroutine(Reload());
        }

        if (GetComponent<AnimationHandler>() != null)
            GetComponent<AnimationHandler>().PlayAnimation("Idle");

        while (GetActiveBulletCount() > 0)
            yield return null;

        yield return new WaitForSeconds(0.1f);
        stillShooting = false;
    }
```

All target acquisition remains server-side and unchanged. Fog does not become an additional
shooting predicate because all `visionRange >= targetRange`.

### Step 7 — `Assets/Scripts/Abilities/Shield.cs`

Replace the file with:

```csharp
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Shield : Ability
{
    private float abilityTime = 3f;
    private Transform shieldTransform;

    private NetworkVariable<bool> shieldActive = new(false);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        shieldTransform = transform.Find("Shield");
        shieldActive.OnValueChanged += OnShieldActiveChanged;
        ApplyShieldState(shieldActive.Value);
    }

    public override void OnNetworkDespawn()
    {
        shieldActive.OnValueChanged -= OnShieldActiveChanged;
        base.OnNetworkDespawn();
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        shieldActive.Value = true;
        yield return new WaitForSeconds(abilityTime);
        shieldActive.Value = false;
    }

    private void OnShieldActiveChanged(bool previousValue, bool newValue)
    {
        ApplyShieldState(newValue);
    }

    private void ApplyShieldState(bool active)
    {
        if (shieldTransform == null)
            shieldTransform = transform.Find("Shield");
        if (shieldTransform != null)
            shieldTransform.gameObject.SetActive(active);
    }
}
```

`base.OnNetworkSpawn()` still disables non-server ability execution, but the remainder of this
override subscribes/applies replicated visual state on every peer.

### Step 8 — `Assets/Scripts/Abilities/AreaLock.cs`

Replace only `ExecuteAbility` with this full body:

```csharp
    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        // Cover the pre-laser delay, rotation, full active window, rush, and cleanup RPC.
        // This happens before every object-scoped AreaLock ClientRpc.
        GameLoop.Instance?.ForceRevealToEnemyTeams(
            gameObject,
            delayForDodge + abilityTime + 1.5f
        );

        transform.GetComponent<Movement>().PauseMovement();
        transform.GetComponent<Movement>().moving = false;
        transform.GetComponent<Shooting>().PauseShooting();

        yield return new WaitForSeconds(delayForDodge);

        Vector3 startPosition = transform.position =
            GridSystem.GetNearestGridCell(transform.position) + Helper.heightOffset(transform);
        Vector3 targetPosition = abilitySquare + Helper.heightOffset(transform);
        CreateLaserLine(startPosition, targetPosition);
        ShowLaserClientRpc(startPosition, targetPosition);

        yield return StartCoroutine(
            transform.GetComponent<Movement>().RotateToFaceTarget(targetPosition, rotationSpeed)
        );

        float elapsed = 0f;
        while (elapsed < abilityTime && !CheckForCrossingTarget(startPosition, targetPosition))
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (elapsed >= abilityTime)
            StartCoroutine(cleanup());
    }
```

Keep `damageMultiplier = 2f`; with Sniper damage 110, Area Lock becomes 220.

### Step 9 — grenade damage serialization

#### `Assets/Scripts/Abilities/Grenade.cs`

Change:

```csharp
    public float damage = 50f;
```

to:

```csharp
    [SerializeField]
    private float damage = 80f;
```

#### `Assets/Prefabs/Units/Soldier.prefab`

On the `Grenade` component, change the serialized value:

```yaml
  damage: 50
```

to:

```yaml
  damage: 80
```

Do not change the stale orphaned prefab override paths for old Shooting fields; runtime shooting
reads `UnitData`, and those legacy overrides are outside this change.

### Step 10 — create fog visual assets

Create through Unity asset/prefab APIs so Unity generates stable paired `.meta` files:

1. `Assets/Materials/Visuals/FogOverlayCell.mat`
   - URP-compatible transparent material;
   - base color approximately `(0.02, 0.03, 0.05, 0.45)`;
   - transparent surface/alpha blend, ZWrite off;
   - no emission;
   - no shadow casting.
2. `Assets/Prefabs/Visuals/FogOverlayCell.prefab`
   - one flat quad or very thin cube centered at local origin;
   - x/z footprint approximately `2.65 × 2.65` world units (slightly inside a 2.7 cell);
   - renderer uses `FogOverlayCell.mat`;
   - **no Collider anywhere in the prefab**;
   - root and children on `Ignore Raycast`, not `Grid`;
   - cast/receive shadows off;
   - GameLoop places instances at y = 0.05.

The no-collider/Ignore-Raycasts requirement is essential: a clone of `MoveOverlayCell.prefab`
without cleanup contains a MeshCollider on the Grid layer and would intercept planning and blind
ability clicks.

### Step 11 — `Assets/Scenes/Game.unity`

On the GameManager’s `GameLoop` component:

- current: no serialized `fogOverlayCellPrefab` property;
- new: assign `Assets/Prefabs/Visuals/FogOverlayCell.prefab`.

Make this assignment through the Unity editor/MCP and save the scene. Do not hand-author a GUID.
No hierarchy object is added; the 150-cell `FogOverlay` hierarchy is client-local at runtime.

### Step 12 — documentation after successful verification

Update `docs/GAME_DESIGN.md` only after compile and runtime verification:

- remove the two remaining claims that the ability pipeline is disabled;
- change the five weapon damages to 12/30/18/110/25 in roster order;
- document vision ranges 6/7/3/8/5 in roster order;
- describe network visibility, host visual-only special case, fog-piercing telegraphs, Shield
  NetworkVariable, and Area Lock/target-lock force reveal;
- update the known-issues entry for Health;
- record the grenade value 80 and Area Lock result 220.

The approved design spec can remain as design history because this implementation plan explicitly
records every reconciliation.

### 2.13 New-file/tool handling

- **No new C# file is planned.** Do not use `unityMCP.create_script`.
- Existing C# files should be changed with script text-edit tooling after confirming their hashes.
- New `.mat`/`.prefab` files and their `.meta` partners must be created by Unity asset/material/
  prefab tooling, not plain filesystem writes.
- The Game scene reference must be assigned and saved through Unity.
- This Phase 1 markdown document is the only plain-write new file.

### 2.14 Explicitly audited, no Phase 2 edit planned

- `DevInput.cs`: server arrays and phase behavior remain valid under fog.
- `PlanMovement.cs`: local plans remain private; own-unit discovery remains valid.
- `PathSelection.cs`: wall/path rules already match the server. Fog prefab must not intercept Grid
  raycasts.
- `Movement.cs`: server movement and NetworkTransform remain authoritative.
- `Bullet.cs`: bullets stay globally visible and use rebalance values supplied by Shooting.
- `NetworkHelper.cs`: scene-object setup/alert RPCs remain; no death call remains.
- `Unit.cs`: `OnNetworkSpawn` restores owner-relative indicator materials.
- `BaseAbility.cs`, `Pogo.cs`: no fog-specific state change needed.
- `ActivateAbility.cs`, `Reroute.cs`: historical/commented; do not revive.
- `AllUnits.asset`, all `.meta` GUIDs, `ProjectSettings/TimeManager.asset`, and the temporary MPPM
  join script: no change.

## 3. Host-vs-client visibility matrix

Assume team 0 is the host/server player and team 1 is the remote/MPPM player.

| Situation | Host player’s screen | Remote player’s screen | Server/transport state |
|---|---|---|---|
| Own living unit | Fully visible, health/UI visible | Same for remote-owned units | Always spawned; contributes vision. |
| Enemy in team vision | `forceRenderingOff=false`, UnitCanvas enabled | Enemy NetworkObject spawned/shown with current NetworkVariables | Transform/state replicated. |
| Enemy outside team vision | All child renderers forced off; UnitCanvas Canvas disabled. Root remains active for simulation. | Enemy NetworkObject is `NetworkHide`n and absent from the remote spawn table | Host inherently retains full authoritative data; remote receives no transform/NetworkVariable stream. |
| Enemy crosses into vision | Appears on next ≤0.15 s pass | `NetworkShow`; object spawns with current health/alive/shield/laser state | Current NetworkTransform and NetworkVariables synchronize. |
| Enemy leaves vision | Disappears on next ≤0.15 s pass, no linger | `NetworkHide`; client object despawns | No last-known marker in this phase. |
| Hidden enemy fires normally | Model/muzzle remains hidden | Model remains absent | Bullet is a separate visible NetworkObject, so tracer leaks direction by design. |
| Hidden enemy dies | Model remains hidden and server root deactivates after state sync opportunity | No object appears; owner card disable still arrives to the dead unit’s owner | `currentHealth=0`, `isAlive=false` persist for any later state query/show attempt. |
| Hidden Shield activates | Hidden model stays hidden | Hidden enemy remains absent | `shieldActive=true` persists. If the unit reveals before expiry, the shield is correctly active. |
| Hidden Pogo jumps | Host model stays suppressed until its current cell is visible | Remote receives no transform while hidden; a mid-jump reveal shows current synchronized position | No forced reveal on landing. |
| Sniper begins weapon lock on one of viewer’s units | If the host is the victim, the enemy sniper is visually forced on and its laser appears | If the remote is the victim, `NetworkShow` occurs before laser changes replicate | Reveal lasts lock duration + 0.5 s and is scoped to the target’s client. |
| Match ends | All living enemy renderers restored; overlay removed | All living hidden enemies are shown; overlay removed | Force-reveal table and fog caches clear. |

### 3.1 Enemy ability telegraph from inside fog

The current pipeline has two distinct visuals:

1. **Dodge-phase telegraph on GameLoop (always delivered):**
   - line ability: orange caster-position-to-wall line;
   - non-line ability: orange marker/disc at the selected square;
   - remains until round cleanup.
2. **Ability implementation visuals during execution:**
   - Grenade/explosion are independent network objects;
   - Area Lock’s red line/rush are unit-scoped RPC visuals protected by force reveal;
   - Shield state is a NetworkVariable;
   - Pogo is the caster’s NetworkTransform.

Exact per-client result:

| Ability from hidden enemy | Defender sees during dodge | Attacker sees | Caster model reveal |
|---|---|---|---|
| Grenade | Target AoE marker, dodge overlay/alert on threatened own units, countdown; caster may remain hidden. Later grenade projectile/explosion is globally visible. | Same target marker; attacker does not need defender Alert icons. | No forced model reveal. |
| Area Lock | Full orange line even though the sniper object may initially be hidden; the line itself discloses the source coordinate. Threatened own units show Alerts and can dive. | Same line. | At `ExecuteAbility` start, force-revealed to enemy clients before the red line/rush RPCs. |
| Pogo | Landing marker is visible even though responseRange is 0 and no dodge window opens. | Same marker. | No forced reveal; normal cell vision controls the rider throughout the jump and landing. |
| Shield | Small self-cell marker is visible; if a nearby defender is threatened, its own Alert/dodge UI is visible. | Same marker. | No forced reveal. If revealed naturally, NetworkVariable state gives the correct shield visual. |

The threatened defender’s `StartDodgePlanningClientRpc` always resolves its own units. A hidden
unit reference can fail only on the opposing client, where that Alert icon is neither needed nor
intended to reveal information.

## 4. Phase 2 compile and verification plan

### 4.1 Safe apply/compile sequence

1. Confirm the other E2E editor driver is finished and both Unity instances are idle.
2. Locate and read the completed rerunnable E2E artifact; record its actual expected checkpoints.
3. Apply Steps 1–4, refresh/compile, and resolve all errors before using new fields.
4. Apply GameLoop, Shooting, Shield, Area Lock, and Grenade changes; compile again.
5. Change UnitData assets and Soldier prefab serialized damage.
6. Create/wire fog material and prefab; save the Game scene.
7. Check the Unity console for errors after every script/domain reload.
8. Run pure grid-math assertions via `execute_code`.
9. Run the existing E2E artifact unchanged once.
10. Run the fog/replication/damage assertions below against both instances.
11. Update `GAME_DESIGN.md` only after results pass.

### 4.2 Existing E2E areas most likely to regress

The final script was not yet in the tree during Phase 1, so map these categories to its concrete
steps when resumed:

| Existing step category | Why it can regress |
|---|---|
| Match startup and first planning phase | Initial NetworkHide must occur after spawn/setup but before planning. Overlay construction must not intercept input or delay GameLoop. |
| Dev-mode indefinite planning and explicit `SubmitPlans` | New coroutines must not alter `currentPhase`, timers, `Time.timeScale`, or server path arrays. |
| Normal move execution and round transition | Visibility checks run during NetworkTransform movement; hiding must not deactivate server roots or stop Movement/Shooting. |
| Ability plan sanitization | New vision fields must not change move/ability tuple serialization or wall/range checks. |
| Dodge telegraph and alert phase | A hidden caster must not prevent GameLoop telegraphs; defender-owned unit refs must resolve; Shield replication must not affect dive submission. |
| Ability cancellation by dodge | Visibility work must not mutate `PathsDict`, `diveUnitsThisRound`, or `runningAbilities`. |
| Shooting/target lock | Moving setup from `Start` to `OnNetworkSpawn` must preserve server event subscriptions, ammo flow, and laser animation. |
| Damage, death, card disable, and win detection | Health now updates asynchronously over NetworkVariables and server root deactivation is delayed one frame. Verify card disable and `teamSize` still converge before the next loop decision. |
| Expected post-round survivors/health | Rebalanced weapons can kill units much earlier, potentially ending a match before later scripted checkpoints. Expected outcomes may need intentional updates, not code rollback. |
| Screenshots/object counts | TESTING spawns should remain mutually visible, but fog overlay changes terrain shading; remote object counts change only in explicit fog layouts. |

### 4.3 Grid-math assertions

Run on the main editor with no Play-mode dependency where possible:

- `ComputeVisibleCells` returns exactly cells inside the 15×10 board.
- A viewer at `(7,4)` with range 0 sees `(7,4)` only.
- Manhattan distance `range + 1` is absent.
- A known wall strictly between viewer/target blocks.
- Endpoint wall remains visible.
- Construct a corner case where one adjacent cell is in `wallLayout`: visible.
- Construct/temporarily pass a wall set only if code is generalized; otherwise use existing pairs
  where both corner-adjacent cells are walls and assert blocked.

### 4.4 Two-instance fog setup

Because `TESTING=true` starts all units mutually visible, use server-side `execute_code` on the main
editor to place the three blue units near rows 0–1 and the three red units near rows 8–9, using
`GameLoop.gridCoordToWorld(cell) + Helper.heightOffset(unit.transform)`. Wait at least one
0.15-second fog tick and one network frame.

Do not change `TESTING`, `devMode`, the spawn list, or assets just to create this test.

### 4.5 Host-view assertions (main editor)

For each enemy outside host team vision:

- root remains `activeSelf=true` while alive;
- all child `Renderer.forceRenderingOff == true`;
- `UnitCanvas.GetComponent<Canvas>().enabled == false`;
- Movement/Shooting components remain present and server simulation continues;
- `DevInput.Dump()` still lists both teams and authoritative grid cells.

Move a friendly viewer across a wall/vision edge:

- enemy becomes visible within one fog tick;
- all `forceRenderingOff` flags clear;
- UnitCanvas enables;
- moving back hides immediately with no linger.

### 4.6 Remote/clone assertions

Select the second Unity MCP instance explicitly:

`mppm3282aa39@e67a57eae6d5b1f5`

Then execute observations in that instance, not the main editor:

- local-owned unit count remains 3;
- hidden host-owned enemy NetworkObjects are absent from
  `NetworkManager.Singleton.SpawnManager.SpawnedObjectsList`;
- after server movement reveals one enemy, exactly that unit appears with current transform and
  UnitData;
- after it leaves vision, it disappears from the spawn table again;
- `FogOverlay` has exactly 150 pooled children;
- `FogCell_x_y.activeSelf` is false for locally visible cells and true otherwise.

On a non-host client, “hidden renderers off” is not the primary assertion because the stronger
network guarantee is that the enemy object and its renderers do not exist locally at all.

### 4.7 Health/death hide-show assertions

1. Hide a 100-HP enemy from the clone.
2. On the server call `TakeDamage(25f)`.
3. Move a clone-owned viewer so the enemy cell becomes visible.
4. On the clone assert:
   - object appears;
   - `Health.CurrentHealth == 75`;
   - health fill x-scale is 0.75;
   - `Health.IsAlive == true`.
5. Hide a second enemy, kill it on the server, then bring the cell into vision.
6. Assert it never spawns as a zombie, its owner card is disabled, and the server reports
   `activeSelf=false`.

Also run the same health test against the 200-HP Shotgunner: after 25 damage, health fill ratio is
0.875 while the max-health bar’s parent scale remains 2.

### 4.8 Ability/dodge fog assertions

1. **GameLoop telegraph safety:** queue an ability with `DevInput.SetAbility`, submit plans, and
   pause in `phase=dodging` where applicable. Verify the orange marker/line exists on both
   instances even if the caster object is absent from the defender’s spawn table.
2. **Alerts:** on the defender instance, every threatened own unit has `UnitCanvas/Alert` active;
   non-threatened units do not. The attacker need not resolve or display hidden defender alerts.
3. **Dodge submission:** use `DevInput.SetDodgePath` and `SubmitDodge`; verify the sanitized dive
   replaces the move and cancels that unit’s ability exactly as before fog.
4. **Area Lock:** place the red Sniper outside host vision, queue Area Lock, and verify:
   - host sees the orange line during dodge;
   - on execution, host renderer suppression clears before the red Area Lock line appears;
   - `ShowLaser`, rush, and cleanup all appear/clear with no dropped-RPC warning;
   - reveal expires and the sniper hides again if still outside normal vision.
5. **Target lock:** place the red Sniper outside host vision with clear weapon LoS to a host unit.
   At lock start, assert the host sees the sniper and charging laser for the full lock; after grace
   expiry, normal fog resumes.
6. **Shield:** activate Shield while the Shotgunner is hidden, reveal it before 3 seconds elapse,
   and assert the shield child is active; hide/reveal after expiry and assert it is inactive.
7. **Pogo:** observe on the opposing instance that the landing telegraph remains visible while the
   hidden rider object does not; reveal is based only on its current airborne/landing cell.
8. **Grenade:** verify marker, projectile, explosion, and camera shake are visible through fog;
   caster remains governed by normal vision.

### 4.9 Damage assertions

First assert serialized runtime values:

| Source | Expected runtime damage |
|---|---:|
| Commander bullet | 12 |
| Pogo bullet | 30 |
| Pogo backstab | 60 |
| Shotgun pellet | 18 |
| Sniper bullet | 110 |
| Soldier bullet | 25 |
| Grenade | 80 |
| Area Lock | 220 |

Use deterministic server-side `Health.TakeDamage` calls with the attacker’s runtime UnitData value
to establish hit counts, then retain the existing E2E combat run to cover Bullet collision:

- Soldier vs 100 HP: alive at 25 HP after 3 hits; dead after hit 4.
- Soldier vs 200 HP: dead after hit 8.
- Shotgunner vs 100 HP: alive at 10 HP after 5 pellets; dead after pellet 6.
- Sniper vs 100 HP: dead after 1 hit; Shotgunner dead after 2.
- Pogo frontal vs 100 HP: dead after 4; backstab value 60 kills in 2.
- Commander vs 100 HP: alive at 4 HP after 8; dead after 9.
- Grenade vs 100 HP: 20 HP remains after one explosion.
- Area Lock vs full 200-HP Shotgunner: dead in one crossing.

After direct assertions, restart the match before running the reusable E2E so test damage does not
pollute its state.

### 4.10 Non-regression/console assertions

- No compilation errors or missing serialized-field warnings.
- No null `targetLaser` callback failures during initial spawn or re-show.
- No `NetworkShow`/`NetworkHide` calls for owner units.
- No RPC invoked on a hidden Area Lock object.
- No fog prefab collider hit by `Mouse.GetGridCellUnderMouse`.
- `DevInput.Dump()` phase progression remains planning → dodging when needed → executing → planning.
- `devMode` returns to its prior value and `Time.timeScale` to 1 after tests.
- End-game re-shows living units, clears fog tiles, and does not resurrect dead units.

## 5. Damage table to apply

| Unit/source | Current | Phase 2 value | Result |
|---|---:|---:|---|
| Commander pistol | 5 | **12** | 9 hits per standard unit |
| Pogo rifle | 15 | **30** | 4 frontal / 2 backstab hits per standard unit |
| Shotgun pellet | 10 | **18** | 6 pellets per standard unit |
| Sniper rifle | 70 | **110** | 1 standard, 2 Shotgunner |
| Soldier rifle | 10 | **25** | 4 hits per standard unit |
| Grenade | 50 | **80** | Leaves a full standard unit at 20 |
| Area Lock multiplier | 2× | **2× unchanged** | 110 × 2 = **220** |

## 6. Risks and recommendations

### Highest Phase 2 risks

1. **NGO hide/show lifecycle and ordering.** `NetworkShow` must precede laser/Area Lock state, and
   NetworkVariables must be explicitly applied in `OnNetworkSpawn`. Recommendation: test the clone
   spawn table and state restoration before any balance playtest.
2. **Serialized/runtime divergence.** `Soldier.prefab` currently pins grenade damage to 50, and a
   fog prefab cloned from the move overlay would contain a Grid-layer collider. Recommendation:
   inspect the actual runtime Grenade field and every fog prefab component after asset creation.
3. **Lethality changes E2E flow.** Sniper 110 and Soldier 25 can kill units before the existing
   script reaches later checkpoints. Recommendation: distinguish a correct new combat outcome from
   an infrastructure regression; update expected outcomes only after a deliberate balance check.

### Additional risks and explicit recommendations

4. **Host security is impossible by design.** Host visual suppression is presentation only; host
   memory contains every position. Accept for client-hosted architecture.
5. **Renderer-state preservation differs from the spec wording.** `forceRenderingOff`/Canvas
   enablement is recommended over `Renderer.enabled`/root deactivation because variants, Shield,
   and lasers own their states. Verify API behavior in Unity 6000 before proceeding; fallback is a
   per-renderer saved-state map, not “enable every renderer.”
6. **Area Lock duration was underspecified.** `abilityTime + 1` does not clearly cover its 0.5 s
   delay, rotation, late crossing, rush, and cleanup RPC. The plan uses 5 seconds. Tune down only
   after proving all object RPCs complete sooner.
7. **One-time client layer state is not replicated state.** A re-shown prefab may use its authored
   layer. It is harmless to current thin-client logic but could break future client-side physics.
   Recommendation: later add networked team identity and reapply tag/layer in `Unit.OnNetworkSpawn`
   if clients begin relying on team layers.
8. **Death deactivation timing.** The one-frame server delay is intended to allow the final
   NetworkVariable state to flush while preserving inactive-root `teamSize`. Verify win detection
   under simultaneous kills and dev fast-forward.
9. **Transparent overlay draw order.** A y=0.05 transparent tile can z-fight or sort over units
   depending on material settings. Recommendation: verify both cameras and adjust y/render queue,
   never by adding a collider.
10. **TESTING layout masks fog.** Adjacent spawns are expected to show everyone. A “fog did
    nothing” result in the baseline E2E is not evidence of failure; use the explicit far-layout
    test.
11. **Sniper one-shot remains the balance risk identified by the approved spec.** Ship 110 first;
    retain 90 as the first tuning fallback after playtest, not as an implementation-time change.
12. **Pogo + fog/backstab synergy may dominate.** Preserve 30 × 2 initially and collect results;
    1.75 backstab is the fallback lever.
13. **Fog overlay reveals local vision computation but no enemy data.** This is intended. Ensure
    the client list filters strictly by local ownership and never consults server team arrays.
14. **Dictionary insertion order remains pre-existing architecture.** Team/client mapping and
    winner logic still rely on current two-client insertion order. Fog uses dictionary keys rather
    than introducing another index dependency, but the underlying risk remains.
15. **Weapon LoS is not mathematically guaranteed to be a subset of grid LoS.** The spec makes
    that claim from `visionRange >= targetRange`, but physics wall colliders and permissive-corner
    supercover can disagree at wall edges. Recommendation: add edge/corner parity checks in Phase
    2; if a real mismatch lets a unit target something its team cannot see, gate target acquisition
    on the shooter team’s server-visible set rather than changing approved vision ranges.
16. **The remote overlay can lag server visibility slightly.** NetworkTransform interpolation means
    the client computes tiles from a marginally older friendly position while the server uses the
    authoritative position for `NetworkShow`. Recommendation: accept a sub-tick cosmetic mismatch
    for this local-overlay design, but verify it does not exceed one 0.15-second fog interval.

No recommendation above silently changes approved damage, vision, no-linger, always-visible
telegraph, or bullet-visibility behavior.
