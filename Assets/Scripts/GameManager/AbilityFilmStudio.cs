#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Dev-only capture rig that films abilities from a live match so their look can be judged frame by
/// frame instead of by description.
/// <para>
/// A shoot runs the real <see cref="Ability.ExecuteAbility"/> coroutine on a real spawned unit in
/// the real Game scene, so what lands on disk is the shipping code path with the shipping shaders,
/// lighting and post stack. Determinism comes from <see cref="Time.captureDeltaTime"/>: every
/// rendered frame advances game time by exactly one step regardless of how long the readback took,
/// so the same revision always produces the same frame at the same timestamp and two revisions can
/// be diffed frame for frame.
/// </para>
/// <para>
/// It films two kinds of subject. A <see cref="Shot"/> is a whole ability. A
/// <see cref="PrimitiveShot"/> is one juice primitive fired on an empty stage, so a shockwave or a
/// debris burst can be improved and judged without the rest of an ability arguing on its behalf.
/// </para>
/// </summary>
public static class AbilityFilmStudio
{
    public const int FrameSize = 1024;

    // 60 Hz simulation with every second frame written: fine enough that the impact frame is never
    // straddled, coarse enough that an iteration's contact sheet stays a manageable size.
    const float CaptureStep = 1f / 60f;
    const int WriteStride = 2;
    const int JpegQuality = 92;

    // The board camera looks down at 73 degrees. Shots reuse that pitch so a strip is honest about
    // what a player actually sees, and only pull the distance in to frame the effect.
    const float StudioPitchDegrees = 73f;

    // Somewhere off the board, where parked units cannot wander into frame.
    static readonly Vector3 OffstagePosition = new(-400f, 0f, -400f);

    public static string Status = "idle";
    public static bool Running;
    public static bool Finished;
    public static string LastError = "";
    public static string LastRunDir = "";
    public static int FramesWritten;
    public static readonly List<string> Log = new();

    sealed class Shot
    {
        public string Name;
        public string AbilityType;
        public Vector2Int CasterCell;
        public Vector2Int TargetCell;
        public Vector2Int[] EnemyCells = Array.Empty<Vector2Int>();
        public float PreRollSeconds = 0.5f;
        public float CaptureSeconds = 2.6f;
        public float CameraDistance = 15f;
        public Vector2 LookAtCell;
        public float RadiusOverride = -1f;

        /// <summary>Deploys the smoke screen on landing, which the round loop normally owns.</summary>
        public bool SmokeFootprint;

        // Optional walker: an enemy dragged from one cell to another mid-shoot, for abilities that
        // only pay off when something crosses them.
        public Vector2Int CrossFrom;
        public Vector2Int CrossTo;
        public float CrossStartSeconds = -1f;
        public float CrossSeconds = 0.8f;
    }

    /// <summary>
    /// A single juice primitive fired on an otherwise empty stage, so it can be judged on its own.
    /// </summary>
    sealed class PrimitiveShot
    {
        public string Name;
        public Vector2Int Origin;
        public Vector2Int[] Bystanders = Array.Empty<Vector2Int>();
        public float PreRollSeconds = 0.4f;
        public float CaptureSeconds = 1.6f;
        public float CameraDistance = 11f;

        /// <summary>Fires the effect, given the stage origin, the team colour and the staged cast.</summary>
        public Action<Vector3, Color, GameObject[]> Fire;
    }

