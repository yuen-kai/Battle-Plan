using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

[TestFixture]
[Category("TripleVolley")]
public class TripleVolleyEditModeTests
{
    private const float Tolerance = 0.001f;
    private readonly List<GameObject> spawnedObjects = new();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject spawned in spawnedObjects)
        {
            if (spawned != null)
                Object.DestroyImmediate(spawned);
        }
        spawnedObjects.Clear();
    }

    [Test]
    public void FanSpreadDegrees_MatchesTheDesignersThreeArrowFan()
    {
        Assert.That(
            GetPrivateStaticConst<float>("FanSpreadDegrees"),
            Is.EqualTo(12f).Within(Tolerance)
        );
    }

    [Test]
    public void ComputeFanDirections_ReturnsTheBaseDirectionAndBothFanEdges()
    {
        Vector3 baseDirection = Vector3.forward;

        Vector3[] fan = TripleVolley.ComputeFanDirections(baseDirection);

        Assert.That(fan.Length, Is.EqualTo(3));

        Vector3 expectedLeft = Quaternion.AngleAxis(-12f, Vector3.up) * baseDirection;
        Vector3 expectedRight = Quaternion.AngleAxis(12f, Vector3.up) * baseDirection;

        Assert.That(
            fan[0],
            Is.EqualTo(expectedLeft).Using(Vector3EqualityComparer.Instance),
            "The first shot must be the left edge of the fan."
        );
        Assert.That(
            fan[1],
            Is.EqualTo(baseDirection).Using(Vector3EqualityComparer.Instance),
            "The middle shot must be the base direction, unmodified."
        );
        Assert.That(
            fan[2],
            Is.EqualTo(expectedRight).Using(Vector3EqualityComparer.Instance),
            "The last shot must be the right edge of the fan."
        );

        foreach (Vector3 direction in fan)
        {
            Assert.That(
                direction.y,
                Is.EqualTo(0f).Within(Tolerance),
                "Every arrow in the fan must stay horizontal."
            );
        }
    }

    [Test]
    public void ComputeFanDirections_NormalizesAnArbitraryLengthBaseDirection()
    {
        Vector3[] fan = TripleVolley.ComputeFanDirections(new Vector3(5f, 0f, 0f));

        foreach (Vector3 direction in fan)
        {
            Assert.That(
                direction.magnitude,
                Is.EqualTo(1f).Within(Tolerance),
                "A longer-than-unit base direction must not change the fan's shot speed/length."
            );
        }
    }

    [Test]
    public void ComputeFanDirections_IsAllZeroesForAZeroBaseDirection()
    {
        Vector3[] fan = TripleVolley.ComputeFanDirections(Vector3.zero);

        Assert.That(fan.Length, Is.EqualTo(3));
        foreach (Vector3 direction in fan)
        {
            Assert.That(direction, Is.EqualTo(Vector3.zero), "A direction-less cast has no fan to aim.");
        }
    }

    [Test]
    public void ExecuteAbility_AbortsWithoutFiringWhenTheTargetSquareIsNotAdjacent()
    {
        GameObject caster = new("TripleVolleyCaster");
        spawnedObjects.Add(caster);
        Movement movement = caster.AddComponent<Movement>();
        Shooting shooting = caster.AddComponent<Shooting>();
        TripleVolley ability = caster.AddComponent<TripleVolley>();

        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            movement.unitData = data;
            shooting.unitData = data;

            Vector2Int casterCell = new(3, 3);
            caster.transform.position =
                GameLoop.gridCoordToWorld(casterCell) + Helper.heightOffset(caster.transform);

            SetIsServer(ability, true);

            var routine = ability.ExecuteAbility(GameLoop.gridCoordToWorld(new Vector2Int(6, 3)), 0f);

            LogAssert.Expect(
                LogType.Warning,
                new Regex(".*TripleVolleyCaster.*could not resolve a volley direction.*")
            );

            bool hasMoreSteps = routine.MoveNext();

            Assert.That(
                hasMoreSteps,
                Is.False,
                "A non-adjacent target must abort the whole coroutine in a single step -- there is "
                    + "no partial fan to fire."
            );
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    private static T GetPrivateStaticConst<T>(string fieldName)
    {
        FieldInfo field = typeof(TripleVolley).GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, $"Missing private const {fieldName}.");
        return (T)field.GetValue(null);
    }

    private static void SetIsServer(NetworkBehaviour behaviour, bool value)
    {
        PropertyInfo property = typeof(NetworkBehaviour).GetProperty(
            "IsServer",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        Assert.That(property, Is.Not.Null, "Missing NetworkBehaviour.IsServer property.");
        property.SetValue(behaviour, value);
    }
}
