using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CardHandler : MonoBehaviour
{
    public int uses = 1;
    public bool disabled = false;
    public void setImage(Sprite sprite)
    {
        var image = transform.Find("TouchArea").Find("UnitImage").GetComponent<Image>();
        if (image != null)
        {
            image.sprite = sprite;
        }
        else
        {
            Debug.LogWarning("No Image found on this GameObject.");
        }
    }

    public void setText(string text)
    {
        var textComponent = transform.Find("TouchArea").Find("Text").GetComponent<TMP_Text>();
        if (textComponent != null)
        {
            textComponent.text = text;
        }
        else
        {
            Debug.LogWarning("No Text component found on this GameObject.");
        }
    }

    public void setButtonListener(UnityEngine.Events.UnityAction action)
    {
        var button = transform.Find("TouchArea").GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(action);
        }
        else
        {
            Debug.LogWarning("No Button component found on this GameObject.");
        }
    }

     public void setUnitCardInteractable(bool interactable)
    {
        transform.Find("TouchArea").GetComponent<Button>().interactable = uses > 0 && !disabled && interactable;
    }
}
