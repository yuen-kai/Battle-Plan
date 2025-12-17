using Unity.Netcode;
using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;

public class UnitCard : NetworkBehaviour
{
    public int uses;

    public void SetUnitCard(Sprite abilityCardSprite, string abilityName)
    {
        transform.Find("TouchArea").Find("UnitImage").GetComponent<Image>().sprite = abilityCardSprite;
        transform.Find("TouchArea").Find("AbilityText").GetComponent<TMP_Text>().text = abilityName;
    }

    public void setUnitCardInteractable(bool interactable)
    {
        bool unavailable = uses <= 0; //|| unit dead
        transform.Find("TouchArea").GetComponent<UnityEngine.UI.Button>().interactable = !unavailable && interactable;
    }

    public void OnAbilityPressed()
    {
        PlanMovement.Instance.SwitchToUnit(transform.GetSiblingIndex());
    }
}