    static readonly Shot[] Shots =
    {
        new()
        {
            Name = "grenade",
            AbilityType = "Grenade",
            CasterCell = new Vector2Int(5, 5),
            TargetCell = new Vector2Int(9, 5),
            EnemyCells = new[] { new Vector2Int(9, 5), new Vector2Int(10, 6), new Vector2Int(8, 4) },
            LookAtCell = new Vector2(8.4f, 5f),
            CameraDistance = 13f,
            CaptureSeconds = 2.8f,
        },
        new()
        {
            Name = "pogo",
            AbilityType = "Pogo",
            CasterCell = new Vector2Int(5, 5),
            TargetCell = new Vector2Int(9, 5),
            EnemyCells = new[] { new Vector2Int(10, 5), new Vector2Int(9, 6) },
            LookAtCell = new Vector2(7.6f, 5f),
            CameraDistance = 15f,
            CaptureSeconds = 2.8f,
        },
        new()
        {
            Name = "smoke",
            AbilityType = "Smoke",
            CasterCell = new Vector2Int(6, 5),
            TargetCell = new Vector2Int(9, 5),
            EnemyCells = new[] { new Vector2Int(11, 5) },
            LookAtCell = new Vector2(8.2f, 5f),
            CameraDistance = 14f,
            CaptureSeconds = 3.0f,
            SmokeFootprint = true,
        },
        new()
        {
            Name = "shieldrush",
            AbilityType = "Shield",
            CasterCell = new Vector2Int(6, 5),
            TargetCell = new Vector2Int(7, 5),
            EnemyCells = new[] { new Vector2Int(10, 5), new Vector2Int(10, 6) },
            LookAtCell = new Vector2(8.2f, 5f),
            CameraDistance = 16f,
            CaptureSeconds = 3.6f,
        },
        new()
        {
            Name = "arealock",
            AbilityType = "AreaLock",
            CasterCell = new Vector2Int(4, 5),
            TargetCell = new Vector2Int(12, 5),
            EnemyCells = new[] { new Vector2Int(9, 8) },
            LookAtCell = new Vector2(8.2f, 5.2f),
            CameraDistance = 21f,
            CaptureSeconds = 4.4f,
            CrossFrom = new Vector2Int(9, 8),
            CrossTo = new Vector2Int(9, 3),
            CrossStartSeconds = 1.3f,
            CrossSeconds = 1.0f,
        },
    };

    static readonly PrimitiveShot[] Primitives =
    {
        new()
        {
            Name = "windup",
            Origin = new Vector2Int(8, 5),
            // The caster sits two cells out, not three: at three it lands exactly on the frame edge
            // and its performance cannot be judged at all.
            Bystanders = new[] { new Vector2Int(6, 5), new Vector2Int(8, 5) },
            CaptureSeconds = 1.9f,
            CameraDistance = 15f,
            Fire = (origin, color, cast) =>
                AbilityWindup.Play(
                    cast.Length > 0 && cast[0] != null ? cast[0].transform : null,
                    origin,
                    color,
                    1.1f,
                    WindupShape.Disc,
                    4.3f
                ),
        },
        new()
        {
            Name = "impactcore",
            Origin = new Vector2Int(8, 5),
            CaptureSeconds = 1.1f,
            CameraDistance = 11f,
            Fire = (origin, color, _) => ImpactCore.Spawn(origin, color, 2.6f, 1f),
        },
        new()
        {
            // Filmed beside the Pogo landing, because the two currently look almost the same and a
            // player cannot tell a unit dying from a rider arriving.
            Name = "death",
            Origin = new Vector2Int(8, 5),
            Bystanders = new[] { new Vector2Int(8, 5), new Vector2Int(6, 6) },
            CaptureSeconds = 2.0f,
            CameraDistance = 11f,
            Fire = (_, _, cast) =>
            {
                if (cast.Length > 0 && cast[0] != null)
                    cast[0].GetComponent<Health>()?.TakeDamage(9999f);
            },
        },
        new()
        {
            Name = "shockwave",
            Origin = new Vector2Int(8, 5),
            CaptureSeconds = 1.4f,
            CameraDistance = 13f,
            Fire = (origin, color, _) => ImpactShockwave.Spawn(origin, color, 4.3f, 0.55f),
        },
        new()
        {
            Name = "debris",
            Origin = new Vector2Int(8, 5),
            CaptureSeconds = 1.9f,
            CameraDistance = 12f,
            Fire = (origin, color, _) => DebrisBurst.Spawn(origin, color, 3.5f, 22),
        },
        new()
        {
            Name = "hitreact",
            Origin = new Vector2Int(8, 5),
            Bystanders = new[] { new Vector2Int(8, 5) },
            CaptureSeconds = 1.6f,
            CameraDistance = 9f,
            Fire = (origin, _, cast) =>
            {
                if (cast.Length > 0 && cast[0] != null)
                    HitReaction.Play(cast[0], origin + new Vector3(-4f, 0f, 0f), 0.7f);
            },
        },
        new()
        {
            Name = "camera",
            Origin = new Vector2Int(8, 5),
            Bystanders = new[] { new Vector2Int(7, 5), new Vector2Int(9, 6) },
            CaptureSeconds = 1.3f,
            CameraDistance = 13f,
            // Debris rides along because a hitstop is unfalsifiable on a static board: with nothing
            // moving, a frozen frame is indistinguishable from a still one.
            Fire = (origin, color, _) =>
            {
                ImpactCamera.Hitstop();
                ImpactCamera.Punch(origin, 1.4f);
                DebrisBurst.Spawn(origin, color, 3.5f, 22);
            },
        },
        new()
        {
            Name = "aftermath",
            Origin = new Vector2Int(8, 5),
            CaptureSeconds = 2.6f,
            CameraDistance = 12f,
            Fire = (origin, color, _) => Aftermath.Spawn(origin, color, 3f, AftermathKind.Scorch),
        },
        new()
        {
            Name = "numbers",
            Origin = new Vector2Int(8, 5),
            Bystanders = new[] { new Vector2Int(8, 5) },
            CaptureSeconds = 1.7f,
            CameraDistance = 9f,
            Fire = (origin, _, cast) =>
            {
                Vector3 above =
                    (cast.Length > 0 && cast[0] != null ? cast[0].transform.position : origin)
                    + Vector3.up * 2.2f;
                DamagePopup.Spawn(above, 80f, DamageTone.Heavy);
            },
        },
    };

