using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The roster standing on a turntable. Every unit in the current filter is spawned once, spaced
/// evenly around a ring, and the whole disk spins; whichever unit is nearest the camera is the
/// focused one. A flick coasts through several units and only snaps once it has slowed down, so
/// the carousel never feels notched.
///
/// The catalog only ships gameplay prefabs, so the model a player recognises is the networked one
/// — but a menu must not run it. Each instance is therefore built inside a deactivated holder,
/// which keeps <c>Awake</c> from firing, stripped of its scripts, colliders, and in-world HUD, and
/// only then switched on.
/// </summary>
public class CharacterCarousel : MonoBehaviour
{
    /// <summary>Raised whenever a different unit comes to the front, including while coasting.</summary>
    public event Action FocusChanged;

    [SerializeField]
    [Tooltip("Spun about Y. Models and the ground disk are parented here.")]
    private Transform turntable;

    [SerializeField]
    [Tooltip("Largest the ring is allowed to get, in world units.")]
    private float ringRadius = 4.7f;

    [SerializeField]
    [Tooltip(
        "Gap held between neighbouring units. The ring is sized from this until it hits the cap, "
            + "so a two-unit filter draws a small turntable rather than a mostly empty one."
    )]
    private float neighbourSpacing = 5.5f;

    [SerializeField]
    [Tooltip("Ground disk. Resized to match whatever ring the current filter produces.")]
    private Transform disk;

    [SerializeField]
    [Tooltip("Darker ring under the disk.")]
    private Transform diskRim;

    [SerializeField]
    [Tooltip("Deck left outside the ring, in world units. Has to clear a unit's own base.")]
    private float diskMargin = 1.1f;

    [SerializeField]
    [Tooltip("How far the rim reaches past the deck edge, in world units.")]
    private float diskRimExtra = 0.45f;

    [SerializeField]
    [Tooltip("Degrees the ring turns per pixel of drag.")]
    private float dragDegreesPerPixel = 0.32f;

    [SerializeField]
    [Tooltip("How quickly a released flick loses speed. Higher stops sooner.")]
    private float spinDamping = 2.4f;

    [SerializeField]
    [Tooltip("Coasting below this speed in degrees per second hands over to the snap.")]
    private float settleSpeed = 60f;

    [SerializeField]
    [Tooltip("How hard the ring is pulled onto the nearest unit once it has settled.")]
    private float settleSharpness = 10f;

    [SerializeField]
    [Tooltip("Ceiling on flick speed so one hard swipe cannot spin for several seconds.")]
    private float maxSpinSpeed = 720f;

    [SerializeField]
    [Tooltip(
        "Optional per-unit yaw in degrees, on top of the seat's own angle. Empty is correct while "
            + "every prefab agrees on a forward; this is here for a model that does not."
    )]
    private DisplayPose[] displayPoses = Array.Empty<DisplayPose>();

    private readonly List<UnitData> units = new();
    private readonly List<GameObject> slots = new();

    private float angle;
    private float velocity;
    private bool dragging;
    private bool settling;
    private float settleTarget;
    private int focusIndex;
    private float diskDiameter = -1f;
    private float diskTargetDiameter;
    private Vector3 rigHome;
    private bool rigHomeCaptured;
    private float frontOffset;
    private float frontTargetOffset;

    public int Count => units.Count;
    public int FocusIndex => focusIndex;
    public UnitData FocusUnit =>
        focusIndex >= 0 && focusIndex < units.Count ? units[focusIndex] : null;

    private float Step => units.Count > 0 ? 360f / units.Count : 360f;
    private Transform Table => turntable != null ? turntable : transform;

    /// <summary>
    /// Ring size for the current filter. Holding the gap between neighbours constant keeps the
    /// turntable proportional to how many units are actually on it, and the cap stops a large
    /// roster from pushing everyone out of frame.
    /// </summary>
    private float CurrentRadius
    {
        get
        {
            if (units.Count <= 1)
                return 0f;
            float spread = neighbourSpacing / (2f * Mathf.Sin(Mathf.PI / units.Count));
            return Mathf.Min(ringRadius, spread);
        }
    }

