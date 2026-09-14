using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Fits the shared UI panel to the device it is running on.
/// <para>
/// A desktop panel is measured against its authored 1920x1080 reference, which on a phone leaves
/// the panel wider than a monitor's and shrinks every control to a fraction of a millimetre. A
/// handheld is therefore measured in density-independent units instead, so the phone and short
/// breakpoints engage and the 44px touch targets they author come out the physical size they were
/// drawn as. Registered screens are also told how much of each edge a display cutout or a home
/// indicator covers, in panel units, so their content can stay clear of it.
/// </para>
/// </summary>
public static class MobileDisplay
{
    public const float DensityIndependentDpi = 160f;

    /// <summary>Used when a handheld does not report its density, near a mid-range phone's.</summary>
    public const float FallbackHandheldScale = 2.5f;

    public const float MinHandheldScale = 1f;
    public const float MaxHandheldScale = 4f;

    /// <summary>
    /// The narrowest panel the phone breakpoint is drawn for, and the reason density is only ever
    /// an opening guess. A web build reports no density at all, so a mobile browser falls back on
    /// an assumed one; clamping the panel itself is what keeps that guess, and any device whose
    /// reported density is wrong, inside a layout that was actually authored.
    /// </summary>
    public const float MinHandheldPanelWidth = 900f;

    private static readonly List<Registration> registrations = new();
    private static readonly Dictionary<PanelSettings, Vector2Int> authoredReferences = new();
    private static Driver driver;
    private static bool restoreHooked;

    public static bool IsHandheldDevice(DeviceType deviceType) =>
        deviceType == DeviceType.Handheld;

    public static float HandheldPanelScale(float screenDpi)
    {
        if (float.IsNaN(screenDpi) || screenDpi <= 1f)
            return FallbackHandheldScale;

        return Mathf.Clamp(
            screenDpi / DensityIndependentDpi,
            MinHandheldScale,
            MaxHandheldScale
        );
    }