    /// <summary>
    /// Films every ability into <c>Captures/AbilityJuice/shots/&lt;tag&gt;/</c>. Call from Play mode
    /// once a match is live, then poll <see cref="Finished"/>.
    /// </summary>
    public static void CaptureAll(string tag) => Begin(tag, null, null);

    /// <summary>Films a subset of abilities, by name.</summary>
    public static void Capture(string tag, params string[] shotNames) => Begin(tag, shotNames, null);

    /// <summary>Films the isolated juice primitives, by name; all of them when none are given.</summary>
    public static void CapturePrimitives(string tag, params string[] names) =>
        Begin(tag, Array.Empty<string>(), names != null && names.Length > 0 ? names : PrimitiveNames());

    /// <summary>Films everything: primitives first, then the abilities they compose into.</summary>
    public static void CaptureEverything(string tag) => Begin(tag, null, PrimitiveNames());

    public static string[] ShotNames()
    {
        string[] names = new string[Shots.Length];
        for (int i = 0; i < Shots.Length; i++)
            names[i] = Shots[i].Name;
        return names;
    }

    public static string[] PrimitiveNames()
    {
        string[] names = new string[Primitives.Length];
        for (int i = 0; i < Primitives.Length; i++)
            names[i] = Primitives[i].Name;
        return names;
    }

    static void Begin(string tag, string[] filter, string[] primitiveFilter)
    {
        if (Running)
        {
            LastError = "already running";
            return;
        }
        if (!Application.isPlaying)
        {
            LastError = "not in play mode";
            return;
        }
        if (GameLoop.Instance == null)
        {
            LastError = "no live GameLoop; start a match first";
            return;
        }

        Running = true;
        Finished = false;
        LastError = "";
        FramesWritten = 0;
        Log.Clear();

        GameObject runnerObject = new("AbilityFilmStudioRunner")
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        runnerObject
            .AddComponent<Runner>()
            .StartCoroutine(Run(tag, filter, primitiveFilter, runnerObject));
    }

