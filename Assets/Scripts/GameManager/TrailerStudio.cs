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
/// Dev-only capture rig that films the trailer's shot list as 1080p60 frame sequences.
/// <para>
/// It is a sibling of <see cref="AbilityFilmStudio"/> and borrows its determinism: every beat runs
/// on <see cref="Time.captureDeltaTime"/>, so one rendered frame advances game time by exactly one
/// step no matter how long the readback took, and a re-shoot of the same revision lands the same
/// event on the same frame. That is what makes an edit built against these frames stable.
/// </para>
/// <para>
/// Two things differ from the ability rig, both because this output is motion rather than contact
/// sheets. Frames are 16:9 and every frame is written, so the sequence plays at 60fps. And the
/// camera is animated across a beat instead of parked: the board camera is fixed at 73 degrees by
/// design, but a trailer needs pushes, descents and yaw, so each beat interpolates its own pose.
/// </para>
/// <para>
/// Beats stage real units and run shipping code — <see cref="Ability.ExecuteAbility"/>,
/// <see cref="Movement.MoveToCells"/>, <see cref="Shooting.StartShooting"/> — so kills, deaths and
/// effects are the ones a player would see, not a mockup built for the camera.
/// </para>
/// </summary>
public static class TrailerStudio
{
    /// <summary>
    /// Capture size. Raise it to film a shot loose and still punch in hard in the edit at native
    /// resolution — the only way to frame a live round tightly, since where the fighting will be
    /// is not known until after the match has played.
    /// </summary>
    public static int FrameWidth = 1920;
    public static int FrameHeight = 1080;

    // Every frame is written at 60Hz: this is footage, not a contact sheet.
    const float CaptureStep = 1f / 60f;
    const int JpegQuality = 95;

    /// <summary>The board camera's pitch. Beats break from it deliberately, never by accident.</summary>
    const float BoardPitch = 73f;

    // Beats aim at a cell, but a unit is about 1.7 units tall, so aiming at the ground plane hangs
    // the subject below centre. The aim point rides high enough to sit its mass in the frame.
    const float AimHeight = 1.1f;

    // Far enough off the board that a parked unit cannot wander into a wide shot.
    static readonly Vector3 OffstagePosition = new(-400f, 0f, -400f);

    public static string Status = "idle";
    public static bool Running;
    public static bool Finished;
    public static string LastError = "";
    public static string LastRunDir = "";
    public static int FramesWritten;
    public static readonly List<string> Log = new();

    enum Ease
    {
        Linear,
        In,
        Out,
        InOut,
    }

    /// <summary>One unit placed on the stage for a beat.</summary>
    sealed class Actor
    {
        public int Team;
        public string Unit;
        public Vector2Int Cell;

        /// <summary>Cell this actor turns to face while staging.</summary>
        public Vector2 Face = new(-1f, -1f);

        /// <summary>Opens fire at this offset once the beat is live; negative never fires.</summary>
        public float ShootAt = -1f;

        /// <summary>
        /// Fraction of max HP this unit starts on. Full crews cannot trade a kill inside a five
        /// second shot, so the unit that is meant to die starts the beat already hurt — which is
        /// also what losing actually looks like. The killing blow is still a real bullet.
        /// </summary>
        public float StartHealth = 1f;

        /// <summary>
        /// Walks this route, with the shipping move animation, at <see cref="WalkAt"/>.
        /// <para>
        /// Any unit that has to fight wants one, even a single cell: <see cref="Movement"/> calls
        /// <see cref="Shooting.StartShooting"/> when a walk finishes, which is how a round arms a
        /// unit that has just arrived. A unit dropped straight onto its cell never gets that call
        /// and stands through the whole beat holding its weapon. Walking it in also looks better
        /// than a tableau that was always there.
        /// </para>
        /// </summary>
        public Vector2Int[] Walk = Array.Empty<Vector2Int>();
        public float WalkAt = -1f;

        /// <summary>
        /// Keeps this unit's weapon cold for the whole beat. Walking a unit in arms it, so a
        /// victim staged that way will fight back and can win — which is how the Ramrod died
        /// in his own hero shot. Victims that only need to be hit hold fire.
        /// </summary>
        public bool HoldsFire;

        /// <summary>
        /// Animation state to hold, if any. A unit that is never told to move or shoot is left in
        /// its bind pose, which is invisible from the board camera and unmissable in a close-up.
        /// </summary>
        public string Animation;

        /// <summary>Draws a plan ribbon over this route, one cell at a time, from <see cref="RouteAt"/>.</summary>
        public Vector2Int[] Route = Array.Empty<Vector2Int>();
        public float RouteAt = -1f;
        public float RouteSeconds = 1.2f;

        /// <summary>
        /// Cell this unit has aimed its ability at, drawn with the planning board's own telegraph
        /// from <see cref="AbilityPlanAt"/>. A plan is either a route or an ability, never both, so
        /// this and <see cref="Route"/> are alternatives. Negative plans nothing.
        /// </summary>
        public Vector2Int AbilityPlan = new(-1, -1);
        public float AbilityPlanAt = -1f;
    }

    sealed class Beat
    {
        public string Name;
        public float PreRoll = 0.4f;
        public float Seconds = 4f;
        public Actor[] Cast = Array.Empty<Actor>();

        public Vector2 FromLook = new(7f, 4.5f);
        public Vector2 ToLook = new(7f, 4.5f);
        public float FromDistance = 18f;
        public float ToDistance = 18f;
        public float FromPitch = BoardPitch;
        public float ToPitch = BoardPitch;
        public float FromYaw;
        public float ToYaw;
        public Ease Move = Ease.InOut;

        public bool Fog;

        /// <summary>
        /// Films the match as it actually plays instead of staging anything. Nothing is parked,
        /// nobody is placed, no health is set: both crews start whole and the round loop decides
        /// what happens. The rig only drives the two seats and points the camera.
        /// </summary>
        public bool LiveMatch;

        /// <summary>Height the camera aims at; overrides <see cref="AimHeight"/> for close work.</summary>
        public float Aim = AimHeight;

        /// <summary>Index into <see cref="Cast"/> of the unit that casts, or -1 for no ability.</summary>
        public int Caster = -1;
        public Vector2Int AbilityTarget;
        public float AbilityAt = -1f;

        /// <summary>
        /// Takes every plan ribbon off the board at this offset, the way the round loop does when
        /// execution starts. An execution shot that opens on the plan it is about to run is the
        /// only framing that says outright that these are the same routes.
        /// </summary>
        public float RibbonsClearAt = -1f;

        /// <summary>Deploys the smoke footprint the round loop would normally own on landing.</summary>
        public bool SmokeFootprint;
    }

    static Actor At(int team, string unit, int col, int row, float startHealth = 1f) =>
        new()
        {
            Team = team,
            Unit = unit,
            Cell = new Vector2Int(col, row),
            StartHealth = startHealth,
        };

    const int Friendly = 0;
    const int Enemy = 1;

    /// <summary>
    /// A member of the losing line: already hurt, walked in so its weapon is live, facing the
    /// advance. It fires back — a crew that stands still while it is shot reads as broken rather
    /// than beaten — but at this health it cannot win.
    /// </summary>
    static Actor Loser(string unit, int row, float health, float firesAt) =>
        new()
        {
            Team = Friendly,
            Unit = unit,
            Cell = new Vector2Int(3, row),
            Walk = new[] { new Vector2Int(4, row) },
            WalkAt = 0.05f,
            Face = new Vector2(9f, row),
            StartHealth = health,
            // Opens up just before the man opposite does, so he dies mid-burst. Left free to fire
            // from the start, a crew this size wins the back half of the fight even at five per
            // cent health — the first take of this shot ended with three of the enemy dead.
            HoldsFire = true,
            ShootAt = firesAt,
        };

    /// <summary>
    /// The man opposite. He walks in like everyone else, which is what arms a unit, and then holds
    /// fire until his turn — otherwise all five open up at once and the line falls in a second.
    /// </summary>
    static Actor Killer(string unit, int col, int row, float shootAt) =>
        new()
        {
            Team = Enemy,
            Unit = unit,
            Cell = new Vector2Int(8, row),
            Walk = new[] { new Vector2Int(col, row) },
            WalkAt = 0.05f,
            Face = new Vector2(0f, row),
            HoldsFire = true,
            ShootAt = shootAt,
        };