    /// <summary>
    /// Rebuilds the ring. <paramref name="preferredFocus"/> is brought to the front when it
    /// survives the new set, so narrowing to the class you were already reading about does not
    /// throw the carousel back to the first unit.
    /// </summary>
    public void SetUnits(IReadOnlyList<UnitData> newUnits, UnitData preferredFocus)
    {
        ClearSlots();
        units.Clear();
        if (newUnits != null)
        {
            for (int i = 0; i < newUnits.Count; i++)
            {
                if (newUnits[i] != null)
                    units.Add(newUnits[i]);
            }
        }

        for (int i = 0; i < units.Count; i++)
            slots.Add(BuildSlot(units[i], i));

        if (!rigHomeCaptured)
        {
            rigHome = transform.localPosition;
            rigHomeCaptured = true;
        }

        diskTargetDiameter = 2f * (CurrentRadius + diskMargin);
        // A smaller ring puts its front seat further from the camera, which would shrink the
        // focused unit every time a filter narrowed. Walking the whole rig forward by the
        // difference keeps the hero at one size and one place on screen for every filter.
        frontTargetOffset = ringRadius - CurrentRadius;
        if (diskDiameter < 0f)
        {
            diskDiameter = diskTargetDiameter;
            frontOffset = frontTargetOffset;
        }

        int target = preferredFocus != null ? units.IndexOf(preferredFocus) : 0;
        angle = Mathf.Max(0, target) * Step;
        velocity = 0f;
        dragging = false;
        settling = false;
        focusIndex = -1;
        ApplyRotation();
        UpdateFocus();
    }

    public void BeginDrag()
    {
        dragging = true;
        settling = false;
        velocity = 0f;
    }

    public void DragBy(float deltaPixels)
    {
        if (units.Count == 0)
            return;

        // Dragging right carries the ring right, which brings the unit on the left to the front.
        angle -= deltaPixels * dragDegreesPerPixel;
        ApplyRotation();
        UpdateFocus();
    }

    public void EndDrag(float pixelsPerSecond)
    {
        dragging = false;
        settling = false;
        velocity = Mathf.Clamp(
            -pixelsPerSecond * dragDegreesPerPixel,
            -maxSpinSpeed,
            maxSpinSpeed
        );
    }

    /// <summary>Moves a whole number of units, for the arrows, the wheel, and the arrow keys.</summary>
    public void StepBy(int direction)
    {
        if (units.Count == 0)
            return;

        dragging = false;
        velocity = 0f;
        settling = true;
        settleTarget = (Mathf.Round(angle / Step) + direction) * Step;
    }

    private void OnDisable()
    {
        ClearSlots();
        units.Clear();
        focusIndex = -1;
    }

    private void Update()
    {
        // Menus keep their own clock: dev fast-forward and pause both scale Time.deltaTime.
        float dt = Time.unscaledDeltaTime;
        AnimateStage(dt);

        if (dragging || units.Count == 0)
            return;

        if (!settling)
        {
            if (Mathf.Abs(velocity) > settleSpeed)
            {
                angle += velocity * dt;
                velocity *= Mathf.Exp(-spinDamping * dt);
            }
            else
            {
                velocity = 0f;
                settling = true;
                settleTarget = Mathf.Round(angle / Step) * Step;
            }
        }

        if (settling)
        {
            angle = Mathf.Lerp(angle, settleTarget, 1f - Mathf.Exp(-settleSharpness * dt));
            if (Mathf.Abs(settleTarget - angle) < 0.02f)
            {
                // Multiples of the step are also multiples of a full turn, so folding the angle
                // back into one revolution here keeps a long session from losing precision.
                angle = Mathf.Repeat(settleTarget, 360f);
                settling = false;
            }
        }

        ApplyRotation();
        UpdateFocus();
    }

    private void ApplyRotation()
    {
        Table.localRotation = Quaternion.Euler(0f, angle, 0f);
    }

    /// <summary>Eases the deck size and the rig's depth toward the current ring, so changing
    /// filter reads as the turntable resizing rather than the ground popping to a new size.</summary>
    private void AnimateStage(float dt)
    {
        if (diskDiameter < 0f)
            return;

        float blend = 1f - Mathf.Exp(-7f * dt);
        diskDiameter = Mathf.Lerp(diskDiameter, diskTargetDiameter, blend);
        if (Mathf.Abs(diskTargetDiameter - diskDiameter) < 0.01f)
            diskDiameter = diskTargetDiameter;

        frontOffset = Mathf.Lerp(frontOffset, frontTargetOffset, blend);
        if (Mathf.Abs(frontTargetOffset - frontOffset) < 0.01f)
            frontOffset = frontTargetOffset;
        if (rigHomeCaptured)
            transform.localPosition = rigHome - new Vector3(0f, 0f, frontOffset);

        if (disk != null)
            disk.localScale = new Vector3(diskDiameter, disk.localScale.y, diskDiameter);
        if (diskRim != null)
        {
            float rim = diskDiameter + (2f * diskRimExtra);
            diskRim.localScale = new Vector3(rim, diskRim.localScale.y, rim);
        }
    }