    sealed class Runner : MonoBehaviour { }

    static IEnumerator Run(
        string tag,
        string[] filter,
        string[] primitiveFilter,
        GameObject runnerObject
    )
    {
        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        string runDir = Path.Combine(projectRoot, "Captures", "AbilityJuice", "shots", tag);
        LastRunDir = runDir;

        float savedTimeScale = Time.timeScale;
        float savedCaptureDelta = Time.captureDeltaTime;
        UniversalRenderPipelineAsset pipeline =
            UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        float savedRenderScale = pipeline != null ? pipeline.renderScale : 1f;

        Camera boardCamera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : Camera.main;
        RenderTexture target = null;
        RenderTexture resolve = null;
        Camera studioCamera = null;
        Texture2D readback = null;
        Dictionary<Transform, Vector3> parked = new();

        try
        {
            // A dev match runs at 6x. Capture has to run at 1x or captureDeltaTime lies about how
            // long an effect took. devMode itself stays on: it is what keeps the round parked in
            // planning instead of resolving out from under the shoot.
            Time.timeScale = 1f;
            SetFogEnabled(false);

            // The quality tier renders the board at 2x and resolves down. Left on, that scaling is
            // applied again on the way into a camera target texture and the frame lands in a corner.
            if (pipeline != null)
                pipeline.renderScale = 1f;

            // URP copies the camera target's descriptor for its own intermediate colour buffer, so
            // an 8-bit target silently clamps the whole pre-tonemap pipeline at 1.0. Filming into
            // one meant nothing could ever clip to white and Bloom, whose threshold is 1.8, could
            // never fire in a single captured frame. The camera renders into a float target and the
            // result is resolved down only for the PNG readback.
            target = new RenderTexture(FrameSize, FrameSize, 24, RenderTextureFormat.DefaultHDR)
            {
                antiAliasing = 1,
                name = "AbilityFilmTargetHDR",
            };
            target.Create();
            resolve = new RenderTexture(FrameSize, FrameSize, 0, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                name = "AbilityFilmResolve",
            };
            resolve.Create();
            resolveTarget = resolve;
            readback = new Texture2D(FrameSize, FrameSize, TextureFormat.RGB24, mipChain: false);
            studioCamera = BuildStudioCamera(boardCamera, target);

            Directory.CreateDirectory(runDir);

            if (primitiveFilter != null)
            {
                foreach (PrimitiveShot primitive in Primitives)
                {
                    if (Array.IndexOf(primitiveFilter, primitive.Name) < 0)
                        continue;

                    Status = $"staging {primitive.Name}";
                    yield return Drain(
                        RunPrimitive(primitive, runDir, studioCamera, target, readback, parked),
                        primitive.Name
                    );
                }
            }

            foreach (Shot shot in Shots)
            {
                if (filter != null && Array.IndexOf(filter, shot.Name) < 0)
                    continue;

                Status = $"staging {shot.Name}";
                yield return Drain(
                    RunShot(shot, runDir, studioCamera, target, readback, parked),
                    shot.Name
                );
            }

            Status = "done";
        }
        finally
        {
            Time.captureDeltaTime = savedCaptureDelta;
            Time.timeScale = savedTimeScale;
            if (pipeline != null)
                pipeline.renderScale = savedRenderScale;
            RestoreParked(parked);
            SetFogEnabled(true);

            if (studioCamera != null)
                UnityEngine.Object.Destroy(studioCamera.gameObject);
            resolveTarget = null;
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
            if (resolve != null)
            {
                resolve.Release();
                UnityEngine.Object.Destroy(resolve);
            }
            if (readback != null)
                UnityEngine.Object.Destroy(readback);
            UnityEngine.Object.Destroy(runnerObject);

            Running = false;
            Finished = true;
        }
    }

