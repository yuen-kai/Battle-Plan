using UnityEngine;

/// <summary>
/// The camera's reaction to a hit: the part of the impact the player feels rather than sees.
/// <para>
/// Two tools, in ascending order of how much they should be spent: a directional punch that starts
/// at full force on the first frame and decays away inside a third of a second, and a hitstop that
/// holds the world — the punch included — still for a few frames so the impact frame is actually
/// seen. Overused, both read as noise; the job is to make a big hit unmistakably bigger than a
/// small one.
/// </para>
/// <para>Purely local and visual. Call on every peer.</para>
/// </summary>
public static class ImpactCamera
{
    // Amplitude is authored as a fraction of the frame height at the impact's depth rather than in
    // world units, so the same hit occupies the same share of the screen however far back the board
    // camera happens to sit.
    //
    // Translation gets the smallest share of the three axes. It is the only one that can slide the
    // board out of frame, and a whole board crossing several percent of the screen with hard edges
    // reads as a mis-registered frame rather than as force. Roughly 2.5% of frame height at the
    // strength of a heavy hit is as far as it can go before it stops being felt and starts being
    // noticed.
    const float KickFrameFraction = 0.0056f;

    // Strength 2 has to feel more than twice strength 1 or the top of the scale is wasted.
    const float StrengthExponent = 1.35f;
    const float MaxStrength = 3f;

    // A shake that holds its amplitude reads as a rumble. This one is an impulse: full force on the
    // frame of contact, most of it spent inside 100ms, gone well before the player can plan again.
    const float ShortestSeconds = 0.20f;
    const float LongestSeconds = 0.34f;
    const float DecayRate = 4.2f;

    // Driven from a clock rather than from Random.Range per frame, so the same hit shakes the same
    // way at 30fps, at 240fps and under the capture rig's fixed step.
    //
    // Frequency is bounded by what a frame rate can actually show: a reversal has to span several
    // rendered frames or consecutive frames land on opposite sides of the base pose and the shake
    // aliases into buzz instead of decaying. One and a half cycles inside the envelope is a
    // displacement, a return and a single overshoot, which is the shape of a real punch.
    const float PrimaryHertz = 5f;
    const float SecondaryHertz = 7f;

    // Enough lateral wander that the path is not a straight line, little enough that the direction
    // of the shove never becomes ambiguous.
    const float SwayWeight = 0.22f;

    // The dolly carries the largest share of the impulse. Pushing the camera back along its own
    // view axis is the one component that cannot take the board off screen however hard it is hit,
    // so the force that would otherwise have to be spent on translation is spent here instead.
    const float DollyWeight = 3f;

    // Roll is the only rotation that reads as rotation from a board camera looking down at 73
    // degrees, and it buys far more perceived force per unit of screen motion than translation: a
    // couple of degrees swings a frame corner further than the whole kick does, without moving the
    // centre of the board at all. Pitch at this camera angle is indistinguishable from a vertical
    // shove, so it is held to a token share rather than allowed to spend the translation budget a
    // second time under a different name.
    const float RollDegreesAtUnitStrength = 1.92f;
    const float PitchShareOfRoll = 0.1f;
    const float MaxRotationDegrees = 5.5f;

    // Below this the impact sits close enough to the view axis that its screen direction is noise.
    // A head-on hit throws the camera up instead, so the board drops away under the player.
    const float LateralThreshold = 0.2f;

    const float EdgeAttenuation = 0.75f;
    const float OffscreenAttenuation = 0.35f;

    // Distances outside this range are a caller mistake, not a framing choice.
    const float NearestImpact = 4f;
    const float FurthestImpact = 80f;

    // Long enough to register, short enough that it can never be mistaken for a hitch.
    const float MaxHitstopSeconds = 0.12f;
    const float TimeScaleEpsilon = 0.0005f;

