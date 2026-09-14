using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The cover bodies are unbroken boxes. They were not always: the container carried seven ribs and
/// the stack a mid-height recess, and at the distance the game is played from those grooves read as
/// stripes across the prop rather than as detail on it. A groove is cheap to add back by accident,
/// and it costs geometry, so the triangle budget is the guard.
/// </summary>
[TestFixture]
public class CoverKitEditModeTests
{
    // A chamfered box is 20. Ribs or a recess cost an order of magnitude more.
    private const int UnbrokenBoxTriBudget = 24;

    private static readonly string[] CoverBodies =
    {
        "Cover_Block",
        "Cover_Container",
        "Cover_Stack",
        "Cover_Pillar",
    };

    [TestCaseSource(nameof(CoverBodies))]
    public void CoverBodiesHaveNoGrooves(string meshName)
    {
        Mesh mesh = Load(meshName);

        Assert.That(
            mesh.triangles.Length / 3,
            Is.LessThanOrEqualTo(UnbrokenBoxTriBudget),
            $"{meshName} carries interior geometry; a cover body is one unbroken box."
        );
    }

    /// <summary>
    /// Every wall cell blocks line of sight by rule and every wall is the same height, so a variant
    /// that is narrower, wider or shorter than the others is a lie about cover.
    /// </summary>
    [TestCaseSource(nameof(CoverBodies))]
    public void CoverBodiesShareOneEnvelope(string meshName)
    {
        Mesh mesh = Load(meshName);

        Assert.That(mesh.bounds.size.x, Is.EqualTo(2.7f).Within(0.001f));
        Assert.That(mesh.bounds.size.z, Is.EqualTo(2.7f).Within(0.001f));
        Assert.That(mesh.bounds.size.y, Is.EqualTo(1.94f).Within(0.001f));
    }

    private static Mesh Load(string meshName)
    {
        string path = $"Assets/Meshes/Arena/{meshName}.asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        Assert.That(mesh, Is.Not.Null, $"Could not load {path}.");
        return mesh;
    }
}