    /// <summary>Pumps a shoot to completion, turning a throw inside it into a logged failure.</summary>
    static IEnumerator Drain(IEnumerator routine, string label)
    {
        while (true)
        {
            bool moved;
            try
            {
                moved = routine.MoveNext();
            }
            catch (Exception exception)
            {
                Log.Add($"{label}: FAILED {exception.Message}");
                yield break;
            }
            if (!moved)
                yield break;
            yield return routine.Current;
        }
    }

    static IEnumerator RunShot(
        Shot shot,
        string runDir,
        Camera studioCamera,
        RenderTexture target,
        Texture2D readback,
        Dictionary<Transform, Vector3> parked
    )
    {
        GameObject caster = FindUnitWithAbility(shot.AbilityType);
        if (caster == null)
        {
            Log.Add($"{shot.Name}: no unit carries {shot.AbilityType}");
            yield break;
        }

        Ability ability = caster.GetComponent<Ability>();
        int casterTeam = caster.GetComponent<Unit>()?.TeamIndex ?? GameLoop.HostTeamIndex;
        int enemyTeam =
            casterTeam == GameLoop.HostTeamIndex ? GameLoop.OpponentTeamIndex : GameLoop.HostTeamIndex;

        // Everything not in the shot goes offstage, so the frame shows the ability and its victims
        // and nothing else.
        ParkAllUnits(parked, except: caster);

        PlaceUnit(caster.transform, shot.CasterCell, parked);
        List<GameObject> victims = new();
        GameObject[] enemyPool = GameLoop.GetTeamUnits(enemyTeam);
        for (int i = 0; i < shot.EnemyCells.Length && i < enemyPool.Length; i++)
        {
            GameObject enemy = enemyPool[i];
            if (enemy == null)
                continue;
            ReviveIfDead(enemy, shot.EnemyCells[i]);
            if (!enemy.activeSelf)
                continue;
            PlaceUnit(enemy.transform, shot.EnemyCells[i], parked);
            FaceTowards(enemy.transform, GameLoop.gridCoordToWorld(shot.CasterCell));
            victims.Add(enemy);
        }

        Vector3 targetWorld = GameLoop.gridCoordToWorld(shot.TargetCell);
        FaceTowards(caster.transform, targetWorld);
        AimStudioCamera(studioCamera, CellToWorld(shot.LookAtCell), shot.CameraDistance);
        Physics.SyncTransforms();

        yield return null;
        yield return null;

        float radius =
            shot.RadiusOverride >= 0f
                ? shot.RadiusOverride
                : caster.GetComponent<Movement>()?.unitData?.abilityRadius ?? 1.5f;
        Color teamColor = GameLoop.GetTeamColorForViewer(casterTeam);

        Transform walker = null;
        Vector3 walkFrom = Vector3.zero;
        Vector3 walkTo = Vector3.zero;
        if (shot.CrossStartSeconds >= 0f && victims.Count > 0)
        {
            walker = victims[0].transform;
            walkFrom = WorldForCell(shot.CrossFrom, walker.position.y);
            walkTo = WorldForCell(shot.CrossTo, walker.position.y);
            walker.position = walkFrom;
        }

        Coroutine abilityRoutine = null;
        Coroutine smokeRoutine = null;

        void Fire()
        {
            // Mirrors GameLoop.RunAbility, which is what a real activation looks like on every peer.
            HitFlash.FlashTarget(caster, 0.4f, 1.5f);
            ImpactShockwave.Spawn(caster.transform.position, teamColor, 1.8f, 0.45f);
            abilityRoutine = ability.StartCoroutine(ability.RunAbility(targetWorld, radius));
            if (shot.SmokeFootprint)
                smokeRoutine = ability.StartCoroutine(DeploySmokeScreen(shot.TargetCell));
        }

        void Tick(float sinceFire)
        {
            if (walker == null)
                return;
            float since = sinceFire - shot.CrossStartSeconds;
            if (since < 0f)
                return;
            float progress = Mathf.Clamp01(since / Mathf.Max(0.01f, shot.CrossSeconds));
            walker.position = Vector3.Lerp(walkFrom, walkTo, progress);
            if (progress < 1f)
                FaceTowards(walker, walkTo);
        }

        yield return CaptureSequence(
            Path.Combine(runDir, shot.Name),
            shot.PreRollSeconds,
            shot.CaptureSeconds,
            target,
            readback,
            Fire,
            Tick
        );

        if (abilityRoutine != null)
            ability.StopCoroutine(abilityRoutine);
        if (smokeRoutine != null)
            ability.StopCoroutine(smokeRoutine);
        ClearSmokeScreen();

        Log.Add($"{shot.Name}: filmed {shot.CaptureSeconds:0.00}s");
        Status = $"captured {shot.Name}";

        // Undo anything the ability left on the caster before the next shoot stages it.
        ability.ResetForRespawn();
        yield return null;
    }

