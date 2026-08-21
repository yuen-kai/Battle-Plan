using System.Linq;
using NUnit.Framework;
using UnityEngine;

// ModelHeadshotRenderer needs a live camera render and pixel readback, which edit-mode tests can
// do directly -- no Play mode required, since Camera.Render()/ReadPixels work outside Play mode.
// These tests use a small hand-built two-primitive stand-in rather than the full
// PlaceholderModelBuilder silhouette: the renderer only ever looks at Renderer bounds, so it does
// not care what built the model, and a bare cube+sphere is enough to prove the render, crop and
// cleanup contract.
[TestFixture]
[Category("ModelHeadshotRenderer")]
public class ModelHeadshotRendererEditModeTests
{
    private GameObject testModel;

    [SetUp]
    public void SetUp()
    {
        testModel = new GameObject("HeadshotTestModel");

        // Torso: y in [0, 1]. Head: y in [1.0, 1.4]. Overall bounds height 1.4, so the default 40%
        // top crop (y in [0.84, 1.4]) captures the whole head plus a sliver of torso -- a bust shot.
        GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
        torso.name = "Torso";
        torso.transform.SetParent(testModel.transform, false);
        torso.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        torso.transform.localScale = new Vector3(0.5f, 1f, 0.3f);
        torso.GetComponent<Renderer>().sharedMaterial = BuildColoredMaterial(Color.red);
        Object.DestroyImmediate(torso.GetComponent<Collider>());

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(testModel.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        head.transform.localScale = Vector3.one * 0.4f;
        head.GetComponent<Renderer>().sharedMaterial = BuildColoredMaterial(Color.yellow);
        Object.DestroyImmediate(head.GetComponent<Collider>());
    }

    [TearDown]
    public void TearDown()
    {
        if (testModel != null)
            Object.DestroyImmediate(testModel);
        testModel = null;
    }

    [Test]
    public void RenderHeadshot_ReturnsATextureAtTheRequestedResolutionWithVisibleContentOverATransparentBackground()
    {
        Texture2D headshot = null;
        try
        {
            headshot = ModelHeadshotRenderer.RenderHeadshot(testModel, 128);

            Assert.That(headshot, Is.Not.Null);
            Assert.That(headshot.width, Is.EqualTo(128));
            Assert.That(headshot.height, Is.EqualTo(128));
            Assert.That(headshot.format, Is.EqualTo(TextureFormat.RGBA32));

            AssertSomethingWasActuallyDrawn(headshot);
        }
        finally
        {
            if (headshot != null)
                Object.DestroyImmediate(headshot);
        }
    }

    [Test]
    public void RenderHeadshot_LeavesNoStrayGameObjectsInTheActiveSceneAfterReturning()
    {
        int[] objectIdsBefore = AllGameObjectIdsSorted();

        Texture2D headshot = null;
        try
        {
            headshot = ModelHeadshotRenderer.RenderHeadshot(testModel, 64);
        }
        finally
        {
            if (headshot != null)
                Object.DestroyImmediate(headshot);
        }

        int[] objectIdsAfter = AllGameObjectIdsSorted();

        Assert.That(
            objectIdsAfter,
            Is.EqualTo(objectIdsBefore),
            "RenderHeadshot must destroy every temporary model instance, camera and light it creates "
                + "before returning -- the active scene's GameObjects must be exactly what they were before the call."
        );
    }

    /// <summary>
    /// Corner pixels (outside the model entirely) must be fully transparent background, and at
    /// least one sampled interior pixel must differ from that background -- i.e. the render is not
    /// uniformly one flat color/alpha, so something from the model actually landed in the frame.
    /// </summary>
    private static void AssertSomethingWasActuallyDrawn(Texture2D headshot)
    {
        Color32 topLeft = headshot.GetPixel(2, headshot.height - 3);
        Assert.That(topLeft.a, Is.EqualTo(0), "A far corner of the headshot should be untouched transparent background.");

        bool foundNonBackgroundPixel = false;
        int step = Mathf.Max(1, headshot.width / 16);
        for (int y = 0; y < headshot.height && !foundNonBackgroundPixel; y += step)
        {
            for (int x = 0; x < headshot.width; x += step)
            {
                Color32 pixel = headshot.GetPixel(x, y);
                if (pixel.a != 0)
                {
                    foundNonBackgroundPixel = true;
                    break;
                }
            }
        }

        Assert.That(
            foundNonBackgroundPixel,
            Is.True,
            "Expected at least one non-transparent pixel from the model somewhere in the frame."
        );
    }

    private static int[] AllGameObjectIdsSorted()
    {
        return Object
            .FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(gameObject => gameObject.GetInstanceID())
            .OrderBy(id => id)
            .ToArray();
    }

    private static Material BuildColoredMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new(shader);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        return material;
    }
}
