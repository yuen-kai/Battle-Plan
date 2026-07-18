using UnityEngine;

[CreateAssetMenu(fileName = "NewUnitData", menuName = "Units/Unit Data")]
public class UnitData : ScriptableObject
{
    public GameObject unitModel;

    [Header("=== UNIT PARAMETERS ===")]
    [Tooltip("Name of the unit")]
    public string unitName = "New Unit";

    [TextArea(3, 5)]
    [Tooltip("Description of the unit")]
    public string unitDescription = "New Unit Description";

    [Tooltip("Sprite representing the unit")]
    public Sprite unitSprite;

    [Header("=== ABILITY CARD PARAMETERS ===")]
    [Tooltip("Name of the ability")]
    public string abilityName = "New Ability";

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

    [Tooltip("Radius of ability area of effect in cells")]
    public float abilityRadius = 0f;

    [Tooltip("Time allowed for ability target selection in seconds")]
    public float selectTime = 4f;

    [Tooltip("Range in cells that triggers enemy response to ability")]
    public float responseRange = 5f;

    public bool responseDistLine = false;

    [Tooltip("Time per unit for dive movement planning in seconds")]
    public float timeDivePerUnit = 3f;

    [Tooltip("Range in cells for dive movement")]
    public int diveRange = 2;

    [Tooltip("Number of times the ability can be used")]
    public int uses = 1;
}
