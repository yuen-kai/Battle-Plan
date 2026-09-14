using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders every unit prefab twice — once at eye level and once at the board camera's 73 degrees —
/// into a single sheet written outside Assets, so a change to <see cref="CharacterBuilder"/> can be
/// looked at rather than reasoned about. Review only: it writes nothing the game loads.
/// </summary>
public static class CharacterContactSheet
{
    private const string OutputFolder = "Temp/CharacterShots/";
    private const int CellWidth = 340;
    private const int CellHeight = 480;
    private const float BoardPitch = 73f;
    private const float EyePitch = 10f;

    private static readonly Vector3 IsolatedOrigin = new(0f, -8000f, 0f);

    /// <summary>
    /// Four to a sheet, which is as many as stay legible at a cell this size. The hand-authored
    /// units get a sheet of their own because the question a generated character has to answer is
    /// not "is this good" but "does this belong next to those".
    /// </summary>
    private static readonly (string Name, string[] Units)[] Sheets =
    {
        ("BuiltA", new[] { "Sentinel", "Breach", "Farsight", "Outrider" }),
        ("BuiltB", new[] { "Salvo", "Voltaic", "Blitz", "President" }),
        ("HandMade", new[] { "Soldier", "Commander", "Sniper", "Ramrod" }),
    };

    [MenuItem("Battle Plan/Art/Character Contact Sheet", false, 14)]
    public static void Render()
    {
        Directory.CreateDirectory(OutputFolder);
        foreach ((string name, string[] units) in Sheets)
            Sheet(name, units);
        Debug.Log($"[Characters] Contact sheets written to {OutputFolder}.");
    }

    private static void Sheet(string name, string[] units)
    {
        Texture2D sheet = new(CellWidth * units.Length, CellHeight * 2, TextureFormat.RGBA32, false);

        for (int i = 0; i < units.Length; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Units/{units[i]}.prefab");
            if (prefab == null)
                continue;

            Blit(sheet, Shoot(prefab, EyePitch, 22f), i * CellWidth, CellHeight);
            Blit(sheet, Shoot(prefab, BoardPitch, 28f), i * CellWidth, 0);
        }

        sheet.Apply();
        File.WriteAllBytes(OutputFolder + name + ".png", ImageConversion.EncodeToPNG(sheet));
        Object.DestroyImmediate(sheet);
    }

    private static Texture2D Shoot(GameObject prefab, float pitch, float yaw)
    {
        GameObject instance = null;
        GameObject rig = null;
        RenderTexture target = null;
        RenderTexture previous = RenderTexture.active;

        try
        {
            instance = Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(IsolatedOrigin, Quaternion.identity);

            // Team surfaces are authored neutral and only become a colour when Unit.SetTeamIndicators
            // swaps them at spawn. Reviewing them grey is reviewing something the player never sees:
            // these are the most saturated things on a unit in play, so they are stood in for here.
            Material team = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Teams/TeamBlueGlow.mat");
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.Contains("Canvas") || child.name.Contains("VisionCone"))
                    child.gameObject.SetActive(false);
                else if (team != null && child.CompareTag("TeamIndicatorProp"))
                {
                    Renderer tagged = child.GetComponent<Renderer>();
                    if (tagged != null)
                        tagged.sharedMaterials = new[] { team };
                }
            }

            Bounds bounds = Bound(instance);
            rig = new GameObject("ContactSheetRig") { hideFlags = HideFlags.HideAndDontSave };

            float size = Mathf.Max(bounds.size.y, bounds.size.x, bounds.size.z) * 0.62f;
            Quaternion orientation = Quaternion.Euler(pitch, 180f + yaw, 0f);
            Vector3 eye = bounds.center - orientation * Vector3.forward * (size * 6f);

            GameObject cameraObject = new("ContactSheetCamera") { hideFlags = HideFlags.HideAndDontSave };
            cameraObject.transform.SetParent(rig.transform, false);
            cameraObject.transform.SetPositionAndRotation(eye, orientation);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = size;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = size * 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.66f, 0.70f, 0.72f, 1f);
            camera.allowHDR = false;
            camera.allowMSAA = false;

            UniversalAdditionalCameraData data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;

            Light(rig.transform, bounds.center, Quaternion.Euler(52f, 90f, 0f), 1.35f);
            Light(rig.transform, bounds.center, Quaternion.Euler(18f, -120f, 0f), 0.55f);

            target = new RenderTexture(CellWidth, CellHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            target.Create();
            camera.targetTexture = target;
            camera.ResetAspect();
            camera.Render();

            RenderTexture.active = target;
            Texture2D shot = new(CellWidth, CellHeight, TextureFormat.RGBA32, false);
            shot.ReadPixels(new Rect(0f, 0f, CellWidth, CellHeight), 0, 0, false);
            shot.Apply(false);
            camera.targetTexture = null;
            return shot;
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
            if (rig != null)
                Object.DestroyImmediate(rig);
            if (instance != null)
                Object.DestroyImmediate(instance);
        }
    }

    private static void Light(Transform rig, Vector3 at, Quaternion rotation, float intensity)
    {
        GameObject light = new("ContactSheetLight") { hideFlags = HideFlags.HideAndDontSave };
        light.transform.SetParent(rig, false);
        light.transform.SetPositionAndRotation(at, rotation);
        Light component = light.AddComponent<Light>();
        component.type = LightType.Directional;
        component.intensity = intensity;
        component.shadows = LightShadows.None;
    }

    private static Bounds Bound(GameObject root)
    {
        List<Renderer> renderers = new();
        foreach (Renderer candidate in root.GetComponentsInChildren<Renderer>(true))
        {
            if (candidate is MeshRenderer && candidate.gameObject.activeInHierarchy)
                renderers.Add(candidate);
        }

        if (renderers.Count == 0)
            return new Bounds(root.transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void Blit(Texture2D sheet, Texture2D cell, int x, int y)
    {
        sheet.SetPixels(x, y, CellWidth, CellHeight, cell.GetPixels());
        Object.DestroyImmediate(cell);
    }
}
