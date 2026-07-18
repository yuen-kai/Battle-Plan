using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class Unit : NetworkBehaviour
{
    public List<Material> teamMaterials;

    [HideInInspector]
    public bool selectMovement = true;

    public override void OnNetworkSpawn()
    {
        SetTeamIndicators();
        SetupVisionCone();
    }

    void SetupVisionCone()
    {
        VisionConeVisual cone = GetComponentInChildren<VisionConeVisual>(true);
        if (cone == null)
            return;

        // Raid-style tactical flashlights: friendly cones are neutral white with only a hint of
        // cool tint; enemy cones remain a dim red warning. Fog hides enemy cones with the unit.
        // Alphas stay low: cones are additive and overlap, so they sum up fast.
        cone.SetColor(
            IsOwner ? new Color(0.92f, 0.95f, 1f, 0.15f) : new Color(1f, 0.3f, 0.32f, 0.08f)
        );

        // The flashlight is the weapon envelope, not fog vision:
        // bulletSpread is one side of center, while VisionConeVisual expects the full angle.
        // targetRange is stored in cells, so convert it to world distance.
        UnitData unitData =
            GetComponent<Shooting>()?.unitData ?? GetComponent<Movement>()?.unitData;
        if (unitData != null)
        {
            cone.SetWeaponEnvelope(
                unitData.bulletSpread,
                unitData.targetRange * GameLoop.cellSize
            );
        }
    }

    void SetTeamIndicators()
    {
        GameObject[] teamIndicators = GetComponentsInChildren<Transform>()
            .Where(t => t.CompareTag("TeamIndicatorProp"))
            .Select(t => t.gameObject)
            .ToArray();

        foreach (GameObject indicator in teamIndicators)
        {
            Renderer renderer = indicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.materials = new Material[] { IsOwner ? teamMaterials[0] : teamMaterials[1] };
            }
        }

    }
}