    /// <summary>
    /// A close, orbiting look at one unit standing alone. The unit faces the camera's opening
    /// position: the rig places the camera on the low-row side of whatever it is aimed at, so
    /// looking towards row zero is looking down the lens.
    /// </summary>
    static Beat Portrait(
        string name,
        string unit,
        int col,
        int row,
        float fromDistance,
        float toDistance,
        float fromYaw,
        float toYaw
    ) =>
        new()
        {
            Name = name,
            Seconds = 4.5f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = unit,
                    Cell = new Vector2Int(col, row),
                    Face = new Vector2(col, -4f),
                    Animation = "Idle",
                },
            },
            FromLook = new Vector2(col, row),
            ToLook = new Vector2(col, row),
            // Head and shoulders, not a full body. Three of the five units have no animation
            // controller in the project at all, so they stand in their bind pose; framed wide that
            // is a T-pose in a close-up, and framed here it is out of shot. It is also simply the
            // better portrait — these faces are the funniest thing the game owns.
            Aim = 2.45f,
            FromDistance = fromDistance,
            ToDistance = toDistance,
            FromPitch = 40f,
            ToPitch = 35f,
            FromYaw = fromYaw,
            ToYaw = toYaw,
            Move = Ease.Linear,
        };

    /// <summary>One unit's part in the filmed turn: where it stands, and where it is being sent.</summary>
    sealed class Order
    {
        public int Team;
        public string Unit;
        public Vector2Int Start;
        public Vector2Int[] Route = Array.Empty<Vector2Int>();

        /// <summary>Whether this route appears during planning. Only the host's plan is shown.</summary>
        public bool Drawn;
    }

    /// <summary>
    /// The turn the gameplay film's first two sections are of, written once because both sections
    /// are built from it. A plan shot followed by an execution that does not match it is the one
    /// mistake this pair cannot survive, and two hand-written copies of ten routes would drift.
    /// <para>
    /// Both crews stand where the game would actually deploy them:
    /// <see cref="GameLoop.CreateSpawnPositions"/> spreads five units across columns 2 to 12 of a
    /// back rank, host on row 0 and opponent on row 9, so the board is contested bottom to top.
    /// Only the host's routes are drawn, because a player never sees the other plan before the
    /// round runs — but both crews walk, which is the whole mechanic.
    /// </para>
    /// <para>
    /// The two plans are deliberately unalike. Five parallel arrows pointing the same way read as
    /// a diagram rather than as a decision, so the host commits three units up the right and
    /// leaves a short holding order in the middle, while the opponent comes down as a broad
    /// crescent. Route lengths run from three cells to six for the same reason.
    /// </para>
    /// </summary>
    static readonly Order[] Turn =
    {
        // Host, on row 0, drawn left to right across the rank — the order a player's eye and the
        // unit cards both run in.
        new()
        {
            Team = Friendly,
            Unit = "Sniper",
            Start = new Vector2Int(2, 0),
            Drawn = true,
            // Cuts to the left edge and climbs it, which puts him on the row-four lane: the one
            // line on Concourse that runs clear from column one to column thirteen.
            Route = new[]
            {
                new Vector2Int(2, 1),
                new Vector2Int(1, 1),
                new Vector2Int(1, 2),
                new Vector2Int(1, 3),
                new Vector2Int(1, 4),
            },
        },
        new()
        {
            Team = Friendly,
            Unit = "Commander",
            Start = new Vector2Int(4, 0),
            Drawn = true,
            Route = new[]
            {
                new Vector2Int(5, 0),
                new Vector2Int(5, 1),
                new Vector2Int(5, 2),
                new Vector2Int(6, 2),
            },
        },
        new()
        {
            Team = Friendly,
            Unit = "Soldier",
            Start = new Vector2Int(7, 0),
            Drawn = true,
            // Round the centre pillar at (7,2) and straight onto the hill.
            Route = new[]
            {
                new Vector2Int(7, 1),
                new Vector2Int(8, 1),
                new Vector2Int(8, 2),
                new Vector2Int(8, 3),
                new Vector2Int(8, 4),
            },
        },
        new()
        {
            Team = Friendly,
            Unit = "Ramrod",
            Start = new Vector2Int(10, 0),
            Drawn = true,
            // The short order in the plan. A turn where every unit is sent the same distance is a
            // turn with no shape to it.
            Route = new[]
            {
                new Vector2Int(11, 0),
                new Vector2Int(11, 1),
                new Vector2Int(11, 2),
            },
        },
        new()
        {
            Team = Friendly,
            Unit = "PogoRider",
            Start = new Vector2Int(12, 0),
            Drawn = true,
            // The longest order, and the only one at two cells a second: all the way up the right
            // edge and then in.
            Route = new[]
            {
                new Vector2Int(13, 0),
                new Vector2Int(13, 1),
                new Vector2Int(13, 2),
                new Vector2Int(13, 3),
                new Vector2Int(13, 4),
                new Vector2Int(12, 4),
            },
        },
        // The opponent, on row 9, coming down as a crescent rather than mirroring any of the
        // above. Its widest unit is on the far side of the board from the host's.
        new()
        {
            Team = Enemy,
            Unit = "Sniper",
            Start = new Vector2Int(12, 9),
            Route = new[]
            {
                new Vector2Int(12, 8),
                new Vector2Int(12, 7),
                new Vector2Int(11, 7),
                new Vector2Int(11, 6),
            },
        },
        new()
        {
            Team = Enemy,
            Unit = "Commander",
            Start = new Vector2Int(10, 9),
            Route = new[]
            {
                new Vector2Int(9, 9),
                new Vector2Int(8, 9),
                new Vector2Int(8, 8),
                new Vector2Int(8, 7),
            },
        },
        new()
        {
            Team = Enemy,
            Unit = "Soldier",
            Start = new Vector2Int(7, 9),
            Route = new[]
            {
                new Vector2Int(7, 8),
                new Vector2Int(6, 8),
                new Vector2Int(6, 7),
                new Vector2Int(6, 6),
            },
        },
        new()
        {
            Team = Enemy,
            Unit = "Ramrod",
            Start = new Vector2Int(4, 9),
            Route = new[]
            {
                new Vector2Int(3, 9),
                new Vector2Int(3, 8),
                new Vector2Int(3, 7),
                new Vector2Int(3, 6),
            },
        },
        new()
        {
            Team = Enemy,
            Unit = "PogoRider",
            Start = new Vector2Int(2, 9),
            // Down the left edge at two cells a second, so he is waiting on the lane before the
            // host's sniper has finished climbing onto it.
            Route = new[]
            {
                new Vector2Int(1, 9),
                new Vector2Int(1, 8),
                new Vector2Int(1, 7),
                new Vector2Int(1, 6),
                new Vector2Int(1, 5),
            },
        },
    };

    /// <summary>
    /// How the plan is written on. One route at a time and nothing overlapping: a board where
    /// three ribbons grow at once reads as a machine filling in a diagram, not as somebody giving
    /// five orders. The rate is what a hand dragging a path down a grid looks like, and the gap is
    /// the pause for picking up the next unit.
    /// </summary>
    const float TurnDrawPerCell = 0.22f;
    const float TurnDrawGap = 0.30f;
    const float TurnDrawFirst = 0.25f;

    /// <summary>How long the finished plan is held before the shot is over.</summary>
    const float TurnPlanHold = 0.8f;

    /// <summary>When the crews step off, which is also when the plan comes down.</summary>
    const float TurnStepOff = 0.6f;

    static Actor TurnActor(Order order) =>
        new()
        {
            Team = order.Team,
            Unit = order.Unit,
            Cell = order.Start,
            Face = new Vector2(order.Start.x, order.Team == Friendly ? 9f : 0f),
            Animation = "Idle",
        };

    static float TurnDrawSeconds(Order order) => TurnDrawPerCell * order.Route.Length;

    /// <summary>
    /// The turn as a plan: the host's routes written on strictly one after another, the next only
    /// starting once the last has finished. Timings are derived rather than listed, so lengthening
    /// a route cannot leave two of them drawing over each other.
    /// </summary>
    static Actor[] TurnPlanned()
    {
        Actor[] cast = new Actor[Turn.Length];
        float at = TurnDrawFirst;
        for (int i = 0; i < Turn.Length; i++)
        {
            Order order = Turn[i];
            cast[i] = TurnActor(order);
            if (!order.Drawn)
                continue;
            cast[i].Route = order.Route;
            cast[i].RouteAt = at;
            cast[i].RouteSeconds = TurnDrawSeconds(order);
            at += TurnDrawSeconds(order) + TurnDrawGap;
        }
        return cast;
    }

    /// <summary>When the last order lands, which is what the planning beat's length is measured from.</summary>
    static float TurnPlanSeconds()
    {
        float at = TurnDrawFirst;
        foreach (Order order in Turn)
        {
            if (order.Drawn)
                at += TurnDrawSeconds(order) + TurnDrawGap;
        }
        return at - TurnDrawGap + TurnPlanHold;
    }

    /// <summary>
    /// The same turn running. It opens on the plan still lying on the board so the two shots are
    /// visibly one round, and the ribbons are struck on the step-off exactly as they are for a
    /// player when execution starts.
    /// </summary>
    static Actor[] TurnExecuted()
    {
        Actor[] cast = new Actor[Turn.Length];
        for (int i = 0; i < Turn.Length; i++)
        {
            Order order = Turn[i];
            cast[i] = TurnActor(order);
            cast[i].Walk = order.Route;
            cast[i].WalkAt = TurnStepOff;
            if (!order.Drawn)
                continue;
            cast[i].Route = order.Route;
            cast[i].RouteAt = 0f;
            cast[i].RouteSeconds = 0.01f;
        }
        return cast;
    }

    /// <summary>The board camera's own framing: the whole 15x10 board, void on all four edges.</summary>
    static Beat WholeBoard(Beat beat, float fromYaw, float toYaw)
    {
        beat.FromLook = new Vector2(7f, 4.5f);
        beat.ToLook = new Vector2(7f, 4.5f);
        beat.FromDistance = 29.2f;
        beat.ToDistance = 28.8f;
        beat.FromPitch = 73f;
        beat.ToPitch = 73f;
        beat.FromYaw = fromYaw;
        beat.ToYaw = toYaw;
        beat.Move = Ease.Linear;
        return beat;
    }

    /// <summary>
    /// The trailer, in order. Act one states the rule the game is built on, act two lets each unit
    /// be the reason a round was won, and act three loses the same crew to an opponent who read
    /// every one of them — each defeat is the counter to the boast that introduced it.
    /// </summary>
    static readonly Beat[] Beats =
    {
        // ---- Act one: the read ---------------------------------------------------------------
        new()
        {
            Name = "a1-fog",
            Seconds = 3.4f,
            Fog = true,
            Cast = new[]
            {
                At(Friendly, "Sniper", 2, 4),
                At(Friendly, "Soldier", 2, 5),
                At(Enemy, "Soldier", 12, 4),
            },
            FromLook = new Vector2(7f, 4.5f),
            ToLook = new Vector2(7f, 4.5f),
            FromDistance = 32f,
            ToDistance = 27f,
            FromPitch = 58f,
            ToPitch = 70f,
            Move = Ease.InOut,
        },
        new()
        {
            Name = "a1-plan",
            Seconds = 4.4f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "Soldier",
                    Cell = new Vector2Int(1, 7),
                    Route = new[]
                    {
                        new Vector2Int(2, 7),
                        new Vector2Int(3, 7),
                        new Vector2Int(3, 6),
                    },
                    RouteAt = 0.2f,
                    RouteSeconds = 1.1f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(1, 2),
                    Route = new[]
                    {
                        new Vector2Int(2, 2),
                        new Vector2Int(3, 2),
                        new Vector2Int(3, 3),
                    },
                    RouteAt = 1.0f,
                    RouteSeconds = 1.1f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "PogoRider",
                    Cell = new Vector2Int(2, 4),
                    Route = new[]
                    {
                        new Vector2Int(3, 4),
                        new Vector2Int(4, 4),
                        new Vector2Int(5, 4),
                        new Vector2Int(6, 4),
                    },
                    RouteAt = 1.8f,
                    RouteSeconds = 1.3f,
                },
            },
            FromLook = new Vector2(4f, 4.5f),
            ToLook = new Vector2(5f, 4.5f),
            FromDistance = 24f,
            ToDistance = 19f,
            FromPitch = 72f,
            ToPitch = 66f,
            Move = Ease.Out,
        },
        new()
        {
            Name = "a1-execute",
            Seconds = 3.6f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "Soldier",
                    Cell = new Vector2Int(1, 7),
                    Walk = new[]
                    {
                        new Vector2Int(2, 7),
                        new Vector2Int(3, 7),
                        new Vector2Int(3, 6),
                    },
                    WalkAt = 0.15f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(1, 2),
                    Walk = new[]
                    {
                        new Vector2Int(2, 2),
                        new Vector2Int(3, 2),
                        new Vector2Int(3, 3),
                    },
                    WalkAt = 0.15f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "PogoRider",
                    Cell = new Vector2Int(2, 4),
                    Walk = new[]
                    {
                        new Vector2Int(3, 4),
                        new Vector2Int(4, 4),
                        new Vector2Int(5, 4),
                        new Vector2Int(6, 4),
                    },
                    WalkAt = 0.15f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Soldier",
                    Cell = new Vector2Int(12, 5),
                    Walk = new[] { new Vector2Int(11, 5), new Vector2Int(10, 5) },
                    WalkAt = 0.15f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(12, 4),
                    Walk = new[] { new Vector2Int(11, 4), new Vector2Int(10, 4) },
                    WalkAt = 0.15f,
                },
            },
            FromLook = new Vector2(7f, 4.5f),
            ToLook = new Vector2(7f, 4.5f),
            FromDistance = 27f,
            ToDistance = 22f,
            FromPitch = 70f,
            ToPitch = 64f,
            Move = Ease.InOut,
        },
        // ---- Act two portraits: the crew, alone, before they do anything ---------------------
        // A showcase pass. Everything else is parked, so each unit stands on an empty deck and the
        // camera drops well below the board's 73 degrees and orbits, which is the only way this
        // game ever looks a character in the face. The pitch stays high enough that the frame is
        // filled with deck rather than the void past the board edge.
        Portrait("p-sniper", "Sniper", 7, 4, 2.7f, 2.3f, -12f, 5f),
        Portrait("p-pogo", "PogoRider", 6, 5, 2.65f, 2.25f, 10f, -6f),
        Portrait("p-shotgunner", "Ramrod", 8, 4, 2.6f, 2.2f, -9f, 7f),
        Portrait("p-soldier", "Soldier", 7, 5, 2.65f, 2.25f, 8f, -5f),
        Portrait("p-commander", "Commander", 6, 4, 2.6f, 2.2f, -7f, 8f),

        // ---- Act two: the crew, each one the reason a round was won --------------------------
        new()
        {
            Name = "a2-sniper",
            Seconds = 6.5f,
            Cast = new[]
            {
                At(Friendly, "Sniper", 2, 4),
                new Actor
                {
                    // Column 10, not 9: Concourse has wall pips at (9,6) and (9,3), and a route
                    // through them neither walks nor reads.
                    // The lock stays armed for three seconds, so he starts one cell off the line
                    // and crosses early inside that window rather than strolling in after it.
                    Team = Enemy,
                    Unit = "Soldier",
                    Cell = new Vector2Int(10, 5),
                    Walk = new[]
                    {
                        new Vector2Int(10, 4),
                        new Vector2Int(10, 3),
                        new Vector2Int(10, 2),
                    },
                    WalkAt = 1.0f,
                    StartHealth = 0.4f,
                },
            },
            Caster = 0,
            AbilityTarget = new Vector2Int(12, 4),
            AbilityAt = 0.6f,
            FromLook = new Vector2(5f, 4.5f),
            ToLook = new Vector2(9f, 4.5f),
            FromDistance = 16f,
            ToDistance = 20f,
            FromPitch = 52f,
            ToPitch = 62f,
            FromYaw = -14f,
            ToYaw = 6f,
            Move = Ease.InOut,
        },
        new()
        {
            Name = "a2-pogo",
            Seconds = 5.2f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "PogoRider",
                    Cell = new Vector2Int(4, 7),
                    Face = new Vector2(7f, 5f),
                    ShootAt = 2.4f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Commander",
                    Cell = new Vector2Int(7, 5),
                    Face = new Vector2(12f, 5f),
                    StartHealth = 0.35f,
                },
            },
            Caster = 0,
            AbilityTarget = new Vector2Int(6, 5),
            AbilityAt = 0.8f,
            FromLook = new Vector2(5f, 6f),
            ToLook = new Vector2(6.6f, 5f),
            FromDistance = 13f,
            ToDistance = 11f,
            FromPitch = 48f,
            ToPitch = 58f,
            FromYaw = 18f,
            ToYaw = -8f,
            Move = Ease.Out,
        },
        new()
        {
            Name = "a2-shotgunner",
            Seconds = 6.0f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(3, 5),
                    Walk = new[] { new Vector2Int(4, 5) },
                    WalkAt = 0.05f,
                    Face = new Vector2(8f, 5f),
                    // Fires before he rushes, not after: the rush shield stops his own pellets as
                    // readily as incoming fire, and outside a real round nothing retires it. He
                    // clears the room, then breaches it, which reads the same way round.
                    ShootAt = 0.9f,
                },
                new Actor
                {
                    Team = Enemy,
                    // Inside his two-cell reach at the moment he fires. Staged further out he was
                    // shooting at nothing and the room cleared itself.
                    Unit = "Soldier",
                    Cell = new Vector2Int(6, 5),
                    Walk = new[] { new Vector2Int(5, 5) },
                    WalkAt = 0.05f,
                    StartHealth = 0.05f,
                    HoldsFire = true,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Commander",
                    Cell = new Vector2Int(6, 4),
                    Walk = new[] { new Vector2Int(5, 4) },
                    WalkAt = 0.05f,
                    StartHealth = 0.05f,
                    HoldsFire = true,
                },
            },
            Caster = 0,
            AbilityTarget = new Vector2Int(7, 5),
            AbilityAt = 2.4f,
            FromLook = new Vector2(6f, 4.6f),
            ToLook = new Vector2(7.6f, 4.8f),
            FromDistance = 12.5f,
            ToDistance = 10.5f,
            FromPitch = 46f,
            ToPitch = 56f,
            FromYaw = -20f,
            ToYaw = -4f,
            Move = Ease.Out,
        },
        new()
        {
            Name = "a2-soldier",
            Seconds = 5.0f,
            Cast = new[]
            {
                At(Friendly, "Soldier", 4, 4),
                At(Enemy, "Soldier", 9, 5, 0.5f),
                At(Enemy, "Ramrod", 10, 4, 0.5f),
                At(Enemy, "PogoRider", 8, 4, 0.5f),
            },
            Caster = 0,
            AbilityTarget = new Vector2Int(9, 4),
            AbilityAt = 0.7f,
            FromLook = new Vector2(7f, 4.4f),
            ToLook = new Vector2(9f, 4.2f),
            FromDistance = 14.5f,
            ToDistance = 12.5f,
            FromPitch = 54f,
            ToPitch = 64f,
            FromYaw = 10f,
            ToYaw = -6f,
            Move = Ease.InOut,
        },
        new()
        {
            Name = "a2-commander",
            Seconds = 5.4f,
            Cast = new[]
            {
                At(Friendly, "Commander", 4, 4),
                new Actor
                {
                    Team = Friendly,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(4, 6),
                    Walk = new[]
                    {
                        new Vector2Int(4, 5),
                        new Vector2Int(5, 5),
                        new Vector2Int(6, 5),
                        new Vector2Int(7, 5),
                    },
                    WalkAt = 2.4f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Soldier",
                    Cell = new Vector2Int(4, 2),
                    Walk = new[]
                    {
                        new Vector2Int(5, 2),
                        new Vector2Int(6, 2),
                        new Vector2Int(6, 3),
                        new Vector2Int(7, 3),
                    },
                    WalkAt = 2.6f,
                },
                At(Enemy, "Sniper", 12, 5),
            },
            Caster = 0,
            AbilityTarget = new Vector2Int(7, 4),
            AbilityAt = 0.7f,
            SmokeFootprint = true,
            FromLook = new Vector2(6f, 4.5f),
            ToLook = new Vector2(7.4f, 4.5f),
            FromDistance = 17f,
            ToDistance = 14.5f,
            FromPitch = 50f,
            ToPitch = 60f,
            FromYaw = -12f,
            ToYaw = 4f,
            Move = Ease.InOut,
        },
        // ---- Act three: an actual match, filmed in one take ---------------------------------
        // Nothing here is staged. Both crews start whole, both seats are played by the same bot
        // reasoning off the same board, and the round loop decides who dies. The camera sits where
        // the board camera sits and simply watches, minus the HUD. It runs long because a real
        // match takes as long as it takes; the edit picks the stretch where a crew comes apart.
        new()
        {
            Name = "a3-match",
            // Long enough to reach a few rounds at real speed, now that each one holds its
            // planning phase open before executing; the edit takes one planning-plus-round pair.
            Seconds = 52f,
            LiveMatch = true,
            FromLook = new Vector2(7f, 4.5f),
            ToLook = new Vector2(7f, 4.5f),
            // The whole board, as large as it will go, with nothing cut off.
            //
            // This sits at the board camera's own 73 degrees rather than the lower, more dramatic
            // angle the rest of the trailer uses. At 66 degrees the near half of the board eats so
            // much of the frame that its front edge falls off the bottom while a band of empty
            // void sits above the far edge — the shot is simultaneously cropped and mostly empty.
            // Flattening the angle projects the board evenly, so it fits at a much closer distance
            // and fills the frame. A 15x10 board is 40.5 by 27 units; at this pitch that needs
            // about 22 units of distance to fit vertically, which is the binding axis.
            //
            // Far enough back that there is visible void on all four sides. At 22-23 units the
            // board only just fits and its near edge still runs off the bottom of the frame, which
            // is the one thing this shot must never do. 26.5 was not far enough either: projecting
            // the board's own corners through this pose puts its near corner within seven pixels
            // of the bottom of the frame at the end of the move, and past it by the last second.
            // Yaw is what costs that margin — swinging the board turns its near corners down — so
            // the drift is small and the distance is nearly held.
            FromDistance = 29.2f,
            ToDistance = 28.8f,
            FromPitch = 73f,
            ToPitch = 73f,
            FromYaw = 1.2f,
            ToYaw = -1.2f,
            Move = Ease.Linear,
        },
        // ---- Planning with abilities on the board -------------------------------------------
        // The live match plans at most one ability a round, because that is what the bot does, so
        // a shot of a board carrying a whole turn's worth of intent has to be staged. Both of
        // these are framed exactly like the live take — the board camera's own 73 degrees, far
        // enough back for void on all four sides — so nothing is cropped and they cut against the
        // match without a change of viewpoint.
        //
        // Every telegraph is built by PlanMovement itself, so these are the arcs, discs,
        // footprints and lock lines the game draws, not a set made to look like them.
        new()
        {
            Name = "g-plan-abilities",
            Seconds = 6.0f,
            Cast = new[]
            {
                // Rush left, grenade the middle, lock the centre column, vault the right, and
                // smoke the corner the vault lands next to: one telegraph of each shape the game
                // owns, appearing in roster order.
                new Actor
                {
                    Team = Friendly,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(3, 0),
                    Face = new Vector2(3f, 9f),
                    // Directional: the cell picked names a heading, and the rush resolves to the
                    // furthest of its three cells that is not a wall — (3,3) on this board.
                    AbilityPlan = new Vector2Int(3, 1),
                    AbilityPlanAt = 0.35f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Soldier",
                    Cell = new Vector2Int(5, 1),
                    Face = new Vector2(5f, 9f),
                    AbilityPlan = new Vector2Int(5, 5),
                    AbilityPlanAt = 0.85f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Sniper",
                    Cell = new Vector2Int(8, 2),
                    Face = new Vector2(8f, 9f),
                    // Foundry has no long lanes by design, so the lock is laid up column eight —
                    // clear from row 2 to row 7 and stopped by the wall pip at (8,8).
                    AbilityPlan = new Vector2Int(8, 7),
                    AbilityPlanAt = 1.35f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "PogoRider",
                    Cell = new Vector2Int(11, 1),
                    Face = new Vector2(12f, 9f),
                    AbilityPlan = new Vector2Int(12, 5),
                    AbilityPlanAt = 1.85f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Commander",
                    Cell = new Vector2Int(13, 0),
                    Face = new Vector2(12f, 9f),
                    AbilityPlan = new Vector2Int(12, 3),
                    AbilityPlanAt = 2.35f,
                },
                // The other crew, holding its rank. A planning board with nothing to plan against
                // reads as a tutorial diagram.
                At(Enemy, "PogoRider", 1, 9),
                At(Enemy, "Soldier", 4, 8),
                At(Enemy, "Sniper", 7, 9),
                At(Enemy, "Ramrod", 10, 8),
                At(Enemy, "Commander", 13, 9),
            },
            FromLook = new Vector2(7f, 4.5f),
            ToLook = new Vector2(7f, 4.5f),
            FromDistance = 29.2f,
            ToDistance = 28.8f,
            FromPitch = 73f,
            ToPitch = 73f,
            FromYaw = -1.2f,
            ToYaw = 1.2f,
            Move = Ease.Linear,
        },
        new()
        {
            Name = "g-plan-mixed",
            Seconds = 6.0f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(3, 0),
                    Route = new[]
                    {
                        new Vector2Int(3, 1),
                        new Vector2Int(3, 2),
                        new Vector2Int(3, 3),
                    },
                    RouteAt = 0.3f,
                    RouteSeconds = 0.9f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Soldier",
                    Cell = new Vector2Int(5, 1),
                    Face = new Vector2(5f, 9f),
                    AbilityPlan = new Vector2Int(5, 5),
                    AbilityPlanAt = 1.0f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Sniper",
                    Cell = new Vector2Int(8, 2),
                    Route = new[]
                    {
                        new Vector2Int(8, 3),
                        new Vector2Int(8, 4),
                        new Vector2Int(8, 5),
                    },
                    RouteAt = 1.6f,
                    RouteSeconds = 0.9f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Commander",
                    Cell = new Vector2Int(13, 0),
                    Face = new Vector2(12f, 9f),
                    AbilityPlan = new Vector2Int(12, 3),
                    AbilityPlanAt = 2.2f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "PogoRider",
                    Cell = new Vector2Int(11, 1),
                    Route = new[]
                    {
                        new Vector2Int(11, 2),
                        new Vector2Int(11, 3),
                        new Vector2Int(12, 3),
                    },
                    RouteAt = 2.8f,
                    RouteSeconds = 0.9f,
                },
                At(Enemy, "PogoRider", 1, 9),
                At(Enemy, "Soldier", 4, 8),
                At(Enemy, "Sniper", 7, 9),
                At(Enemy, "Ramrod", 10, 8),
                At(Enemy, "Commander", 13, 9),
            },
            FromLook = new Vector2(7f, 4.5f),
            ToLook = new Vector2(7f, 4.5f),
            FromDistance = 29.2f,
            ToDistance = 28.8f,
            FromPitch = 73f,
            ToPitch = 73f,
            FromYaw = 1.2f,
            ToYaw = -1.2f,
            Move = Ease.Linear,
        },
        // ---- The gameplay film: plan, that plan running, and the fighting it leads to --------
        //
        // The first two are one turn seen twice, off the shared table above and at one framing,
        // so cutting from the plan to the round is a cut in time and nothing else. The rest are
        // the montage: seven short shots of the game's five abilities and two firefights, each
        // staged close enough that the montage never repeats a viewpoint.
        WholeBoard(
            new Beat
            {
                Name = "n-plan",
                // Derived from the draw schedule: five orders written one after another, plus the
                // hold on the finished plan. Nothing else happens on this board.
                Seconds = TurnPlanSeconds(),
                Cast = TurnPlanned(),
            },
            -1.0f,
            0.6f
        ),
        WholeBoard(
            new Beat
            {
                Name = "n-exec",
                // The slowest order is five cells at one cell a second, so the crews are still
                // crossing at five and a half; the rest is the contact that crossing buys.
                Seconds = 8.6f,
                Cast = TurnExecuted(),
                RibbonsClearAt = TurnStepOff - 0.04f,
            },
            0.6f,
            -1.0f
        ),
        // Area Lock down row four, which on Concourse is clear from column one to thirteen — the
        // only board in the set that gives the line its full width. The crosser starts one cell
        // off the lane and steps into it well inside the three seconds the lock stays armed.
        new()
        {
            Name = "n-lock",
            Seconds = 4.2f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "Sniper",
                    Cell = new Vector2Int(3, 4),
                    Face = new Vector2(13f, 4f),
                    Animation = "Idle",
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Soldier",
                    Cell = new Vector2Int(8, 5),
                    Walk = new[] { new Vector2Int(8, 4), new Vector2Int(8, 3) },
                    WalkAt = 0.9f,
                    StartHealth = 0.4f,
                },
            },
            Caster = 0,
            AbilityTarget = new Vector2Int(12, 4),
            AbilityAt = 0.5f,
            FromLook = new Vector2(5f, 4.5f),
            ToLook = new Vector2(8.5f, 4.3f),
            FromDistance = 17f,
            ToDistance = 13.5f,
            FromPitch = 50f,
            ToPitch = 60f,
            FromYaw = -18f,
            ToYaw = 6f,
            Move = Ease.Out,
        },
        // A grenade into three units that are still advancing, rather than three standing still
        // waiting for it. Everything in the blast walks a cell in, which is also what arms it.
        new()
        {
            Name = "n-grenade",
            Seconds = 4.2f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "Soldier",
                    Cell = new Vector2Int(4, 5),
                    Walk = new[] { new Vector2Int(5, 5) },
                    WalkAt = 0.05f,
                    Face = new Vector2(11f, 5f),
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Soldier",
                    Cell = new Vector2Int(10, 5),
                    Walk = new[] { new Vector2Int(9, 5) },
                    WalkAt = 0.05f,
                    HoldsFire = true,
                    StartHealth = 0.5f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(10, 4),
                    Walk = new[] { new Vector2Int(9, 4) },
                    WalkAt = 0.05f,
                    HoldsFire = true,
                    StartHealth = 0.35f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Commander",
                    Cell = new Vector2Int(8, 6),
                    Walk = new[] { new Vector2Int(8, 5) },
                    WalkAt = 0.05f,
                    HoldsFire = true,
                    StartHealth = 0.5f,
                },
            },
            Caster = 0,
            AbilityTarget = new Vector2Int(9, 5),
            AbilityAt = 1.4f,
            FromLook = new Vector2(6.5f, 4.8f),
            ToLook = new Vector2(9f, 4.7f),
            FromDistance = 15f,
            ToDistance = 11f,
            FromPitch = 50f,
            ToPitch = 62f,
            FromYaw = 16f,
            ToYaw = -4f,
            Move = Ease.Out,
        },
        // Point blank, then the door. He fires before he rushes because the rush shield stops his
        // own pellets as readily as incoming fire, and outside a real round nothing retires it.
        new()
        {
            Name = "n-breach",
            Seconds = 4.5f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(9, 2),
                    Walk = new[] { new Vector2Int(10, 2) },
                    WalkAt = 0.05f,
                    Face = new Vector2(13f, 2f),
                    ShootAt = 0.9f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Soldier",
                    Cell = new Vector2Int(12, 2),
                    Walk = new[] { new Vector2Int(11, 2) },
                    WalkAt = 0.05f,
                    HoldsFire = true,
                    StartHealth = 0.05f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Commander",
                    Cell = new Vector2Int(12, 1),
                    Walk = new[] { new Vector2Int(11, 1) },
                    WalkAt = 0.05f,
                    HoldsFire = true,
                    StartHealth = 0.05f,
                },
            },
            Caster = 0,
            AbilityTarget = new Vector2Int(13, 2),
            AbilityAt = 2.3f,
            FromLook = new Vector2(10.5f, 2.2f),
            ToLook = new Vector2(12.2f, 1.9f),
            FromDistance = 12f,
            ToDistance = 9.5f,
            FromPitch = 44f,
            ToPitch = 56f,
            FromYaw = -22f,
            ToYaw = 0f,
            Move = Ease.Out,
        },
        // Over the wall pip at (9,6) and down behind a sniper who is looking the other way, which
        // is what doubles the damage. The victim holds fire: he is not meant to win this.
        new()
        {
            Name = "n-vault",
            Seconds = 4.4f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "PogoRider",
                    Cell = new Vector2Int(11, 7),
                    Face = new Vector2(9f, 5f),
                    ShootAt = 2.4f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Sniper",
                    Cell = new Vector2Int(8, 5),
                    Face = new Vector2(2f, 5f),
                    Animation = "Idle",
                    HoldsFire = true,
                    StartHealth = 0.35f,
                },
            },
            Caster = 0,
            AbilityTarget = new Vector2Int(9, 5),
            AbilityAt = 0.9f,
            FromLook = new Vector2(10.5f, 6.2f),
            ToLook = new Vector2(8.8f, 5.2f),
            FromDistance = 12.5f,
            ToDistance = 9.5f,
            FromPitch = 44f,
            ToPitch = 56f,
            FromYaw = 20f,
            ToYaw = -2f,
            Move = Ease.Out,
        },
        // Smoke across the lane, and the crew rotating through it while it hangs.
        new()
        {
            Name = "n-smoke",
            Seconds = 5.2f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "Commander",
                    Cell = new Vector2Int(11, 4),
                    Face = new Vector2(7f, 4f),
                    Animation = "Idle",
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Soldier",
                    Cell = new Vector2Int(11, 6),
                    Walk = new[]
                    {
                        new Vector2Int(11, 5),
                        new Vector2Int(10, 5),
                        new Vector2Int(9, 5),
                        new Vector2Int(8, 5),
                    },
                    WalkAt = 2.3f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(12, 2),
                    Walk = new[]
                    {
                        new Vector2Int(11, 2),
                        new Vector2Int(10, 2),
                        new Vector2Int(9, 2),
                        new Vector2Int(8, 2),
                    },
                    WalkAt = 2.5f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Sniper",
                    Cell = new Vector2Int(3, 4),
                    Face = new Vector2(13f, 4f),
                    Animation = "Idle",
                },
            },
            Caster = 0,
            AbilityTarget = new Vector2Int(8, 4),
            AbilityAt = 0.7f,
            SmokeFootprint = true,
            FromLook = new Vector2(10f, 4.5f),
            ToLook = new Vector2(8.2f, 4.5f),
            FromDistance = 17f,
            ToDistance = 14f,
            FromPitch = 52f,
            ToPitch = 62f,
            FromYaw = 14f,
            ToYaw = -4f,
            Move = Ease.InOut,
        },
        // No abilities at all: four units walk into range of each other and trade. The two on the
        // right start hurt, so the exchange resolves inside a montage shot instead of a magazine.
        new()
        {
            Name = "n-crossfire",
            Seconds = 4.0f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "Soldier",
                    Cell = new Vector2Int(5, 4),
                    Walk = new[] { new Vector2Int(6, 4) },
                    WalkAt = 0.05f,
                    Face = new Vector2(11f, 4f),
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Commander",
                    Cell = new Vector2Int(5, 5),
                    Walk = new[] { new Vector2Int(6, 5) },
                    WalkAt = 0.05f,
                    Face = new Vector2(11f, 5f),
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Soldier",
                    Cell = new Vector2Int(10, 4),
                    Walk = new[] { new Vector2Int(9, 4) },
                    WalkAt = 0.05f,
                    StartHealth = 0.15f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Sniper",
                    Cell = new Vector2Int(10, 5),
                    Walk = new[] { new Vector2Int(9, 5) },
                    WalkAt = 0.05f,
                    StartHealth = 0.25f,
                },
            },
            FromLook = new Vector2(6.5f, 4.5f),
            ToLook = new Vector2(8.6f, 4.5f),
            FromDistance = 14f,
            ToDistance = 11f,
            FromPitch = 48f,
            ToPitch = 58f,
            FromYaw = -16f,
            ToYaw = 8f,
            Move = Ease.Out,
        },
        // Both crews at once, in among it rather than above it. Nobody holds fire and nobody
        // casts; this is the shot that says a round is ten units fighting, not one doing a trick.
        new()
        {
            Name = "n-melee",
            Seconds = 4.6f,
            Cast = new[]
            {
                new Actor
                {
                    Team = Friendly,
                    Unit = "Sniper",
                    Cell = new Vector2Int(4, 6),
                    Walk = new[] { new Vector2Int(4, 5) },
                    WalkAt = 0.05f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Commander",
                    Cell = new Vector2Int(5, 4),
                    Walk = new[] { new Vector2Int(6, 4) },
                    WalkAt = 0.05f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Soldier",
                    Cell = new Vector2Int(5, 5),
                    Walk = new[] { new Vector2Int(6, 5) },
                    WalkAt = 0.05f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(5, 2),
                    Walk = new[] { new Vector2Int(6, 2) },
                    WalkAt = 0.05f,
                },
                new Actor
                {
                    Team = Friendly,
                    Unit = "PogoRider",
                    Cell = new Vector2Int(4, 7),
                    Walk = new[] { new Vector2Int(5, 7) },
                    WalkAt = 0.05f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Soldier",
                    Cell = new Vector2Int(10, 5),
                    Walk = new[] { new Vector2Int(9, 5) },
                    WalkAt = 0.05f,
                    StartHealth = 0.2f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Commander",
                    Cell = new Vector2Int(10, 4),
                    Walk = new[] { new Vector2Int(9, 4) },
                    WalkAt = 0.05f,
                    StartHealth = 0.2f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Ramrod",
                    Cell = new Vector2Int(10, 2),
                    Walk = new[] { new Vector2Int(9, 2) },
                    WalkAt = 0.05f,
                    StartHealth = 0.35f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "Sniper",
                    Cell = new Vector2Int(11, 6),
                    Walk = new[] { new Vector2Int(10, 6) },
                    WalkAt = 0.05f,
                    StartHealth = 0.3f,
                },
                new Actor
                {
                    Team = Enemy,
                    Unit = "PogoRider",
                    Cell = new Vector2Int(11, 8),
                    Walk = new[] { new Vector2Int(11, 7) },
                    WalkAt = 0.05f,
                    StartHealth = 0.3f,
                },
            },
            FromLook = new Vector2(7f, 4.6f),
            ToLook = new Vector2(7.8f, 4.6f),
            FromDistance = 18f,
            ToDistance = 15f,
            FromPitch = 58f,
            ToPitch = 64f,
            FromYaw = -10f,
            ToYaw = 10f,
            Move = Ease.InOut,
        },
        // ---- The other boards ----------------------------------------------------------------
        //
        // Three shots whose subject is the map rather than the unit, so they are framed at the
        // whole-board pose the rest of the film's wide shots use: a board's silhouette is the
        // thing that has to register, and it registers instantly. Each also carries one event big
        // enough to read at that distance — a blast, a smoke bloom, a lock line — because a
        // firefight alone is a few pixels of tracer from here.
        //
        // Each is shot in its own Play session, because the board is chosen when the match stands
        // up: TrailerShoot.ShootOn(tag, map, beats) per map.
        WholeBoard(
            new Beat
            {
                // Foundry: close quarters, and the shot is what that does to a round. Eight units
                // end up two cells apart around the hill because there is nowhere further to
                // stand, and a grenade into that is unmissable.
                Name = "m-foundry",
                Seconds = 3.6f,
                Cast = new[]
                {
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "Soldier",
                        Cell = new Vector2Int(5, 4),
                        Walk = new[] { new Vector2Int(6, 4) },
                        WalkAt = 0.05f,
                    },
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "Commander",
                        Cell = new Vector2Int(5, 5),
                        Walk = new[] { new Vector2Int(6, 5) },
                        WalkAt = 0.05f,
                    },
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "Ramrod",
                        Cell = new Vector2Int(6, 2),
                        Walk = new[] { new Vector2Int(6, 3) },
                        WalkAt = 0.05f,
                    },
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "Sniper",
                        Cell = new Vector2Int(6, 7),
                        Walk = new[] { new Vector2Int(6, 6) },
                        WalkAt = 0.05f,
                    },
                    new Actor
                    {
                        Team = Enemy,
                        Unit = "Soldier",
                        Cell = new Vector2Int(9, 4),
                        Walk = new[] { new Vector2Int(8, 4) },
                        WalkAt = 0.05f,
                        StartHealth = 0.25f,
                    },
                    new Actor
                    {
                        Team = Enemy,
                        Unit = "Commander",
                        Cell = new Vector2Int(9, 5),
                        Walk = new[] { new Vector2Int(8, 5) },
                        WalkAt = 0.05f,
                        StartHealth = 0.25f,
                    },
                    new Actor
                    {
                        Team = Enemy,
                        Unit = "Ramrod",
                        Cell = new Vector2Int(8, 2),
                        Walk = new[] { new Vector2Int(8, 3) },
                        WalkAt = 0.05f,
                        StartHealth = 0.3f,
                    },
                    new Actor
                    {
                        Team = Enemy,
                        Unit = "PogoRider",
                        Cell = new Vector2Int(8, 6),
                        Walk = new[] { new Vector2Int(7, 6) },
                        WalkAt = 0.05f,
                        StartHealth = 0.3f,
                    },
                },
                Caster = 0,
                AbilityTarget = new Vector2Int(8, 4),
                AbilityAt = 1.5f,
            },
            -1.0f,
            0.8f
        ),
        WholeBoard(
            new Beat
            {
                // Bastion: the hill is a walled compound with four one-cell doorways, so the shot
                // is a breach. The defenders screen the (8,7) door and the crew goes through it
                // anyway, while a second man comes in the side door at (9,4).
                //
                // The smoke is thrown by the *defenders*, and that is the whole reason the shot
                // works. A screen is drawn from the local crew's own sight: over a cell they have
                // a look into it is a wash, and only over the cells they cannot see does it close
                // up. Thrown by the crew we are watching, standing right next to it, every cell is
                // seen and the bank films as a haze you can barely find. Thrown at them it is the
                // wall it is supposed to be — and it opens, cell by cell, exactly as the
                // Ramrod pushes into it, because a sightline that ends in smoke is not blocked
                // by it. That is the mechanic doing the work no staging could fake.
                Name = "m-bastion",
                Seconds = 4.4f,
                Cast = new[]
                {
                    new Actor
                    {
                        Team = Enemy,
                        Unit = "Commander",
                        Cell = new Vector2Int(6, 5),
                        Face = new Vector2(8f, 7f),
                        Animation = "Idle",
                    },
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "Ramrod",
                        Cell = new Vector2Int(8, 9),
                        Walk = new[]
                        {
                            new Vector2Int(8, 8),
                            new Vector2Int(8, 7),
                            new Vector2Int(8, 6),
                        },
                        WalkAt = 0.9f,
                    },
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "Soldier",
                        Cell = new Vector2Int(10, 4),
                        Walk = new[] { new Vector2Int(9, 4), new Vector2Int(8, 4) },
                        WalkAt = 0.9f,
                    },
                    // Held well back off the footprint, and that is a lighting decision as much as
                    // a tactical one. A sightline that ends in smoke is not blocked by it, so a
                    // unit parked next to the bank sees the cell it is standing beside and thins
                    // it; two of them on the doorstep thinned the whole screen to a haze. From
                    // over here the crew has no look into it at all, so it stands as the wall it
                    // is and opens one cell at a time under the Ramrod as he walks in.
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "Commander",
                        Cell = new Vector2Int(12, 9),
                        Face = new Vector2(8f, 4f),
                        Animation = "Idle",
                    },
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "Sniper",
                        Cell = new Vector2Int(11, 8),
                        Face = new Vector2(8f, 4f),
                        Animation = "Idle",
                    },
                    new Actor
                    {
                        Team = Enemy,
                        Unit = "Soldier",
                        Cell = new Vector2Int(6, 3),
                        Walk = new[] { new Vector2Int(6, 4) },
                        WalkAt = 0.05f,
                        StartHealth = 0.3f,
                    },
                    new Actor
                    {
                        Team = Enemy,
                        Unit = "Sniper",
                        Cell = new Vector2Int(7, 6),
                        Walk = new[] { new Vector2Int(7, 5) },
                        WalkAt = 0.05f,
                        StartHealth = 0.3f,
                    },
                },
                Caster = 0,
                AbilityTarget = new Vector2Int(8, 7),
                AbilityAt = 0.4f,
                SmokeFootprint = true,
            },
            0.9f,
            -0.9f
        ),
        WholeBoard(
            new Beat
            {
                // Causeway: two spines split the board into a centre court and two side
                // corridors. Area Lock is laid the full width of the court — row four is clear
                // from column four to ten and stopped by the spine at (11,4) — while both
                // corridors fight their own point-blank fight, which is the board's whole idea.
                Name = "m-causeway",
                Seconds = 3.8f,
                Cast = new[]
                {
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "Sniper",
                        Cell = new Vector2Int(4, 4),
                        Face = new Vector2(13f, 4f),
                        Animation = "Idle",
                    },
                    new Actor
                    {
                        Team = Enemy,
                        Unit = "Soldier",
                        Cell = new Vector2Int(7, 5),
                        Walk = new[] { new Vector2Int(7, 4), new Vector2Int(7, 3) },
                        WalkAt = 0.9f,
                        StartHealth = 0.4f,
                    },
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "Ramrod",
                        Cell = new Vector2Int(1, 4),
                        Walk = new[] { new Vector2Int(1, 5) },
                        WalkAt = 0.05f,
                    },
                    new Actor
                    {
                        Team = Enemy,
                        Unit = "Ramrod",
                        Cell = new Vector2Int(1, 7),
                        Walk = new[] { new Vector2Int(1, 6) },
                        WalkAt = 0.05f,
                        StartHealth = 0.25f,
                    },
                    new Actor
                    {
                        Team = Friendly,
                        Unit = "PogoRider",
                        Cell = new Vector2Int(13, 3),
                        Walk = new[] { new Vector2Int(13, 4) },
                        WalkAt = 0.05f,
                    },
                    new Actor
                    {
                        Team = Enemy,
                        Unit = "Commander",
                        Cell = new Vector2Int(13, 7),
                        Walk = new[] { new Vector2Int(13, 6) },
                        WalkAt = 0.05f,
                        StartHealth = 0.3f,
                    },
                },
                Caster = 0,
                AbilityTarget = new Vector2Int(10, 4),
                AbilityAt = 0.5f,
            },
            -0.8f,
            1.0f
        ),
    };

    public static string[] BeatNames()
    {
        string[] names = new string[Beats.Length];
        for (int i = 0; i < Beats.Length; i++)
            names[i] = Beats[i].Name;
        return names;
    }

    /// <summary>
    /// Films every beat into <c>Captures/Trailer/shots/&lt;tag&gt;/</c>. Call from Play mode with a
    /// match already live, then poll <see cref="Finished"/>.
    /// </summary>
    public static void CaptureAll(string tag) => Begin(tag, null);

    /// <summary>Films a subset of beats, by name, so one bad shot can be reshot on its own.</summary>
    public static void Capture(string tag, params string[] beatNames) => Begin(tag, beatNames);

    static void Begin(string tag, string[] filter)
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

        GameObject runnerObject = new("TrailerStudioRunner")
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        runnerObject.AddComponent<Runner>().StartCoroutine(Run(tag, filter, runnerObject));
    }

    sealed class Runner : MonoBehaviour { }

    static IEnumerator Run(string tag, string[] filter, GameObject runnerObject)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        string runDir = Path.Combine(projectRoot, "Captures", "Trailer", "shots", tag);
        LastRunDir = runDir;

        float savedTimeScale = Time.timeScale;
        float savedCaptureDelta = Time.captureDeltaTime;
        UniversalRenderPipelineAsset pipeline =
            UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
            as UniversalRenderPipelineAsset;
        float savedRenderScale = pipeline != null ? pipeline.renderScale : 1f;

        Camera boardCamera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : Camera.main;
        RenderTexture target = null;
        RenderTexture resolve = null;
        Camera studioCamera = null;
        Texture2D readback = null;
        Dictionary<Transform, Vector3> parked = new();

        try
        {
            // A dev match runs fast-forwarded; capture has to run at 1x or captureDeltaTime lies
            // about how long a beat took and the edit's timings stop matching the footage.
            Time.timeScale = 1f;

            // The quality tier renders the board scaled and resolves down. Left on, that scaling is
            // applied a second time into a camera target texture and the frame lands in a corner.
            if (pipeline != null)
                pipeline.renderScale = 1f;

            // URP copies the camera target's descriptor for its intermediate colour buffer, so an
            // 8-bit target would clamp the whole pre-tonemap pipeline at 1.0 and Bloom could never
            // fire. The camera renders float and is resolved down only for the JPEG readback.
            target = new RenderTexture(FrameWidth, FrameHeight, 24, RenderTextureFormat.DefaultHDR)
            {
                antiAliasing = 1,
                name = "TrailerTargetHDR",
            };
            target.Create();
            resolve = new RenderTexture(FrameWidth, FrameHeight, 0, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                name = "TrailerResolve",
            };
            resolve.Create();
            resolveTarget = resolve;
            readback = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGB24, mipChain: false);
            studioCamera = BuildStudioCamera(boardCamera, target);

            // Camera-facing effects must turn to the lens that is recording, not to the board
            // camera parked behind it. This is the one that matters for the smoke screen: it is a
            // stack of near-opaque billboards whose whole readability depends on facing the
            // viewer squarely and on one mass per cell being pushed in front of the others along
            // the view axis. Filmed from the trailer's own lower, closer pose while still aimed
            // at the board camera, its masses came out as tilted ellipses in the wrong order and
            // the bank read as a pile of translucent eggs rather than one cloud.
            //
            // Deliberately not done by repointing GameLoop.TeamCamera, which would also hand the
            // studio camera to ImpactCamera — and the studio camera's local pose is rewritten
            // every frame, so its shake would be wiped. The board camera keeps the shake; only
            // the facing moves.
            GameLoop.devViewCameraOverride = studioCamera;

            // Beat poses are authored in world space, but the camera hangs off the board camera so
            // that an ImpactCamera shake still moves the shot. Poses are therefore converted into
            // the board camera's resting frame, and whatever shake it adds on top survives.
            restInverse =
                boardCamera != null
                    ? Matrix4x4.TRS(
                        boardCamera.transform.position,
                        boardCamera.transform.rotation,
                        Vector3.one
                    ).inverse
                    : Matrix4x4.identity;

            Directory.CreateDirectory(runDir);

            foreach (Beat beat in Beats)
            {
                if (filter != null && Array.IndexOf(filter, beat.Name) < 0)
                    continue;

                Status = $"staging {beat.Name}";
                yield return Drain(
                    RunBeat(beat, runDir, studioCamera, target, readback, parked),
                    beat.Name
                );
            }

            Status = "done";
        }
        finally
        {
            GameLoop.devViewCameraOverride = null;
            Time.captureDeltaTime = savedCaptureDelta;
            Time.timeScale = savedTimeScale;
            if (pipeline != null)
                pipeline.renderScale = savedRenderScale;
            RestoreParked(parked);
            ClearRibbons();
            ClearSmokeScreen();
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

    /// <summary>Pumps a beat to completion, turning a throw inside it into a logged failure.</summary>
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

    static IEnumerator RunBeat(
        Beat beat,
        string runDir,
        Camera studioCamera,
        RenderTexture target,
        Texture2D readback,
        Dictionary<Transform, Vector3> parked
    )
    {
        SetFogEnabled(beat.Fog);
        ClearRibbons();
        ClearSmokeScreen();

        // A live beat touches nothing. Its "cast" is just every unit on the board, named so the
        // report can say who died and when.
        if (beat.LiveMatch)
            ResetLiveDriver();

        // Beats run back to back on one live board, so a beat inherits whatever the last one left
        // in the air: rounds still shooting, and bullets already in flight toward cells the next
        // beat is about to stage someone on. That is how the Sniper came to lose thirty health
        // standing alone in his own portrait.
        CeaseFireEverywhere();

        Actor[] cast = beat.LiveMatch ? LiveCast() : beat.Cast;
        if (!beat.LiveMatch)
        {
            ValidateCells(beat);
            ParkAllUnits(parked);
        }

        // Resolve the cast first: a beat that cannot find one of its units should say so rather
        // than film a stage with a hole in it.
        GameObject[] staged = new GameObject[cast.Length];
        for (int i = 0; i < cast.Length; i++)
        {
            Actor actor = cast[i];
            if (beat.LiveMatch)
            {
                staged[i] = FindUnit(actor.Team, actor.Unit);
                continue;
            }
            GameObject unit = FindUnit(actor.Team, actor.Unit);
            if (unit == null)
            {
                Log.Add($"{beat.Name}: no {actor.Unit} on team {actor.Team}");
                yield break;
            }

            ReviveIfDead(unit, actor.Cell);
            PlaceUnit(unit.transform, actor.Cell, parked);
            if (actor.Face.x >= 0f)
                FaceTowards(unit.transform, CellToWorld(actor.Face));

            // Set unconditionally, so a beat never inherits chip damage from the beat before it and
            // a single-beat reshoot matches the same beat inside a full run.
            Health health = unit.GetComponent<Health>();
            if (health != null)
                health.DevSetHealth(health.MaxHealth * Mathf.Clamp01(actor.StartHealth));

            if (!string.IsNullOrEmpty(actor.Animation))
                unit.GetComponent<AnimationHandler>()?.PlayAnimation(actor.Animation);

            Shooting shooting = unit.GetComponent<Shooting>();
            if (shooting != null)
            {
                shooting.allowShooting = false;
                shooting.StandDown();
            }

            staged[i] = unit;
        }

        Physics.SyncTransforms();
        yield return null;
        yield return null;

        Ability ability = beat.Caster >= 0 ? staged[beat.Caster].GetComponent<Ability>() : null;
        Vector3 abilityWorld = GameLoop.gridCoordToWorld(beat.AbilityTarget);
        float abilityRadius =
            beat.Caster >= 0
                ? staged[beat.Caster].GetComponent<Movement>()?.unitData?.abilityRadius ?? 1.5f
                : 1.5f;
        int casterTeam =
            beat.Caster >= 0 ? cast[beat.Caster].Team : GameLoop.HostTeamIndex;
        Color casterColor = GameLoop.GetTeamColorForViewer(casterTeam);

        List<Coroutine> spawned = new();
        bool[] walked = new bool[cast.Length];
        bool[] fired = new bool[cast.Length];
        bool[] routed = new bool[cast.Length];
        bool[] telegraphed = new bool[cast.Length];

        // What each ability telegraph resolved to. A plan that silently drew nothing — an out of
        // range target, a direction the caster cannot rush — looks identical to a plan that was
        // never asked for, so the report names the ones that landed.
        List<string> telegraphs = new();

        // When each unit fell, so an edit can place a line on a death instead of guessing at it.
        // In a single continuous take this is the only way to know where the beats are.
        float[] deathAt = new float[cast.Length];
        for (int i = 0; i < deathAt.Length; i++)
            deathAt[i] = -1f;
        PathRibbon[] ribbons = new PathRibbon[cast.Length];
        bool abilityFired = false;
        bool ribbonsStruck = false;

        void Tick(float since)
        {
            if (beat.LiveMatch)
                DriveLiveMatch(since);

            if (!ribbonsStruck && beat.RibbonsClearAt >= 0f && since >= beat.RibbonsClearAt)
            {
                ribbonsStruck = true;
                ClearRibbons();
            }

            if (!abilityFired && ability != null && beat.AbilityAt >= 0f && since >= beat.AbilityAt)
            {
                abilityFired = true;
                // Mirrors GameLoop.RunAbility, which is what an activation looks like on every peer.
                HitFlash.FlashTarget(staged[beat.Caster], 0.4f, 1.5f);
                ImpactShockwave.Spawn(
                    staged[beat.Caster].transform.position,
                    casterColor,
                    1.8f,
                    0.45f
                );
                spawned.Add(
                    ability.StartCoroutine(ability.ExecuteAbility(abilityWorld, abilityRadius))
                );
                if (beat.SmokeFootprint)
                    spawned.Add(ability.StartCoroutine(DeploySmokeScreen(beat.AbilityTarget)));
            }

            for (int i = 0; i < cast.Length; i++)
            {
                Actor actor = cast[i];
                GameObject unit = staged[i];
                if (unit == null)
                    continue;

                if (
                    !walked[i]
                    && actor.WalkAt >= 0f
                    && actor.Walk.Length > 0
                    && since >= actor.WalkAt
                )
                {
                    walked[i] = true;
                    Movement movement = unit.GetComponent<Movement>();
                    if (movement != null)
                    {
                        List<Vector3> cells = new(actor.Walk.Length);
                        foreach (Vector2Int cell in actor.Walk)
                            cells.Add(GameLoop.gridCoordToWorld(cell));
                        spawned.Add(movement.StartCoroutine(movement.MoveToCells(cells)));
                    }
                }

                if (deathAt[i] < 0f)
                {
                    Health pulse = unit.GetComponent<Health>();
                    if (pulse != null && !pulse.IsAlive)
                        deathAt[i] = since;
                }

                // Holding fire lasts until this unit's own turn to shoot, if it has one.
                if (actor.HoldsFire && (actor.ShootAt < 0f || since < actor.ShootAt))
                {
                    // StandDown, not just the flag: once a firing cycle has a target it empties
                    // the magazine without rechecking permission, so clearing the flag alone lets
                    // a victim get a full burst away first.
                    Shooting held = unit.GetComponent<Shooting>();
                    if (held != null && held.allowShooting)
                        held.StandDown();
                }

                if (!fired[i] && actor.ShootAt >= 0f && since >= actor.ShootAt)
                {
                    fired[i] = true;
                    Shooting shooting = unit.GetComponent<Shooting>();
                    if (shooting != null)
                    {
                        shooting.allowShooting = true;
                        shooting.StartShooting();
                    }
                }

                if (
                    !telegraphed[i]
                    && actor.AbilityPlanAt >= 0f
                    && actor.AbilityPlan.x >= 0
                    && since >= actor.AbilityPlanAt
                )
                {
                    telegraphed[i] = true;
                    string drawn = ShowAbilityPlan(
                        unit,
                        unit.transform.position,
                        GameLoop.gridCoordToWorld(actor.AbilityPlan)
                    );
                    telegraphs.Add(drawn ?? $"{actor.Unit}:DREW NOTHING");
                }

                if (
                    !routed[i]
                    && !ribbonsStruck
                    && actor.RouteAt >= 0f
                    && actor.Route.Length > 0
                    && since >= actor.RouteAt
                )
                {
                    routed[i] = true;
                    ribbons[i] = CreatePlanRibbon($"TrailerRibbon{i}", i);
                }

                if (ribbons[i] != null && !ribbonsStruck)
                {
                    // The ribbon grows a cell at a time so the shot reads as a plan being drawn
                    // rather than one that was always there.
                    float progress = Mathf.Clamp01(
                        (since - actor.RouteAt) / Mathf.Max(0.01f, actor.RouteSeconds)
                    );
                    int shown = Mathf.Clamp(
                        Mathf.CeilToInt(progress * actor.Route.Length),
                        0,
                        actor.Route.Length
                    );
                    List<Vector3> plan = new(shown + 1)
                    {
                        GameLoop.gridCoordToWorld(actor.Cell),
                    };
                    for (int step = 0; step < shown; step++)
                        plan.Add(GameLoop.gridCoordToWorld(actor.Route[step]));
                    ribbons[i].SetRoute(plan, null);
                }
            }
        }

        yield return CaptureSequence(
            Path.Combine(runDir, beat.Name),
            beat,
            target,
            readback,
            studioCamera,
            Tick
        );

        foreach (GameObject unit in staged)
        {
            if (unit == null)
                continue;
            Shooting shooting = unit.GetComponent<Shooting>();
            if (shooting != null)
            {
                shooting.StandDown();
                shooting.allowShooting = false;
            }
            unit.GetComponent<Ability>()?.ResetForRespawn();
        }
        ClearRibbons();
        ClearSmokeScreen();

        // Whether the beat's kill actually landed is the one thing a frame count cannot tell us,
        // and squinting at contact sheets to find out is guesswork. The report says it outright.
        // End-of-beat HP as well as deaths: a survivor that took no damage at all means its killer
        // never engaged, which is a staging bug, while one left on a sliver is only a timing bug.
        StringBuilder outcome = new();
        for (int i = 0; i < staged.Length; i++)
        {
            if (staged[i] == null)
                continue;
            Health health = staged[i].GetComponent<Health>();
            if (health == null)
                continue;
            string side = cast[i].Team == Friendly ? "blue" : "red";
            outcome.Append(
                health.IsAlive
                    ? $" {side}:{cast[i].Unit}={health.CurrentHealth:0}/{health.MaxHealth:0}"
                    : $" {side}:{cast[i].Unit}=DEAD@{deathAt[i]:0.00}s"
            );
        }

        Log.Add($"{beat.Name}: filmed {beat.Seconds:0.00}s,{outcome}");
        if (telegraphs.Count > 0)
            Log.Add($"{beat.Name} telegraphs: {string.Join(" ", telegraphs)}");
        if (beat.LiveMatch && PhaseLog.Count > 0)
            Log.Add($"{beat.Name} phases: {string.Join(" ", PhaseLog)}");
        if (beat.LiveMatch && PlanLog.Count > 0)
            Log.Add($"{beat.Name} plans: {string.Join(" ", PlanLog)}");
        Status = $"captured {beat.Name}";
        yield return null;
    }

    /// <summary>
    /// Rolls the deterministic clock, drives the beat's camera move, and writes every frame to
    /// <paramref name="beat"/>'s directory alongside a timestamp manifest.
    /// </summary>
    static IEnumerator CaptureSequence(
        string shotDir,
        Beat beat,
        RenderTexture target,
        Texture2D readback,
        Camera studioCamera,
        Action<float> onTick
    )
    {
        Directory.CreateDirectory(shotDir);
        foreach (string stale in Directory.GetFiles(shotDir, "*.jpg"))
            File.Delete(stale);

        StringBuilder manifest = new();
        manifest.AppendLine("frame,time_seconds,phase");

        Time.captureDeltaTime = CaptureStep;

        int written = 0;
        float clock = 0f;
        float total = beat.PreRoll + beat.Seconds;

        while (clock <= total)
        {
            float progress = Mathf.Clamp01(clock / Mathf.Max(0.01f, total));
            float shaped = Shape(beat.Move, progress);
            SetCameraPose(
                studioCamera,
                CellToWorld(Vector2.Lerp(beat.FromLook, beat.ToLook, shaped))
                    + (Vector3.up * beat.Aim),
                Mathf.Lerp(beat.FromDistance, beat.ToDistance, shaped),
                Mathf.Lerp(beat.FromPitch, beat.ToPitch, shaped),
                Mathf.Lerp(beat.FromYaw, beat.ToYaw, shaped)
            );

            if (clock >= beat.PreRoll && onTick != null)
            {
                // A beat that throws must cost its own shot and nothing else. Left unguarded the
                // exception kills this coroutine mid-sequence, the caller never resumes, the
                // capture clock is never restored, and the whole shoot hangs on one bad beat.
                try
                {
                    onTick(clock - beat.PreRoll);
                }
                catch (Exception tickException)
                {
                    Log.Add($"{beat.Name}: TICK THREW {tickException.Message}");
                    onTick = null;
                }
            }

            yield return new WaitForEndOfFrame();

            float shotTime = clock - beat.PreRoll;
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

            clock += CaptureStep;
        }

        Time.captureDeltaTime = 0f;
        File.WriteAllText(Path.Combine(shotDir, "frames.csv"), manifest.ToString());
    }

    static float Shape(Ease ease, float t) =>
        ease switch
        {
            Ease.In => t * t,
            Ease.Out => 1f - ((1f - t) * (1f - t)),
            Ease.InOut => t * t * (3f - (2f * t)),
            _ => t,
        };

    /// <summary>
    /// Stands up the smoke screen the round loop would normally deploy, so a smoke beat shows the
    /// cloud rather than only the canister that asked for it.
    /// <para>
    /// Through <see cref="GameLoop.DevShowSmokeScreen"/> rather than
    /// <see cref="SmokeScreenVisual.Create"/> directly, because the cloud is only half the effect.
    /// The other half is which of its cells the crew can see into: a screen is drawn as a wash
    /// over a cell you have a look at and closes only over the ones you do not, and that is
    /// decided by a vision pass the round loop runs and keeps running. Building the visual by hand
    /// skips all of it, so every mass sits at its unseen alpha and the bank films as an opaque
    /// lump — the picture nobody in the match is actually looking at, since the crew that threw it
    /// is standing right beside it.
    /// </para>
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

        if (GameLoop.Instance != null)
            GameLoop.Instance.DevShowSmokeScreen(cells.ToArray());
    }

    static readonly List<PathRibbon> activeRibbons = new();

    static void ClearSmokeScreen()
    {
        if (GameLoop.Instance != null)
            GameLoop.Instance.DevHideSmokeScreen();
    }

    /// <summary>
    /// A plan ribbon in the planning screen's own selected styling — full alpha and the wider of
    /// its two widths, rather than the 72%-alpha hairline an unselected route draws as.
    /// <para>
    /// That is both the legible choice and the truthful one. A route is drawn in this style while
    /// its unit is the one being given the order, which is exactly what these shots are of; the
    /// unselected style exists for the plans you are not currently touching. At the distance the
    /// whole board is filmed from, the difference is a route you can follow against one you can
    /// only just find.
    /// </para>
    /// </summary>
    static PathRibbon CreatePlanRibbon(string name, int rosterSlot)
    {
        PathRibbon ribbon = PathRibbon.Create(null, name, rosterSlot);
        if (ribbon == null)
            return null;

        ribbon.SetSelected(true);
        activeRibbons.Add(ribbon);
        return ribbon;
    }

    static void ClearRibbons()
    {
        foreach (PathRibbon ribbon in activeRibbons)
        {
            if (ribbon == null)
                continue;
            ribbon.Clear();
            if (ribbon.gameObject != null)
                UnityEngine.Object.Destroy(ribbon.gameObject);
        }
        activeRibbons.Clear();

        foreach (GameObject telegraph in activeTelegraphs)
        {
            if (telegraph == null)
                continue;
            telegraph.SetActive(false);
            UnityEngine.Object.Destroy(telegraph);
        }
        activeTelegraphs.Clear();
    }

    static readonly List<GameObject> activeTelegraphs = new();

    /// <summary>
    /// Puts one unit's ability plan on the board through the planning screen's own builder, so a
    /// planning shot shows the arc, disc, footprint or lock line the game actually draws rather
    /// than a copy of it made for the camera. Returns what the plan reads as, for the report.
    /// </summary>
    static string ShowAbilityPlan(GameObject unit, Vector3 start, Vector3 square)
    {
        UnitData data = unit != null ? unit.GetComponent<Movement>()?.unitData : null;
        if (data == null || !data.selectAbilitySquare)
            return null;

        GameObject host = PlanMovement.BuildAbilityPreview(
            null,
            unit,
            start,
            square,
            data,
            unit.GetComponent<Unit>()?.RosterSlot ?? 0,
            selected: true
        );
        if (host == null)
            return null;

        activeTelegraphs.Add(host);
        Vector2Int cell = GridSystem.ConvertToGridCoords(square);
        return $"{data.unitName}:{data.abilityName}->({cell.x},{cell.y})";
    }

    /// <summary>Resolve surface for the float camera target; see the note where it is created.</summary>
    static RenderTexture resolveTarget;
    static Matrix4x4 restInverse = Matrix4x4.identity;

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
        readback.ReadPixels(new Rect(0, 0, FrameWidth, FrameHeight), 0, 0, recalculateMipMaps: false);
        readback.Apply(updateMipmaps: false);
        RenderTexture.active = previous;
        File.WriteAllBytes(path, readback.EncodeToJPG(JpegQuality));
    }

    static Camera BuildStudioCamera(Camera source, RenderTexture target)
    {
        GameObject cameraObject = new("TrailerStudioCamera")
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        Camera studio = cameraObject.AddComponent<Camera>();

        // Settings are copied field by field rather than with CopyFrom, which also carries over the
        // board camera's locked aspect ratio and renders the target into one corner.
        if (source != null)
        {
            studio.clearFlags = source.clearFlags;
            studio.backgroundColor = source.backgroundColor;
            studio.cullingMask = source.cullingMask;
            studio.nearClipPlane = source.nearClipPlane;
            studio.farClipPlane = source.farClipPlane;
            studio.allowMSAA = source.allowMSAA;
            studio.depth = source.depth + 10f;
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

    static void SetCameraPose(Camera studio, Vector3 lookAt, float distance, float pitch, float yaw)
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 position = lookAt - (rotation * Vector3.forward * distance);

        if (studio.transform.parent == null)
        {
            studio.transform.SetPositionAndRotation(position, rotation);
            return;
        }

        Matrix4x4 local = restInverse * Matrix4x4.TRS(position, rotation, Vector3.one);
        studio.transform.localPosition = local.GetColumn(3);
        studio.transform.localRotation = local.rotation;
    }

    static Vector3 CellToWorld(Vector2 cell)
    {
        return new Vector3(
            GameLoop.gridBounds.xMin + (cell.x * GameLoop.cellSize),
            0f,
            GameLoop.gridBounds.yMin + (cell.y * GameLoop.cellSize)
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

        // Stand on the height the unit had in the live match, not whatever it happens to be at now.
        // Carrying the current y forward sinks a unit into the deck if the beat before it left the
        // model mid-leap or mid-death.
        unit.position = WorldForCell(cell, parked[unit].y);
    }

    // ---- Live match ------------------------------------------------------------------------

    static BotPlayer hostBrain;
    static int plannedRound = -1;
    static int dodgedRound = -1;
    static string lastPhase = "";

    /// <summary>
    /// How long the planning phase is held open before the plans are submitted.
    /// <para>
    /// Dev-mode planning resolves the instant plans arrive, so a filmed match cuts from one
    /// execution straight into the next with nothing to see in between. Holding it open, with the
    /// host's real plan drawn on the board, means the round the camera then watches is visibly the
    /// round those paths just described — planning and execution in one unbroken take, rather than
    /// a planning shot staged separately and edited in front of an unrelated fight.
    /// </para>
    /// </summary>
    public static float PlanHoldSeconds = 5.5f;

    /// <summary>
    /// How long the held plan takes to appear, per unit. A submitted plan exists all at once, but
    /// a board that snaps five finished routes on in a single frame has nothing to watch; drawing
    /// them in one unit at a time is what the section is of.
    /// </summary>
    public static float PlanDrawSeconds = 0.85f;
    const float PlanStagger = 0.42f;

    static PathsDict heldPlans;
    static float planHoldUntil;
    static float planDrawFrom;

    /// <summary>One unit's held plan, and the ribbon or telegraph it is being revealed through.</summary>
    sealed class HeldPlan
    {
        public GameObject Unit;
        public bool IsAbility;
        public List<Vector3> Path;
        public PathRibbon Ribbon;
        public float At;
        public bool Shown;
    }

    static readonly List<HeldPlan> heldDraw = new();

    /// <summary>
    /// Every phase change with the time it happened, so an edit can cut one round out of a live
    /// take instead of guessing where a round starts and stops.
    /// </summary>
    static readonly List<string> PhaseLog = new();

    /// <summary>
    /// What the host planned each round. Whether a round's planning phase has an ability telegraph
    /// on the board is the difference between a usable planning shot and a plain one, and it is
    /// the bot's choice rather than ours, so the report says which rounds have one.
    /// </summary>
    static readonly List<string> PlanLog = new();

    static void ResetLiveDriver()
    {
        hostBrain = null;
        plannedRound = -1;
        dodgedRound = -1;
        lastPhase = "";
        heldPlans = null;
        planHoldUntil = 0f;
        planDrawFrom = 0f;
        heldDraw.Clear();
        PhaseLog.Clear();
        PlanLog.Clear();
    }

    /// <summary>
    /// Takes the host's plan apart into one entry per unit, ordered by roster slot so the board
    /// fills up in the same order the unit cards sit in. Only the host's, because that is all a
    /// player ever sees of a round before it runs.
    /// </summary>
    static void HoldPlanForDrawing(PathsDict plans)
    {
        heldDraw.Clear();
        if (plans == null)
            return;

        List<HeldPlan> pending = new();
        foreach (KeyValuePair<GameObject, (bool ability, List<Vector3> path)> plan in plans)
        {
            if (plan.Key == null || plan.Value.path == null || plan.Value.path.Count < 2)
                continue;
            pending.Add(
                new HeldPlan
                {
                    Unit = plan.Key,
                    IsAbility = plan.Value.ability,
                    Path = plan.Value.path,
                    At = plan.Key.GetComponent<Unit>()?.RosterSlot ?? 0,
                }
            );
        }

        pending.Sort((left, right) => left.At.CompareTo(right.At));
        for (int index = 0; index < pending.Count; index++)
        {
            pending[index].At = index * PlanStagger;
            heldDraw.Add(pending[index]);
        }
    }

    /// <summary>
    /// Reveals the held plan over time: routes grow a cell at a time in the game's own ribbons,
    /// ability plans pop their telegraph the way committing a target does.
    /// </summary>
    static void GrowPlanRibbons(float elapsed)
    {
        foreach (HeldPlan plan in heldDraw)
        {
            if (plan.Unit == null || elapsed < plan.At)
                continue;

            if (plan.IsAbility)
            {
                if (plan.Shown)
                    continue;
                plan.Shown = true;
                ShowAbilityPlan(plan.Unit, plan.Path[0], plan.Path[1]);
                continue;
            }

            if (plan.Ribbon == null)
            {
                int slot = plan.Unit.GetComponent<Unit>()?.RosterSlot ?? 0;
                plan.Ribbon = CreatePlanRibbon($"TrailerPlan{slot}", slot);
                if (plan.Ribbon == null)
                    continue;
            }

            float progress = Mathf.Clamp01(
                (elapsed - plan.At) / Mathf.Max(0.01f, PlanDrawSeconds)
            );
            int steps = plan.Path.Count - 1;
            int shown = Mathf.Clamp(Mathf.CeilToInt(progress * steps), 1, steps);
            plan.Ribbon.SetRoute(plan.Path.GetRange(0, shown + 1), null);
        }
    }

    /// <summary>
    /// Names the round's plan for the report: how far each unit is being sent, and who is aiming
    /// what where. Route length is the number that decides whether a round is worth cutting as a
    /// planning shot — a turn of single-cell shuffles draws four stubs and reads as nothing.
    /// </summary>
    static string DescribePlan(int round)
    {
        List<string> steps = new();
        List<string> abilities = new();
        foreach (HeldPlan plan in heldDraw)
        {
            if (!plan.IsAbility)
            {
                steps.Add((plan.Path.Count - 1).ToString());
                continue;
            }
            UnitData data = plan.Unit.GetComponent<Movement>()?.unitData;
            Vector2Int cell = GridSystem.ConvertToGridCoords(plan.Path[1]);
            abilities.Add($"{data?.abilityName ?? "ability"}->({cell.x},{cell.y})");
        }

        steps.Sort();
        steps.Reverse();
        string ability = abilities.Count > 0 ? string.Join("+", abilities) : "none";
        string cells = steps.Count > 0 ? string.Join("/", steps) : "0";
        return $"r{round}:routeCells={cells},ability={ability}";
    }

    /// <summary>Every unit on the board, named, so a live take can report who died and when.</summary>
    static Actor[] LiveCast()
    {
        List<Actor> cast = new();
        for (int team = 0; team <= 1; team++)
        {
            foreach (GameObject unit in GameLoop.GetTeamUnits(team))
            {
                UnitData data = unit != null ? unit.GetComponent<Movement>()?.unitData : null;
                if (data != null)
                    cast.Add(new Actor { Team = team, Unit = data.unitName });
            }
        }
        return cast.ToArray();
    }

    /// <summary>
    /// Plays the host's seat so a real match can run in front of the camera.
    /// <para>
    /// The opponent seat is the game's own bot. This drives the host seat with a second instance of
    /// that same <see cref="BotPlayer"/>, so both crews are choosing their moves with identical
    /// reasoning off the same board state and neither side is being helped. It answers dodge
    /// windows too — the opposing bot answers its own, and a seat that never dodges would be
    /// handing the other one the match.
    /// </para>
    /// </summary>
    static void DriveLiveMatch(float since)
    {
        GameLoop loop = GameLoop.Instance;
        if (loop == null)
            return;

        string phase = GameLoop.currentPhase;
        int round = loop.RoundNumber;

        if (phase != lastPhase)
        {
            PhaseLog.Add($"r{round}:{phase}@{since:0.00}");
            lastPhase = phase;
        }

        if (phase == "planning" && round != plannedRound)
        {
            if (heldPlans == null)
            {
                hostBrain ??= new BotPlayer(loop, GameLoop.HostTeamIndex);
                heldPlans = hostBrain.CreatePlanningContribution(round);
                HoldPlanForDrawing(heldPlans);
                PlanLog.Add(DescribePlan(round));
                planDrawFrom = since;
                planHoldUntil = since + PlanHoldSeconds;
                return;
            }

            GrowPlanRibbons(since - planDrawFrom);

            if (since >= planHoldUntil)
            {
                // The ribbons come down as the round starts, exactly as they do for a player.
                ClearRibbons();
                loop.DevSubmitPlansServer(heldPlans);
                heldPlans = null;
                plannedRound = round;
            }
        }
        else if (phase == "dodging" && round != dodgedRound)
        {
            AnswerDodge();
            dodgedRound = round;
        }
    }

    /// <summary>
    /// Dives every alerted host unit clear of the nearest threat. An alerted unit is flagged by the
    /// alert badge the round loop switches on above it, which is the only part of the dodge window
    /// visible from out here.
    /// </summary>
    static void AnswerDodge()
    {
        GameObject[] units = GameLoop.GetTeamUnits(GameLoop.HostTeamIndex);
        GameObject[] enemies = GameLoop.GetTeamUnits(GameLoop.OpponentTeamIndex);

        for (int i = 0; i < units.Length; i++)
        {
            GameObject unit = units[i];
            if (unit == null || !unit.activeSelf)
                continue;
            Transform alert = unit.transform.Find("UnitCanvas/Alert");
            if (alert == null || !alert.gameObject.activeSelf)
                continue;

            Vector2Int from = GridSystem.ConvertToGridCoords(unit.transform.position);
            if (TryFindDive(from, enemies, out Vector2Int to))
                DevInput.SetDodgePath(GameLoop.HostTeamIndex, i, to.x, to.y);
        }

        DevInput.SubmitDodge();
    }

    static bool TryFindDive(Vector2Int from, GameObject[] enemies, out Vector2Int destination)
    {
        Vector2 away = Vector2.zero;
        foreach (GameObject enemy in enemies)
        {
            if (enemy == null || !enemy.activeSelf)
                continue;
            Vector2Int cell = GridSystem.ConvertToGridCoords(enemy.transform.position);
            Vector2 offset = new(from.x - cell.x, from.y - cell.y);
            float distance = offset.magnitude;
            if (distance > 0.01f)
                away += offset / (distance * distance);
        }

        // Two cells is the usual dive allowance; prefer the furthest legal step directly away.
        Vector2 heading = away.sqrMagnitude > 0.001f ? away.normalized : Vector2.up;
        for (int reach = 2; reach >= 1; reach--)
        {
            Vector2Int candidate = new(
                from.x + Mathf.RoundToInt(heading.x * reach),
                from.y + Mathf.RoundToInt(heading.y * reach)
            );
            if (
                candidate != from
                && GridSystem.IsCellInBounds(candidate)
                && !GameLoop.wallLayout.Contains(candidate)
            )
            {
                destination = candidate;
                return true;
            }
        }

        destination = from;
        return false;
    }

    /// <summary>
    /// Reports any staged cell that is off the board or inside a wall. A unit standing in a wall is
    /// obvious on camera and invisible in a shot list, so the rig says so rather than filming it.
    /// </summary>
    static void ValidateCells(Beat beat)
    {
        void Check(Vector2Int cell, string what)
        {
            if (!GridSystem.IsCellInBounds(cell))
                Log.Add($"{beat.Name}: {what} ({cell.x},{cell.y}) is off the board");
            else if (GameLoop.wallLayout.Contains(cell))
                Log.Add($"{beat.Name}: {what} ({cell.x},{cell.y}) is a WALL");
        }

        // Units move one cell at a time along the grid; nothing in the game can travel diagonally,
        // so a staged route that does is immediately wrong on camera.
        void Walk(Vector2Int from, IReadOnlyList<Vector2Int> steps, string what)
        {
            Vector2Int at = from;
            foreach (Vector2Int step in steps)
            {
                Check(step, what);
                int distance = Mathf.Abs(step.x - at.x) + Mathf.Abs(step.y - at.y);
                if (distance != 1)
                {
                    Log.Add(
                        $"{beat.Name}: {what} ({at.x},{at.y})->({step.x},{step.y}) is "
                            + (distance == 2 && step.x != at.x && step.y != at.y
                                ? "DIAGONAL"
                                : $"{distance} cells")
                            + "; moves must be one orthogonal step"
                    );
                }
                at = step;
            }
        }

        foreach (Actor actor in beat.Cast)
        {
            Check(actor.Cell, $"{actor.Unit} start");
            Walk(actor.Cell, actor.Walk, $"{actor.Unit} walk");
            Walk(actor.Cell, actor.Route, $"{actor.Unit} route");

            // An ability may legitimately be aimed at a wall — a grenade clears the cell behind
            // it, smoke drops on top of it — so only the board's edge is an error here.
            if (actor.AbilityPlan.x >= 0 && !GridSystem.IsCellInBounds(actor.AbilityPlan))
            {
                Log.Add(
                    $"{beat.Name}: {actor.Unit} ability target "
                        + $"({actor.AbilityPlan.x},{actor.AbilityPlan.y}) is off the board"
                );
            }
        }

        // Two bodies cannot share a cell, and a walk that ends on someone reads as clipping
        // through them.
        foreach (Actor actor in beat.Cast)
        {
            foreach (Actor other in beat.Cast)
            {
                if (ReferenceEquals(actor, other))
                    continue;
                if (actor.Cell == other.Cell)
                    Log.Add($"{beat.Name}: {actor.Unit} and {other.Unit} share ({actor.Cell.x},{actor.Cell.y})");
                foreach (Vector2Int step in actor.Walk)
                {
                    if (step == other.Cell && other.Walk.Length == 0)
                        Log.Add($"{beat.Name}: {actor.Unit} walks through {other.Unit} at ({step.x},{step.y})");
                }
            }
        }
    }

    static void FaceTowards(Transform unit, Vector3 worldTarget)
    {
        Vector3 flat = worldTarget - unit.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.0001f)
            unit.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
    }

    /// <summary>
    /// Stands every unit down and clears every live round, so a beat starts on a quiet board.
    /// Parking a unit offstage does not stop it shooting, and a bullet already travelling belongs
    /// to no unit at all.
    /// </summary>
    static void CeaseFireEverywhere()
    {
        for (int team = 0; team <= 1; team++)
        {
            foreach (GameObject unit in GameLoop.GetTeamUnits(team))
            {
                Shooting shooting = unit != null ? unit.GetComponent<Shooting>() : null;
                if (shooting == null)
                    continue;
                shooting.StandDown();
                shooting.allowShooting = false;
            }
        }

        foreach (Bullet bullet in UnityEngine.Object.FindObjectsByType<Bullet>(FindObjectsSortMode.None))
        {
            if (bullet != null)
                UnityEngine.Object.Destroy(bullet.gameObject);
        }
    }

    static void ParkAllUnits(Dictionary<Transform, Vector3> parked)
    {
        for (int team = 0; team <= 1; team++)
        {
            foreach (GameObject unit in GameLoop.GetTeamUnits(team))
            {
                if (unit == null || !unit.activeSelf)
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

    static GameObject FindUnit(int team, string unitName)
    {
        foreach (GameObject unit in GameLoop.GetTeamUnits(team))
        {
            if (unit == null)
                continue;
            UnitData data = unit.GetComponent<Movement>()?.unitData;
            if (data != null && string.Equals(data.unitName, unitName, StringComparison.OrdinalIgnoreCase))
                return unit;
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
            // Fog control is a convenience; a beat is still valid without it.
        }
    }
}
#endif