    static IEnumerator RunPrimitive(
        PrimitiveShot primitive,
        string runDir,
        Camera studioCamera,
        RenderTexture target,
        Texture2D readback,
        Dictionary<Transform, Vector3> parked
    )
    {
        ParkAllUnits(parked, except: null);

        Vector3 origin = GameLoop.gridCoordToWorld(primitive.Origin);
        List<GameObject> cast = new();
        GameObject[] pool = GameLoop.GetTeamUnits(GameLoop.OpponentTeamIndex);
        for (int i = 0; i < primitive.Bystanders.Length && i < pool.Length; i++)
        {
            GameObject unit = pool[i];
            if (unit == null)
                continue;
            ReviveIfDead(unit, primitive.Bystanders[i]);
            if (!unit.activeSelf)
                continue;
            PlaceUnit(unit.transform, primitive.Bystanders[i], parked);
            FaceTowards(unit.transform, origin + Vector3.right * 4f);
            cast.Add(unit);
        }

        AimStudioCamera(studioCamera, origin, primitive.CameraDistance);
        Physics.SyncTransforms();
        yield return null;
        yield return null;

        Color teamColor = GameLoop.GetTeamColorForViewer(GameLoop.HostTeamIndex);
        GameObject[] castArray = cast.ToArray();

        yield return CaptureSequence(
            Path.Combine(runDir, primitive.Name),
            primitive.PreRollSeconds,
            primitive.CaptureSeconds,
            target,
            readback,
            () => primitive.Fire?.Invoke(origin, teamColor, castArray),
            null
        );

        Log.Add($"{primitive.Name}: filmed {primitive.CaptureSeconds:0.00}s");
        Status = $"captured {primitive.Name}";
        yield return null;
    }

