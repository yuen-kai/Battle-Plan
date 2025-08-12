using TMPro;
using Unity.Netcode;
using UnityEngine;

public class JoinGameUIHandler : MonoBehaviour
{
    public GameObject optionsPanel;
    public GameObject createPanel;
    public GameObject joinPanel;

    public TMP_Text createdGameCodeText;
    public TMP_InputField gameCodeInput;
    public TMP_Text gameCodeErrorText;

    public void togglePanels(GameObject panel)
    {
        optionsPanel.SetActive(false);
        createPanel.SetActive(false);
        joinPanel.SetActive(false);
        panel.SetActive(true);
    }

    public async void CreateGame()
    {
        togglePanels(createPanel);
        createdGameCodeText.text = "Loading...";
        createdGameCodeText.text =
            await RelayManager.Instance.StartHost(2) ?? "Failed to create game";
    }

    public async void OnJoinGame()
    {
        gameCodeErrorText.text = "";
        bool success = await RelayManager.Instance.JoinClient(gameCodeInput.text);
        if (!success)
        {
            gameCodeErrorText.text = "Failed to join game";
        }
    }
}