    // The shake runs on scaled time so a freeze holds it, which means a clock stopped by anything
    // other than a hitstop would strand the camera off its base pose. Past this much unscaled time
    // a shake gives up and hands the pose back rather than waiting for a clock that is not coming.
    const float StallGraceSeconds = 0.5f;

    // Bounds a hitch. A stalled frame must not teleport a live oscillation to an arbitrary phase.
    const float LongestFrameStep = 0.05f;

    // Enough for every unit on the board to be hit in the same beat; past that the oldest shakes
    // are the ones the player has stopped watching.
    const int MaxConcurrentShakes = 8;

    // A pile-up of simultaneous hits still has to leave the board on screen. The dolly is held to
    // its own, looser limit: stacking it can only ever shrink the board, never push it past an edge.
    const float StackedOffsetLimit = 2.5f;
    const float StackedDollyLimit = 1.6f;

    static Driver driver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        driver = null;
    }

    /// <summary>
    /// Shakes and punches the board camera for an impact at <paramref name="worldPosition"/>.
    /// </summary>
    /// <param name="worldPosition">Where the hit happened, so the shake can be directional.</param>
    /// <param name="strength">
    /// 0 is imperceptible, 1 is a standard grenade, 2 is the biggest thing in the game. Impacts
    /// off the edge of the view should attenuate rather than shaking at full force.
    /// </param>
    public static void Punch(Vector3 worldPosition, float strength = 1f)
    {
        if (float.IsNaN(strength) || strength <= 0.01f)
            return;

        float positionCheck = worldPosition.sqrMagnitude;
        if (float.IsNaN(positionCheck) || float.IsInfinity(positionCheck))
            return;

        Camera camera = BoardCamera();
        if (camera == null)
            return;

        Driver active = EnsureDriver();
        if (active == null)
            return;

        Transform view = camera.transform;
        float clampedStrength = Mathf.Min(strength, MaxStrength);
        float scaled = Mathf.Pow(clampedStrength, StrengthExponent);
        float attenuation = ViewAttenuation(camera, worldPosition);

        Vector3 toCamera = view.position - worldPosition;
        float distance = Mathf.Clamp(toCamera.magnitude, NearestImpact, FurthestImpact);
        float frameHeight = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float amplitude = KickFrameFraction * frameHeight * scaled * attenuation;
        float rollDegrees = RollDegreesAtUnitStrength * scaled * attenuation;

        Vector3 local = view.InverseTransformDirection(toCamera.normalized);
        Vector2 lateral = new(local.x, local.y);
        float lateralLength = lateral.magnitude;
        Vector2 direction = lateralLength > LateralThreshold ? lateral / lateralLength : Vector2.up;

        active.Add(
            camera,
            new Shake
            {
                KickAxis = ToParentSpace(view, view.right * direction.x + view.up * direction.y),
                SwayAxis = ToParentSpace(view, view.right * -direction.y + view.up * direction.x),
                DollyAxis = ToParentSpace(view, -view.forward * Mathf.Clamp01(-local.z)),
                Amplitude = amplitude,
                // Tipping away from the shove: a nose that turns into the translation cancels
                // most of it on screen, and the pair has to reinforce or the kick reads as half
                // the force it is.
                PitchDegrees = -rollDegrees * PitchShareOfRoll * direction.y,
                RollDegrees = rollDegrees * (direction.x >= 0f ? -1f : 1f),
                SwayPhase = PhaseFor(worldPosition),
                Duration = Mathf.Lerp(
                    ShortestSeconds,
                    LongestSeconds,
                    Mathf.Clamp01((clampedStrength - 0.5f) / 1.5f)
                ),
            }
        );
    }

    /// <summary>
    /// Freezes the simulation briefly so the impact frame registers. The shake freezes with it: a
    /// hitstop that let the camera keep travelling would only prove that nothing was held.
    /// </summary>
    /// <param name="seconds">Unscaled duration of the freeze. Keep this in the 0.03-0.12 range.</param>
    /// <param name="timeScale">Scale to hold during the freeze; 0 is a hard stop.</param>
    public static void Hitstop(float seconds = 0.06f, float timeScale = 0.02f)
    {
        if (float.IsNaN(seconds) || float.IsNaN(timeScale))
            return;

        float hold = Mathf.Clamp(seconds, 0f, MaxHitstopSeconds);
        if (hold <= 0f)
            return;

        Driver active = EnsureDriver();
        if (active == null)
            return;

        active.HoldTime(hold, Mathf.Clamp01(timeScale));
    }

    /// <summary>Deterministic per location, so a revision can be compared frame for frame.</summary>
    static float PhaseFor(Vector3 worldPosition)
    {
        return Mathf.Repeat(worldPosition.x * 12.9898f + worldPosition.z * 78.233f, Mathf.PI * 2f);
    }

    static Camera BoardCamera()
    {
        Camera camera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        return camera != null ? camera : Camera.main;
    }

    /// <summary>
    /// The offset is written to the camera's local pose so a moving rig above it still composes;
    /// the shake axes therefore have to be resolved out of world space once, up front.
    /// </summary>
    static Vector3 ToParentSpace(Transform view, Vector3 worldDirection)
    {
        Transform parent = view.parent;
        return parent != null ? parent.InverseTransformDirection(worldDirection) : worldDirection;
    }

    static float ViewAttenuation(Camera camera, Vector3 worldPosition)
    {
        Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
        if (viewport.z <= 0f || float.IsNaN(viewport.x) || float.IsNaN(viewport.y))
            return OffscreenAttenuation;

        float offCentre =
            Mathf.Max(Mathf.Abs(viewport.x - 0.5f), Mathf.Abs(viewport.y - 0.5f)) * 2f;
        return offCentre <= 1f
            ? Mathf.Lerp(1f, EdgeAttenuation, offCentre)
            : Mathf.Lerp(EdgeAttenuation, OffscreenAttenuation, Mathf.Clamp01(offCentre - 1f));
    }

    /// <summary>
    /// What one rendered frame is worth with the freeze taken out of it, and the only clock a
    /// hitstop may measure itself on: whatever the freeze does to <see cref="Time.timeScale"/>, it
    /// still has to be able to end itself. The capture rig pins a frame to
    /// <see cref="Time.captureDeltaTime"/>, so a filmed freeze lasts exactly as many frames as a
    /// played one.
    /// </summary>
    static float UnscaledFrameDelta()
    {
        float delta = Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;
        return Mathf.Min(delta, LongestFrameStep);
    }

    /// <summary>
    /// What one rendered frame is worth to everything a hitstop is supposed to hold still, the
    /// shake included. <see cref="Time.deltaTime"/> is already the scaled step in both worlds the
    /// shake runs in — <c>unscaledDeltaTime * timeScale</c> in a live match and
    /// <c>captureDeltaTime * timeScale</c> under the capture rig — and it is fixed at the top of
    /// the frame, so a freeze that releases mid-frame releases on the same frame here as it does
    /// for every other effect on the board rather than a frame early.
    /// </summary>
    static float ScaledFrameDelta()
    {
        float ceiling = UnscaledFrameDelta() * Mathf.Max(Time.timeScale, 0f);
        return Mathf.Clamp(Time.deltaTime, 0f, ceiling);
    }

    static Driver EnsureDriver()
    {
        if (driver != null)
            return driver;
        if (!Application.isPlaying)
            return null;

        GameObject host = new("ImpactCameraDriver") { hideFlags = HideFlags.HideAndDontSave };
        driver = host.AddComponent<Driver>();
        return driver;
    }

    struct Shake
    {
        public Vector3 KickAxis;
        public Vector3 SwayAxis;
        public Vector3 DollyAxis;
        public float Amplitude;
        public float PitchDegrees;
        public float RollDegrees;
        public float SwayPhase;
        public float Duration;
        public float Elapsed;
        public float UnscaledElapsed;
    }

    /// <summary>
    /// Holds the one authoritative base pose for the board camera and writes the sum of every live
    /// shake as an offset from it, so overlapping hits add up and still hand the pose back exactly.
    /// </summary>
    sealed class Driver : MonoBehaviour
    {
        // The applied pose is read straight back out of the transform, so a mismatch beyond storage
        // noise means another system repositioned the rig and its pose is the one that outranks us.
        const float PoseEpsilonSquared = 1e-8f;
        const float RotationEpsilon = 1e-6f;

        readonly Shake[] shakes = new Shake[MaxConcurrentShakes];
        int shakeCount;

        Transform anchor;
        Vector3 basePosition;
        Quaternion baseRotation = Quaternion.identity;
        Vector3 appliedPosition;
        Quaternion appliedRotation = Quaternion.identity;
        bool anchored;
        bool applied;

        Camera boardCamera;

        float holdRemaining;
        float holdElapsed;
        float heldTimeScale;
        float appliedTimeScale;
        float restoreTimeScale = 1f;
        bool holding;

        public void Add(Camera camera, Shake shake)
        {
            boardCamera = camera;

            if (shakeCount < shakes.Length)
            {
                shakes[shakeCount++] = shake;
                return;
            }

            // Displace whichever shake is closest to finishing: the newest hit is the one the
            // player is looking at.
            int spent = 0;
            for (int i = 1; i < shakeCount; i++)
            {
                if (
                    shakes[i].Elapsed / shakes[i].Duration
                    > shakes[spent].Elapsed / shakes[spent].Duration
                )
                    spent = i;
            }
            shakes[spent] = shake;
        }

        public void HoldTime(float seconds, float scale)
        {
            if (holding)
            {
                // Overlapping calls extend one freeze instead of stacking into a permanent one,
                // and the total is capped so a burst of hits can never stall the match.
                float extended = Mathf.Min(
                    Mathf.Max(holdRemaining, seconds),
                    MaxHitstopSeconds - holdElapsed
                );
                if (extended <= 0f)
                {
                    ReleaseTime();
                    return;
                }
                heldTimeScale = Mathf.Min(heldTimeScale, scale);
                holdRemaining = extended;
            }
            else
            {
                // A world already paused or running slower than the freeze has nothing to gain
                // from one, and caching its scale here would hand back the wrong value.
                if (Time.timeScale <= scale + TimeScaleEpsilon)
                    return;

                restoreTimeScale = Time.timeScale;
                heldTimeScale = scale;
                holdRemaining = seconds;
                holdElapsed = 0f;
                holding = true;
            }

            Time.timeScale = heldTimeScale;
            appliedTimeScale = heldTimeScale;
        }

        void Update()
        {
            if (!holding)
                return;

            float delta = UnscaledFrameDelta();
            holdElapsed += delta;
            holdRemaining -= delta;
            if (holdRemaining <= 0f)
                ReleaseTime();
        }

        void LateUpdate()
        {
            // Idle costs nothing and, more importantly, leaves the camera's pose entirely alone so
            // whatever else owns the rig is never fighting a driver with no work to do.
            if (shakeCount == 0 && !applied)
                return;

            Transform target = ResolveTarget();
            if (target != anchor)
            {
                RestoreAnchor();
                anchor = target;
                anchored = false;
            }

            if (anchor == null)
            {
                shakeCount = 0;
                applied = false;
                return;
            }

            if (!anchored || !applied || !PoseIsOurs())
            {
                basePosition = anchor.localPosition;
                baseRotation = anchor.localRotation;
                anchored = true;
                applied = false;
            }

            if (shakeCount == 0)
            {
                RestoreAnchor();
                return;
            }

            Vector3 slide = Vector3.zero;
            Vector3 dolly = Vector3.zero;
            float pitch = 0f;
            float roll = 0f;
            float largest = 0f;
            float delta = ScaledFrameDelta();
            float unscaledDelta = UnscaledFrameDelta();

            for (int i = shakeCount - 1; i >= 0; i--)
            {
                float elapsed = shakes[i].Elapsed;
                float progress = elapsed / shakes[i].Duration;

                // The trailing (1 - progress) term lands the envelope on exactly zero rather than
                // truncating a live oscillation, which is what makes the return to pose invisible.
                float envelope = Mathf.Exp(-DecayRate * progress) * (1f - progress);
                float kickWave = Mathf.Cos(2f * Mathf.PI * PrimaryHertz * elapsed);
                float swayWave = Mathf.Sin(
                    2f * Mathf.PI * SecondaryHertz * elapsed + shakes[i].SwayPhase
                );

                float amplitude = shakes[i].Amplitude * envelope;
                slide +=
                    shakes[i].KickAxis * (amplitude * kickWave)
                    + shakes[i].SwayAxis * (amplitude * SwayWeight * swayWave);
                dolly += shakes[i].DollyAxis * (amplitude * DollyWeight * kickWave);
                pitch += shakes[i].PitchDegrees * envelope * kickWave;
                roll += shakes[i].RollDegrees * envelope * kickWave;
                largest = Mathf.Max(largest, shakes[i].Amplitude);

                shakes[i].Elapsed = elapsed + delta;
                shakes[i].UnscaledElapsed += unscaledDelta;
                if (
                    shakes[i].Elapsed >= shakes[i].Duration
                    || shakes[i].UnscaledElapsed >= shakes[i].Duration + StallGraceSeconds
                )
                    shakes[i] = shakes[--shakeCount];
            }

            Vector3 offset =
                Limit(slide, largest * StackedOffsetLimit)
                + Limit(dolly, largest * DollyWeight * StackedDollyLimit);

            anchor.localPosition = basePosition + offset;
            anchor.localRotation =
                baseRotation
                * Quaternion.Euler(
                    Mathf.Clamp(pitch, -MaxRotationDegrees, MaxRotationDegrees),
                    0f,
                    Mathf.Clamp(roll, -MaxRotationDegrees, MaxRotationDegrees)
                );
            appliedPosition = anchor.localPosition;
            appliedRotation = anchor.localRotation;
            applied = true;
        }

        void OnDisable()
        {
            RestoreAnchor();
            ReleaseTime();
        }

        static Vector3 Limit(Vector3 offset, float limit)
        {
            float reach = offset.magnitude;
            return reach > limit && reach > 0f ? offset * (limit / reach) : offset;
        }

        Transform ResolveTarget()
        {
            if (boardCamera == null)
                boardCamera = BoardCamera();
            return boardCamera != null ? boardCamera.transform : null;
        }

        bool PoseIsOurs()
        {
            return (anchor.localPosition - appliedPosition).sqrMagnitude <= PoseEpsilonSquared
                && Mathf.Abs(Quaternion.Dot(anchor.localRotation, appliedRotation))
                    >= 1f - RotationEpsilon;
        }

        void RestoreAnchor()
        {
            if (!applied || anchor == null)
            {
                applied = false;
                return;
            }

            anchor.localPosition = basePosition;
            anchor.localRotation = baseRotation;
            appliedPosition = basePosition;
            appliedRotation = baseRotation;
            applied = false;
        }

        void ReleaseTime()
        {
            if (!holding)
                return;

            holding = false;
            holdRemaining = 0f;
            holdElapsed = 0f;

            // Whoever owns the clock now outranks the freeze: the dev speed multiplier, the capture
            // rig and the end-of-match reset all write timeScale, and only the value the freeze
            // itself put there may be taken back.
            if (Mathf.Abs(Time.timeScale - appliedTimeScale) <= TimeScaleEpsilon)
                Time.timeScale = restoreTimeScale;
        }
    }
}
