using NUnit.Framework;
using UnityEngine;

// Structural sanity for the primitive-only placeholder silhouette builder: no scene, network, or
// Play mode needed, since BuildHumanoidSilhouette is a pure data-in/GameObject-out function meant
// to be invoked once at authoring time. These tests can't judge whether the result looks right —
// only that the hierarchy, components, and materials it produces are well-formed.
[TestFixture]
[Category("PlaceholderModel")]
public class PlaceholderModelBuilderEditModeTests
{
    private static readonly string[] ExpectedBodyParts =
    {
        "Torso",
        "Head",
        "LeftUpperArm",
        "RightUpperArm",
        "LeftForearm",
        "RightForearm",
        "LeftLeg",
        "RightLeg",
    };

    private static readonly PlaceholderBodyAnchor[] ExpectedAnchors =
    {
        PlaceholderBodyAnchor.Torso,
        PlaceholderBodyAnchor.Head,
        PlaceholderBodyAnchor.Pelvis,
        PlaceholderBodyAnchor.LeftForearm,
        PlaceholderBodyAnchor.RightForearm,
        PlaceholderBodyAnchor.LeftHand,
        PlaceholderBodyAnchor.RightHand,
    };

    [Test]
    public void BuildHumanoidSilhouette_CreatesBodyAndAnchorGroupsWithWellFormedPrimitives()
    {
        GameObject model = PlaceholderModelBuilder.BuildHumanoidSilhouette(
            "TestSilhouette",
            new PlaceholderModelSpec()
        );
        try
        {
            Assert.That(model, Is.Not.Null);
            Assert.That(
                model.transform.childCount,
                Is.EqualTo(2),
                "Root should only ever contain the Body and Anchors groups; accessories live under Anchors."
            );

            Transform body = model.transform.Find("Body");
            Transform anchorsGroup = model.transform.Find("Anchors");
            Assert.That(body, Is.Not.Null);
            Assert.That(anchorsGroup, Is.Not.Null);
            Assert.That(body.childCount, Is.EqualTo(ExpectedBodyParts.Length));
            Assert.That(anchorsGroup.childCount, Is.EqualTo(ExpectedAnchors.Length));

            foreach (string partName in ExpectedBodyParts)
            {
                Transform part = body.Find(partName);
                Assert.That(part, Is.Not.Null, $"Missing body part '{partName}'.");
                AssertWellFormedVisual(part.gameObject, partName);
                Assert.That(
                    part.GetComponent<Collider>(),
                    Is.Null,
                    $"'{partName}' should not carry a collider — gameplay collision belongs to Unit's own colliders."
                );
            }

            foreach (PlaceholderBodyAnchor anchor in ExpectedAnchors)
            {
                Transform anchorTransform = anchorsGroup.Find(anchor.ToString());
                Assert.That(anchorTransform, Is.Not.Null, $"Missing anchor '{anchor}'.");
                Assert.That(
                    anchorTransform.GetComponent<MeshRenderer>(),
                    Is.Null,
                    "A bare anchor should have no visual of its own."
                );
            }
        }
        finally
        {
            Object.DestroyImmediate(model);
        }
    }