    public static Vector2Int PanelReferenceResolution(
        DeviceType deviceType,
        Vector2Int screenPixels,
        float screenDpi,
        Vector2Int authoredReference
    )
    {
        if (
            !IsHandheldDevice(deviceType)
            || screenPixels.x <= 0
            || screenPixels.y <= 0
            || authoredReference.x <= 0
        )
            return authoredReference;

        float width = Mathf.Clamp(
            screenPixels.x / HandheldPanelScale(screenDpi),
            Mathf.Min(MinHandheldPanelWidth, authoredReference.x),
            authoredReference.x
        );
        return new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(width)),
            Mathf.Max(1, Mathf.RoundToInt(width * screenPixels.y / screenPixels.x))
        );
    }

    /// <summary>
    /// Left, top, right and bottom screen pixels that lie outside the safe area. Unity reports the
    /// safe area from the bottom left, while UI insets are read from the top, so the vertical pair
    /// is flipped here rather than at each call site.
    /// </summary>
    public static Vector4 SafeAreaInsetPixels(Rect safeArea, Vector2 screenPixels)
    {
        if (
            screenPixels.x <= 0f
            || screenPixels.y <= 0f
            || safeArea.width <= 0f
            || safeArea.height <= 0f
        )
            return Vector4.zero;

        return new Vector4(
            Mathf.Max(0f, safeArea.xMin),
            Mathf.Max(0f, screenPixels.y - safeArea.yMax),
            Mathf.Max(0f, screenPixels.x - safeArea.xMax),
            Mathf.Max(0f, safeArea.yMin)
        );
    }

    /// <summary>
    /// Scales the panel for this device and pads its screen element clear of any cutout. Screens
    /// whose chrome must keep bleeding to the display edge pass their own inset handler instead.
    /// </summary>
    public static void ConfigureScreen(UIDocument document) => ConfigureScreen(document, null);

    public static void ConfigureScreen(UIDocument document, Action<Vector4> applyPanelInsets)
    {
        if (document == null)
            return;

        Registration registration = Find(document);
        if (registration == null)
        {
            registration = new Registration { Document = document };
            registrations.Add(registration);
        }
        registration.ApplyPanelInsets = applyPanelInsets;

        EnsureDriver();
        Apply(registration);

        // The panel's scale is not known until it has laid out once, so the insets are measured
        // again on the first geometry pass rather than against an unresolved layout.
        document.rootVisualElement?.schedule.Execute(() => Apply(registration));
    }

    public static void ForgetScreen(UIDocument document)
    {
        Registration registration = Find(document);
        if (registration != null)
            registrations.Remove(registration);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        registrations.Clear();
        authoredReferences.Clear();
        driver = null;
        restoreHooked = false;
    }

    private static Registration Find(UIDocument document)
    {
        foreach (Registration registration in registrations)
        {
            if (registration.Document == document)
                return registration;
        }
        return null;
    }

    private static void ApplyAll()
    {
        for (int index = registrations.Count - 1; index >= 0; index--)
        {
            if (registrations[index].Document == null)
                registrations.RemoveAt(index);
            else
                Apply(registrations[index]);
        }
    }

    private static void Apply(Registration registration)
    {
        UIDocument document = registration.Document;
        VisualElement root = document != null ? document.rootVisualElement : null;
        if (root == null)
            return;

        ApplyPanelScale(document.panelSettings);

        Vector4 insets = PanelInsets(root.panel);
        if (registration.ApplyPanelInsets != null)
            registration.ApplyPanelInsets(insets);
        else
            ApplyScreenPadding(root, insets);
    }

    private static void ApplyPanelScale(PanelSettings panelSettings)
    {
        if (panelSettings == null)
            return;

        if (!authoredReferences.TryGetValue(panelSettings, out Vector2Int authored))
        {
            authored = panelSettings.referenceResolution;
            authoredReferences[panelSettings] = authored;
            HookAuthoredReferenceRestore();
        }

        Vector2Int wanted = PanelReferenceResolution(
            SystemInfo.deviceType,
            new Vector2Int(Screen.width, Screen.height),
            Screen.dpi,
            authored
        );
        if (panelSettings.referenceResolution != wanted)
            panelSettings.referenceResolution = wanted;
    }

    private static Vector4 PanelInsets(IPanel panel)
    {
        Vector2 screenPixels = new(Screen.width, Screen.height);
        Vector4 pixels = SafeAreaInsetPixels(Screen.safeArea, screenPixels);
        if (panel == null || pixels == Vector4.zero)
            return Vector4.zero;

        float panelWidth = panel.visualTree.layout.width;
        if (float.IsNaN(panelWidth) || panelWidth <= 0f)
            return Vector4.zero;

        return pixels / (screenPixels.x / panelWidth);
    }

    /// <summary>
    /// Pads the screen element rather than the document root. A background paints a padding box, so
    /// the screen's opaque ground still reaches the display edge while its content moves inside the
    /// cutout; padding the root would pull that ground inwards and leave a band the camera behind
    /// it does not necessarily clear.
    /// </summary>
    private static void ApplyScreenPadding(VisualElement root, Vector4 insets)
    {
        VisualElement screen = root.Q("screen") ?? root;
        screen.style.paddingLeft = insets.x;
        screen.style.paddingTop = insets.y;
        screen.style.paddingRight = insets.z;
        screen.style.paddingBottom = insets.w;
    }

    /// <summary>
    /// The reference resolution lives on a shared asset, so a handheld run inside the Editor's
    /// device simulator would otherwise leave its phone-sized value written into the project.
    /// </summary>
    private static void HookAuthoredReferenceRestore()
    {
        if (restoreHooked)
            return;

        restoreHooked = true;
        Application.quitting += RestoreAuthoredReferences;
    }

    private static void RestoreAuthoredReferences()
    {
        Application.quitting -= RestoreAuthoredReferences;
        restoreHooked = false;
        foreach (KeyValuePair<PanelSettings, Vector2Int> entry in authoredReferences)
        {
            if (entry.Key != null)
                entry.Key.referenceResolution = entry.Value;
        }
        authoredReferences.Clear();
    }

    private static void EnsureDriver()
    {
        if (driver != null || !Application.isPlaying)
            return;

        GameObject host = new("MobileDisplayDriver") { hideFlags = HideFlags.HideAndDontSave };
        driver = host.AddComponent<Driver>();
    }

    private sealed class Registration
    {
        public UIDocument Document;
        public Action<Vector4> ApplyPanelInsets;
    }

    /// <summary>Re-fits every registered screen when the display resizes or rotates.</summary>
    private sealed class Driver : MonoBehaviour
    {
        private Vector2Int screenPixels;
        private Rect safeArea;
        private ScreenOrientation orientation;

        private void Update()
        {
            Vector2Int pixels = new(Screen.width, Screen.height);
            if (
                pixels == screenPixels
                && Screen.safeArea == safeArea
                && Screen.orientation == orientation
            )
                return;

            screenPixels = pixels;
            safeArea = Screen.safeArea;
            orientation = Screen.orientation;
            ApplyAll();
        }
    }
}