    /// <summary>
    /// Rolls the deterministic clock, fires the subject after the pre-roll, and writes every second
    /// frame to <paramref name="shotDir"/> alongside a timestamp manifest.
    /// </summary>
    static IEnumerator CaptureSequence(
        string shotDir,
        float preRollSeconds,
        float captureSeconds,
        RenderTexture target,
        Texture2D readback,
        Action onFire,
        Action<float> onTick
    )
    {
        Directory.CreateDirectory(shotDir);
        foreach (string stale in Directory.GetFiles(shotDir, "*.jpg"))
            File.Delete(stale);

        StringBuilder manifest = new();
        manifest.AppendLine("frame,time_seconds,phase");

        Time.captureDeltaTime = CaptureStep;

        int frameIndex = 0;
        int written = 0;
        float clock = 0f;
        bool fired = false;
        float total = preRollSeconds + captureSeconds;

        while (clock <= total)
        {
            if (!fired && clock >= preRollSeconds)
            {
                fired = true;
                // An effect that throws must cost its own shot and nothing else. Left unguarded, the
                // exception kills this coroutine mid-sequence, so the caller never resumes, the
                // capture clock is never restored, and the whole shoot hangs on one bad primitive.
                try
                {
                    onFire?.Invoke();
                }
                catch (Exception fireException)
                {
                    Log.Add($"{Path.GetFileName(shotDir)}: FIRE THREW {fireException.Message}");
                }
            }
            if (fired && onTick != null)
            {
                try
                {
                    onTick(clock - preRollSeconds);
                }
                catch (Exception tickException)
                {
                    Log.Add($"{Path.GetFileName(shotDir)}: TICK THREW {tickException.Message}");
                    onTick = null;
                }
            }

            yield return new WaitForEndOfFrame();

            if (frameIndex % WriteStride == 0)
            {
                float shotTime = clock - preRollSeconds;
                WriteFrame(target, readback, Path.Combine(shotDir, $"f{written:D4}.jpg"));
                manifest.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1:0.0000},{2}",
                        written,
                        shotTime,
                        shotTime < 0f ? "preroll" : "live"
                    )
                );
                written++;
                FramesWritten++;
            }

            frameIndex++;
            clock += CaptureStep;
        }

        Time.captureDeltaTime = 0f;
        File.WriteAllText(Path.Combine(shotDir, "frames.csv"), manifest.ToString());
    }

    /// <summary>
    /// Stands up the smoke screen the round loop would normally deploy, so a smoke shoot shows the
    /// cloud rather than only the canister that asked for it.
    /// </summary>
    static IEnumerator DeploySmokeScreen(Vector2Int center)
    {
        yield return new WaitForSeconds(Smoke.ThrowSeconds);

        List<Vector3> cells = new();
        for (int dx = -Smoke.FootprintRadius; dx <= Smoke.FootprintRadius; dx++)
        {
            for (int dy = -Smoke.FootprintRadius; dy <= Smoke.FootprintRadius; dy++)
                cells.Add(GameLoop.gridCoordToWorld(center + new Vector2Int(dx, dy)));
        }

        ClearSmokeScreen();
        smokeScreen = SmokeScreenVisual.Create(null, cells, GameLoop.cellSize)?.gameObject;
    }

    static GameObject smokeScreen;

    static void ClearSmokeScreen()
    {
        if (smokeScreen != null)
            UnityEngine.Object.Destroy(smokeScreen);
        smokeScreen = null;
    }

    /// <summary>Resolve surface for the float camera target; see the note where it is created.</summary>
    static RenderTexture resolveTarget;

    static void WriteFrame(RenderTexture target, Texture2D readback, string path)
    {
        RenderTexture readSource = target;
        if (resolveTarget != null)
        {
            Graphics.Blit(target, resolveTarget);
            readSource = resolveTarget;
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = readSource;
        readback.ReadPixels(new Rect(0, 0, FrameSize, FrameSize), 0, 0, recalculateMipMaps: false);
        readback.Apply(updateMipmaps: false);
        RenderTexture.active = previous;
        File.WriteAllBytes(path, readback.EncodeToJPG(JpegQuality));
    }

    static Camera BuildStudioCamera(Camera source, RenderTexture target)
    {
        GameObject cameraObject = new("AbilityFilmCamera") { hideFlags = HideFlags.HideAndDontSave };
        Camera studio = cameraObject.AddComponent<Camera>();

        // Settings are copied field by field rather than with CopyFrom, which also carries over the
        // board camera's locked aspect ratio and renders the square target into one corner.
        if (source != null)
        {
            studio.clearFlags = source.clearFlags;
            studio.backgroundColor = source.backgroundColor;
            studio.cullingMask = source.cullingMask;
            studio.nearClipPlane = source.nearClipPlane;
            studio.farClipPlane = source.farClipPlane;
            studio.allowMSAA = source.allowMSAA;
            studio.depth = source.depth + 10f;

            // Riding the board camera is what lets a strip show the camera's own reaction: a shake
            // applied to the parent moves the shot exactly as a player would see it.
            cameraObject.transform.SetParent(source.transform, worldPositionStays: false);
        }
        studio.orthographic = false;
        studio.fieldOfView = 60f;
        studio.allowHDR = true;
        studio.rect = new Rect(0f, 0f, 1f, 1f);
        studio.targetTexture = target;
        studio.ResetAspect();

        UniversalAdditionalCameraData studioData =
            cameraObject.GetComponent<UniversalAdditionalCameraData>()
            ?? cameraObject.AddComponent<UniversalAdditionalCameraData>();
        studioData.renderType = CameraRenderType.Base;
        studioData.renderPostProcessing = true;
        studioData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        studioData.antialiasingQuality = AntialiasingQuality.High;
        studioData.renderShadows = true;

        UniversalAdditionalCameraData sourceData =
            source != null ? source.GetComponent<UniversalAdditionalCameraData>() : null;
        if (sourceData != null)
            studioData.volumeLayerMask = sourceData.volumeLayerMask;

        return studio;
    }

    static void AimStudioCamera(Camera studio, Vector3 lookAt, float distance)
    {
        Quaternion rotation = Quaternion.Euler(StudioPitchDegrees, 0f, 0f);
        studio.transform.SetPositionAndRotation(
            lookAt - rotation * Vector3.forward * distance,
            rotation
        );
    }

    static Vector3 CellToWorld(Vector2 cell)
    {
        return new Vector3(
            GameLoop.gridBounds.xMin + cell.x * GameLoop.cellSize,
            0f,
            GameLoop.gridBounds.yMin + cell.y * GameLoop.cellSize
        );
    }

    static Vector3 WorldForCell(Vector2Int cell, float height)
    {
        Vector3 world = GameLoop.gridCoordToWorld(cell);
        world.y = height;
        return world;
    }

    static void PlaceUnit(Transform unit, Vector2Int cell, Dictionary<Transform, Vector3> parked)
    {
        if (!parked.ContainsKey(unit))
            parked[unit] = unit.position;
        unit.position = WorldForCell(cell, unit.position.y);
    }

    static void FaceTowards(Transform unit, Vector3 worldTarget)
    {
        Vector3 flat = worldTarget - unit.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.0001f)
            unit.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
    }

    static void ParkAllUnits(Dictionary<Transform, Vector3> parked, GameObject except)
    {
        for (int team = 0; team <= 1; team++)
        {
            foreach (GameObject unit in GameLoop.GetTeamUnits(team))
            {
                if (unit == null || unit == except || !unit.activeSelf)
                    continue;
                Transform unitTransform = unit.transform;
                if (!parked.ContainsKey(unitTransform))
                    parked[unitTransform] = unitTransform.position;
                unitTransform.position = OffstagePosition;
            }
        }
    }

    static void RestoreParked(Dictionary<Transform, Vector3> parked)
    {
        foreach (KeyValuePair<Transform, Vector3> entry in parked)
        {
            if (entry.Key != null)
                entry.Key.position = entry.Value;
        }
        parked.Clear();
    }

    static void ReviveIfDead(GameObject unit, Vector2Int cell)
    {
        Health health = unit.GetComponent<Health>();
        if (health == null || health.IsAlive)
            return;
        health.RespawnAt(WorldForCell(cell, 0f), unit.transform.rotation);
    }

    static GameObject FindUnitWithAbility(string abilityTypeName)
    {
        for (int team = 0; team <= 1; team++)
        {
            foreach (GameObject unit in GameLoop.GetTeamUnits(team))
            {
                if (unit == null)
                    continue;
                Ability ability = unit.GetComponent<Ability>();
                if (ability != null && ability.GetType().Name == abilityTypeName)
                    return unit;
            }
        }
        return null;
    }

    static void SetFogEnabled(bool enabled)
    {
        try
        {
            DevInput.SetFog(enabled);
        }
        catch (Exception)
        {
            // Fog control is a convenience; a shoot is still valid without it.
        }
    }
}
#endif