    [Test]
    public void BuildHumanoidSilhouette_AttachesEachAccessoryUnderItsSpecifiedAnchorWithItsOwnColor()
    {
        PlaceholderModelSpec spec = new()
        {
            Accessories =
            {
                new PlaceholderAccessorySpec
                {
                    Name = "ChestPlate",
                    PrimitiveType = PrimitiveType.Cube,
                    Anchor = PlaceholderBodyAnchor.Torso,
                    LocalPosition = new Vector3(0f, 0f, 0.1f),
                    LocalScale = new Vector3(0.3f, 0.4f, 0.1f),
                    Color = Color.red,
                },
                new PlaceholderAccessorySpec
                {
                    Name = "Sidearm",
                    PrimitiveType = PrimitiveType.Cylinder,
                    Anchor = PlaceholderBodyAnchor.RightHand,
                    LocalPosition = new Vector3(0.05f, 0f, 0.1f),
                    LocalScale = new Vector3(0.05f, 0.1f, 0.05f),
                    Color = Color.blue,
                },
            },
        };

        GameObject model = PlaceholderModelBuilder.BuildHumanoidSilhouette("TestAccessorySilhouette", spec);
        try
        {
            Transform anchorsGroup = model.transform.Find("Anchors");
            Assert.That(anchorsGroup, Is.Not.Null);

            Transform torsoAnchor = anchorsGroup.Find(nameof(PlaceholderBodyAnchor.Torso));
            Transform rightHandAnchor = anchorsGroup.Find(nameof(PlaceholderBodyAnchor.RightHand));
            Assert.That(torsoAnchor, Is.Not.Null);
            Assert.That(rightHandAnchor, Is.Not.Null);
            Assert.That(torsoAnchor.childCount, Is.EqualTo(1), "ChestPlate should be the only accessory on Torso.");
            Assert.That(
                rightHandAnchor.childCount,
                Is.EqualTo(1),
                "Sidearm should be the only accessory on RightHand."
            );

            Transform chestPlate = torsoAnchor.Find("ChestPlate");
            Transform sidearm = rightHandAnchor.Find("Sidearm");
            Assert.That(chestPlate, Is.Not.Null);
            Assert.That(sidearm, Is.Not.Null);
            AssertWellFormedVisual(chestPlate.gameObject, "ChestPlate");
            AssertWellFormedVisual(sidearm.gameObject, "Sidearm");

            Assert.That(chestPlate.localPosition, Is.EqualTo(new Vector3(0f, 0f, 0.1f)));
            Assert.That(sidearm.localPosition, Is.EqualTo(new Vector3(0.05f, 0f, 0.1f)));

            MeshRenderer chestRenderer = chestPlate.GetComponent<MeshRenderer>();
            MeshRenderer sidearmRenderer = sidearm.GetComponent<MeshRenderer>();
            Assert.That(
                chestRenderer.sharedMaterial,
                Is.Not.EqualTo(sidearmRenderer.sharedMaterial),
                "Accessories with different colors must not share a material instance."
            );
        }
        finally
        {
            Object.DestroyImmediate(model);
        }
    }

    [Test]
    public void BuildSentinel_ProducesAShieldOnTheLeftForearmAndAPistolInTheRightHand()
    {
        GameObject sentinel = PlaceholderModelBuilder.BuildSentinel();
        try
        {
            Assert.That(sentinel, Is.Not.Null);
            Assert.That(sentinel.name, Is.EqualTo("Sentinel"));

            Transform anchorsGroup = sentinel.transform.Find("Anchors");
            Assert.That(anchorsGroup, Is.Not.Null);

            Transform leftForearmAnchor = anchorsGroup.Find(nameof(PlaceholderBodyAnchor.LeftForearm));
            Transform rightHandAnchor = anchorsGroup.Find(nameof(PlaceholderBodyAnchor.RightHand));
            Assert.That(leftForearmAnchor, Is.Not.Null);
            Assert.That(rightHandAnchor, Is.Not.Null);

            Transform shield = leftForearmAnchor.Find("Shield");
            Transform pistol = rightHandAnchor.Find("Pistol");
            Assert.That(shield, Is.Not.Null, "Sentinel must carry a Shield accessory on its left forearm.");
            Assert.That(pistol, Is.Not.Null, "Sentinel must carry a Pistol accessory in its right hand.");
            AssertWellFormedVisual(shield.gameObject, "Shield");
            AssertWellFormedVisual(pistol.gameObject, "Pistol");
        }
        finally
        {
            Object.DestroyImmediate(sentinel);
        }
    }

    private static void AssertWellFormedVisual(GameObject part, string label)
    {
        MeshFilter meshFilter = part.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = part.GetComponent<MeshRenderer>();
        Assert.That(meshFilter, Is.Not.Null, $"'{label}' is missing a MeshFilter.");
        Assert.That(meshFilter.sharedMesh, Is.Not.Null, $"'{label}' has a MeshFilter with no mesh.");
        Assert.That(meshRenderer, Is.Not.Null, $"'{label}' is missing a MeshRenderer.");
        Assert.That(meshRenderer.sharedMaterial, Is.Not.Null, $"'{label}' has a MeshRenderer with no material.");
    }
}