    private void UpdateFocus()
    {
        if (units.Count == 0)
            return;

        int index = Mathf.RoundToInt(angle / Step) % units.Count;
        if (index < 0)
            index += units.Count;
        if (index == focusIndex)
            return;

        focusIndex = index;
        if (FocusChanged != null)
            FocusChanged();
    }

    private GameObject BuildSlot(UnitData data, int index)
    {
        // Ring angle 0 is the seat nearest the camera, so a slot reaches the front when the
        // turntable has turned by that slot's own angle.
        float theta = index * Step;
        float radians = theta * Mathf.Deg2Rad;
        // A lone unit stands on the middle of the disk. Left on the ring it would sit off to one
        // side of an otherwise empty turntable, which reads as a layout bug rather than a filter.
        float radius = CurrentRadius;

        Vector3 seat = new(Mathf.Sin(radians) * radius, 0f, -Mathf.Cos(radians) * radius);
        Vector3 outward = radius > 0f ? seat.normalized : Vector3.back;

        GameObject slot = new("Slot_" + data.unitName);
        slot.transform.SetParent(Table, false);
        slot.SetActive(false);
        slot.transform.localPosition = seat;
        // Gameplay aims units with Quaternion.LookRotation, so a unit prefab's model faces +Z at
        // rest by Unity's own convention. Pointing the seat's forward down its outward radius
        // therefore turns the model out of the ring with no per-model fudge, and puts the front
        // seat's face to the camera whatever the turntable is doing.
        slot.transform.localRotation =
            Quaternion.LookRotation(outward, Vector3.up) * Quaternion.Euler(0f, YawFor(data), 0f);

        if (data.unitModel != null)
        {
            GameObject model = Instantiate(data.unitModel, slot.transform);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            StripForDisplay(model);
            slot.SetActive(true);
            StandOnDisk(slot.transform, model.transform);
        }
        else
        {
            slot.SetActive(true);
        }

        return slot;
    }

    /// <summary>
    /// Drops the model so its lowest geometry rests on the turntable plane. Unit prefabs are
    /// pivoted around the middle of the model rather than its feet, and the base plate is the
    /// lowest thing on every one of them, so this seats the plate on the deck.
    /// </summary>
    private static void StandOnDisk(Transform slot, Transform model)
    {
        if (!TryFindLowestPoint(model, out float lowest))
            return;

        // Slots only ever yaw, so a lift in world Y is the same as a lift in the slot's local Y.
        model.localPosition += new Vector3(0f, slot.position.y - lowest, 0f);
    }

    /// <summary>
    /// Lowest point of the model's actual geometry, in world space.
    ///
    /// <see cref="Renderer.bounds"/> is not usable for this. Most of the character meshes are
    /// skinned, and a <see cref="SkinnedMeshRenderer"/> reports padded animation bounds that reach
    /// up to a quarter of a unit below the real mesh — enough to seat a unit's padding on the deck
    /// and leave the unit itself visibly hovering. Reading the vertices costs a few milliseconds
    /// once per rebuild and is exact.
    /// </summary>
    private static bool TryFindLowestPoint(Transform model, out float lowest)
    {
        lowest = float.MaxValue;
        bool found = false;
        Mesh baked = null;

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            Mesh mesh;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                baked ??= new Mesh();
                skinned.BakeMesh(baked, true);
                mesh = baked;
            }
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
            }
            if (mesh == null)
                continue;

            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                float y = toWorld.MultiplyPoint3x4(vertices[i]).y;
                if (y >= lowest)
                    continue;
                lowest = y;
                found = true;
            }
        }

        if (baked != null)
            Destroy(baked);
        return found;
    }

    private float YawFor(UnitData data)
    {
        if (data == null || displayPoses == null)
            return 0f;

        for (int i = 0; i < displayPoses.Length; i++)
        {
            if (displayPoses[i].unit == data)
                return displayPoses[i].yaw;
        }
        return 0f;
    }

    private void ClearSlots()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null)
                Destroy(slots[i]);
        }
        slots.Clear();
    }

    private static void StripForDisplay(GameObject instance)
    {
        foreach (Canvas canvas in instance.GetComponentsInChildren<Canvas>(true))
            canvas.gameObject.SetActive(false);

        foreach (VisionConeVisual cone in instance.GetComponentsInChildren<VisionConeVisual>(true))
            cone.gameObject.SetActive(false);

        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(collider);

        // NetworkObject and every NetworkBehaviour derive from MonoBehaviour, so one sweep clears
        // the authoritative scripts and the networking together. Renderers, filters, and Animators
        // are not MonoBehaviours and survive, which is the whole model minus its behaviour.
        foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null)
                DestroyImmediate(behaviour);
        }
    }

    [Serializable]
    private struct DisplayPose
    {
        public UnitData unit;
        public float yaw;
    }
}
