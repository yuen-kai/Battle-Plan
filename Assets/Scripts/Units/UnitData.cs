using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Role bucket a unit belongs to. Presentation only — nothing in combat reads it. The names and
/// the order both come from the roster table in Overview.md, which is the source of truth for
/// roles; a class grouping that disagreed with the design doc would just be a second one.
/// </summary>
public enum UnitClass
{
    Assault = 0,
    Initiator = 1,
    Controller = 2,
    Mobility = 3,
    Support = 4,
}

[CreateAssetMenu(fileName = "NewUnitData", menuName = "Units/Unit Data")]
public class UnitData : ScriptableObject
{
    public GameObject unitModel;

    [Header("=== UNIT PARAMETERS ===")]
    [Tooltip("Name of the unit")]
    public string unitName = "New Unit";

    [Tooltip("Role bucket used to group the roster in menus")]
    public UnitClass unitClass = UnitClass.Assault;

    [TextArea(3, 5)]
    [Tooltip("Description of the unit")]
    public string unitDescription = "New Unit Description";

    [TextArea(2, 3)]
    [Tooltip("How this unit is played, in one sentence")]
    public string playStyle = "";

    [Tooltip("Weapon in plain words, e.g. \"Mid-range rifle\"")]
    public string weaponName = "";

    [Tooltip("What this unit is good at, in one line")]
    public string strengths = "";

    [Tooltip("What this unit struggles against, in one line")]
    public string weaknesses = "";

    [Tooltip("Sprite representing the unit")]
    public Sprite unitSprite;

    [Header("=== ROSTER PARAMETERS ===")]
    [SerializeField]
    [Tooltip("Opt out of player-selectable and externally configured crews")]
    private bool unavailableForRoster = false;

    public bool IsRosterEligible => !unavailableForRoster;

    [Header("=== ABILITY CARD PARAMETERS ===")]
    [Tooltip("Name of the ability")]
    public string abilityName = "New Ability";

    [TextArea(2, 3)]
    [Tooltip("What the ability does, in one plain sentence")]
    public string abilityDescription = "";

    [Tooltip("Sprite representing the ability")]
    public Sprite abilitySprite;

    [Header("=== MOVEMENT PARAMETERS ===")]
    [Tooltip("Maximum distance the unit can move in cells")]
    public int moveDist = 3;

    [Tooltip("Movement speed in cells per second")]
    public float moveSpeed = 1f;

    [Tooltip("Dive movement speed in cells per second")]
    public float diveSpeed = 4f;

    [Tooltip("Rotation speed in degrees per second")]
    public float rotationSpeed = 360f;

    [Header("=== SHOOTING PARAMETERS ===")]
    [Tooltip("Prefab for blue team bullets")]
    public GameObject blueBulletPrefab;

    [Tooltip("Prefab for red team bullets")]
    public GameObject redBulletPrefab;

    [Tooltip("Time between individual shots in seconds")]
    public float timeBetweenShots = 0.3f;

    [Tooltip("Number of bullets before needing to reload")]
    public int magazineSize = 10;

    [Tooltip("Time required to reload in seconds")]
    public float reloadTime = 2f;

    [Tooltip("Bullet spread angle in degrees (one side of center)")]
    public float bulletSpread = 3f;

    [Tooltip(
        "Shots in an uninterrupted firing run before the cadence reaches timeBetweenShots. 0 disables the ramp."
    )]
    public int fireRateRampShots = 0;

    [Tooltip(
        "Delay after the first shot of a firing run when fireRateRampShots > 0; unused otherwise."
    )]
    public float fireRateRampStartDelay = 0f;

    [Tooltip("Time without a shot before the fire-rate ramp starts over.")]
    public float fireRateRampResetDelay = 1.5f;

    [Tooltip("Unit may fire immediately while moving instead of waiting for movement to finish")]
    public bool canShootWhileMoving = false;

    [Header("=== COMBAT PARAMETERS ===")]
    [Tooltip("Bullet travel speed in cells per second")]
    public float bulletSpeed = 3f;

    [Tooltip("Range to detect and target enemies in cells")]
    public float targetRange = 5f;

    [Tooltip("Maximum bullet travel distance in cells")]
    public float bulletRange = 7f;

    [Tooltip("Base damage per bullet")]
    public int damage = 10;

    [Tooltip("Damage multiplier when attacking from behind")]
    public float backstabMultiplier = 1f;

    [Tooltip("Minimum angle in degrees from forward direction to consider a backstab")]
    public float backstabAngle = 90f;

    [Tooltip("Bullet explodes in a radius on impact (enemy or wall) instead of single-target only")]
    public bool bulletExplodesOnImpact = false;

    [Tooltip("Splash radius in cells when bulletExplodesOnImpact is set")]
    public float bulletAoeRadius = 0f;

    [Tooltip("Bullet keeps flying through enemies instead of stopping on the first one hit")]
    public bool bulletPierces = false;

    [Header("=== TARGETING PARAMETERS ===")]
    [Tooltip("DISABLED: Time to find and acquire a target")]
    public float findTargetTime = 0f;

    [Tooltip("Time to lock onto target before firing")]
    public float targetLockDuration = 0f;

    [Tooltip("Rotation speed in degrees per second")]
    public float aimRotationSpeed = 270f;

    [Header("=== VISION PARAMETERS ===")]
    [Tooltip("Manhattan vision range in cells; must be at least targetRange")]
    public int visionRange = 5;

    [Header("=== HEALTH PARAMETERS ===")]
    [Tooltip("Maximum health points for this unit")]
    public float maxHealth = 100f;

    [Header("=== ABILITY PARAMETERS ===")]
    [Tooltip("Sprite for this unit's ability card")]
    public Sprite abilityCardSprite;

    public bool selectAbilitySquare = true;

    [Tooltip("Range in cells for ability target selection")]
    public int abilitySquareRange = 3;

    [Tooltip("Restrict ability targeting to the eight adjacent compass directions")]
    public bool selectAbilityDirection = false;

    [Tooltip("Fixed travel distance in cells for a directional ability")]
    public int abilityFixedDistance = 0;

    [Tooltip("Radius of ability area of effect in cells")]
    public float abilityRadius = 0f;

    [Tooltip("Time allowed for ability target selection in seconds")]
    public float selectTime = 4f;

    [Tooltip("Range in cells that triggers enemy response to ability")]
    public float responseRange = 5f;

    public bool responseDistLine = false;

    /// <summary>
    /// Whether the ability may be aimed at the cell its caster is standing on. A line ability fires
    /// from the caster through the chosen square, so its own cell names no direction to fire in.
    /// Everything else lands on its square, and landing on its own is a fair play — smoke dropped
    /// underfoot is one of the Commander's better ones.
    /// </summary>
    public bool CanTargetOwnCell => !responseDistLine;

    [Tooltip("Time per unit for dive movement planning in seconds")]
    public float timeDivePerUnit = 3f;

    [Tooltip("Range in cells for dive movement")]
    public int diveRange = 2;

    [FormerlySerializedAs("uses")]
    [Min(1)]
    [Tooltip("Full rounds the ability remains unavailable after activation")]
    public int abilityCooldownRounds = 1;
}
