using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UnitCard : NetworkBehaviour
{
    public int uses;
    public bool disabled = false;

    public void SetUnitCard(Sprite abilityCardSprite, string abilityName)
    {
        transform.Find("TouchArea").Find("UnitImage").GetComponent<Image>().sprite = abilityCardSprite;
        transform.Find("TouchArea").Find("AbilityText").GetComponent<TMP_Text>().text = abilityName;
    }

    public void setUnitCardInteractable(bool interactable)
    {
        transform.Find("TouchArea").GetComponent<Button>().interactable = uses > 0 && !disabled && interactable;
    }

    public void OnAbilityPressed()
    {
        PlanMovement.Instance.SwitchToUnit(transform.GetSiblingIndex());
    }
}
