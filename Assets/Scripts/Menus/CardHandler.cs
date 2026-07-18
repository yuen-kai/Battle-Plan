using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CardHandler : MonoBehaviour
{
    // Planning palette: yellow marks the selected unit's current mode.
    public static readonly Color NormalColor = new(0.145f, 0.15f, 0.16f, 1f);
    public static readonly Color ArmedColor = new(0.92f, 0.62f, 0.12f, 1f);
    private static readonly Color InactiveModeColor = new(0.075f, 0.08f, 0.09f, 1f);
    private static readonly Color QueuedModeColor = new(0.18f, 0.185f, 0.19f, 1f);
    private static readonly Color SelectedCardColor = new(0.11f, 0.115f, 0.125f, 1f);
    private static readonly Color DefaultCardColor = new(0.075f, 0.08f, 0.09f, 0.98f);

    public int uses = 1;
    public bool disabled = false;
    private bool hasAbility;

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

    public void SetCardColor(Color color)
    {
        transform.Find("TouchArea").GetComponent<Image>().color = color;
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
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
        else
        {
            Debug.LogWarning("No Button component found on this GameObject.");
        }
    }

    public void ConfigurePlanningControls(
        UnityEngine.Events.UnityAction selectAction,
        UnityEngine.Events.UnityAction moveAction,
        UnityEngine.Events.UnityAction abilityAction,
        bool abilityAvailable
    )
    {
        hasAbility = abilityAvailable;
        setButtonListener(selectAction);

        Button moveButton = transform.Find("TouchArea/ModeBar/MoveTab")?.GetComponent<Button>();
        Button abilityButton = transform
            .Find("TouchArea/ModeBar/AbilityTab")
            ?.GetComponent<Button>();

        if (moveButton != null)
        {
            moveButton.onClick.RemoveAllListeners();
            moveButton.onClick.AddListener(moveAction);
        }

        if (abilityButton != null)
        {
            abilityButton.onClick.RemoveAllListeners();
            abilityButton.onClick.AddListener(abilityAction);
            abilityButton.interactable = hasAbility;
        }

        TMP_Text abilityLabel = transform
            .Find("TouchArea/ModeBar/AbilityTab/Label")
            ?.GetComponent<TMP_Text>();
        if (abilityLabel != null)
            abilityLabel.text = hasAbility ? "ABILITY" : "N/A";

        SetPlanningState(false, false);
    }

    public void SetPlanningState(bool selected, bool abilityMode)
    {
        Image cardImage = GetComponent<Image>();
        Image touchImage = transform.Find("TouchArea")?.GetComponent<Image>();
        Image moveImage = transform.Find("TouchArea/ModeBar/MoveTab")?.GetComponent<Image>();
        Image abilityImage = transform.Find("TouchArea/ModeBar/AbilityTab")?.GetComponent<Image>();
        TMP_Text moveLabel = transform
            .Find("TouchArea/ModeBar/MoveTab/Label")
            ?.GetComponent<TMP_Text>();
        TMP_Text abilityLabel = transform
            .Find("TouchArea/ModeBar/AbilityTab/Label")
            ?.GetComponent<TMP_Text>();
        TMP_Text modeHint = transform.Find("TouchArea/ModeHint")?.GetComponent<TMP_Text>();
        Transform selectedIndicator = transform.Find("TouchArea/SelectedIndicator");

        if (cardImage != null)
            cardImage.color = selected ? SelectedCardColor : DefaultCardColor;
        if (touchImage != null)
            touchImage.color = NormalColor;
        if (selectedIndicator != null)
            selectedIndicator.gameObject.SetActive(selected);
        if (modeHint != null)
        {
            modeHint.gameObject.SetActive(selected);
            modeHint.text = abilityMode ? "ABILITY MODE" : "MOVE MODE";
        }

        bool moveChosen = !abilityMode;
        bool abilityChosen = abilityMode && hasAbility;

        if (moveImage != null)
            moveImage.color = moveChosen
                ? (selected ? ArmedColor : QueuedModeColor)
                : InactiveModeColor;
        if (abilityImage != null)
        {
            abilityImage.color = abilityChosen
                ? (selected ? ArmedColor : QueuedModeColor)
                : (hasAbility ? InactiveModeColor : new Color(0.055f, 0.058f, 0.065f, 1f));
        }

        Color activeText = new(0.08f, 0.07f, 0.045f, 1f);
        Color chosenText = new(0.88f, 0.89f, 0.88f, 1f);
        Color inactiveText = new(0.52f, 0.53f, 0.54f, 1f);
        Color disabledText = new(0.38f, 0.39f, 0.4f, 1f);
        if (moveLabel != null)
            moveLabel.color = moveChosen ? (selected ? activeText : chosenText) : inactiveText;
        if (abilityLabel != null)
            abilityLabel.color = abilityChosen
                ? (selected ? activeText : chosenText)
                : (hasAbility ? inactiveText : disabledText);
    }

    public void setUnitCardInteractable(bool interactable)
    {
        bool cardEnabled = uses > 0 && !disabled && interactable;
        Button mainButton = transform.Find("TouchArea")?.GetComponent<Button>();
        Button moveButton = transform.Find("TouchArea/ModeBar/MoveTab")?.GetComponent<Button>();
        Button abilityButton = transform
            .Find("TouchArea/ModeBar/AbilityTab")
            ?.GetComponent<Button>();

        if (mainButton != null)
            mainButton.interactable = cardEnabled;
        if (moveButton != null)
            moveButton.interactable = cardEnabled;
        if (abilityButton != null)
            abilityButton.interactable = cardEnabled && hasAbility;
    }
}
