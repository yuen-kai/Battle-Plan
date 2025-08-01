using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterSelection : MonoBehaviour
{
    public GameObject CharacterSelectionOptionsParent;
    public GameObject characterSelectedParent;

    public UnitData[] unitOptions;
    public GameObject characterOptionPrefab;

    int[] team = new int[3] { -1, -1, -1 };

    void Start()
    {
        InitializeCharacterSelection();
    }

    void InitializeCharacterSelection()
    {
        InitializeOptions();
        InitializeSelected();
    }

    void InitializeOptions()
    {
        // Clear existing options before creating new ones
        for (int i = CharacterSelectionOptionsParent.transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(CharacterSelectionOptionsParent.transform.GetChild(i).gameObject);
        }

        //Create options
        foreach (var unit in unitOptions)
        {
            var option = Instantiate(characterOptionPrefab, CharacterSelectionOptionsParent.transform);

            CardHandler cardHandler = option.GetComponent<CardHandler>();
            cardHandler.setImage(unit.unitSprite);
            cardHandler.setText(unit.unitName);
            cardHandler.setButtonListener(() => SelectUnit(unit));

        }
    }

    void SelectUnit(UnitData unit)
    {
        // Find the first empty slot in the team
        for (int i = 0; i < team.Length; i++)
        {
            if (team[i] == -1) // If the slot is empty
            {
                team[i] = System.Array.IndexOf(unitOptions, unit); // Store the index of the selected unit
                SetSelectedUnit(characterSelectedParent.transform.GetChild(i).gameObject, team[i]);
                return; // Exit after selecting a unit
            }
        }
    }

    void InitializeSelected()
    {
        // Clear existing options before creating new ones
        for (int i = characterSelectedParent.transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(characterSelectedParent.transform.GetChild(i).gameObject);
        }

        //Create options
        for(int i = 0; i < team.Length; i++)
        {
            int unit = team[i];
            GameObject option = Instantiate(characterOptionPrefab, characterSelectedParent.transform);

            SetSelectedUnit(option, unit);
        }
    }

    void SetSelectedUnit(GameObject selectedUnit, int unitIndex)
    {
        if (unitIndex == -1)
        {
            selectedUnit.GetComponent<CardHandler>().setImage(null); // Set to null or a default sprite if no unit is selected
            selectedUnit.GetComponent<CardHandler>().setText("Select Unit");
            return;
        }

        UnitData unit = unitOptions[unitIndex];
        selectedUnit.GetComponent<CardHandler>().setImage(unit.unitSprite);
        selectedUnit.GetComponent<CardHandler>().setText(unit.unitName);
    }
}
