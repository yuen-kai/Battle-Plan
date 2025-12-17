using TMPro;
using Unity.Netcode;
using UnityEngine;

public class JoinGameUIHandler : MonoBehaviour
{
    [SerializeField]
    private GameObject optionsPanel;

    [SerializeField]
    private GameObject createPanel;

    [SerializeField]
    private GameObject joinPanel;

    [SerializeField]
    private TMP_Text createdGameCodeText;

    [SerializeField]
    private TMP_InputField gameCodeInput;

    [SerializeField]
    private TMP_Text gameCodeErrorText;

    public void togglePanels(GameObject panel)
    {
        optionsPanel.SetActive(false);
        createPanel.SetActive(false);
        joinPanel.SetActive(false);
        panel.SetActive(true);
    }

    public async void CreateGame()
    {
        if (GameLoop.TESTING)
        {
            NetworkManager.Singleton.StartHost();
            return;
        }
        togglePanels(createPanel);
        createdGameCodeText.text = "Loading...";
        createdGameCodeText.text =
            await RelayManager.Instance.StartHost(2) ?? "Failed to create game";
    }

    public void JoinGame()
    {
        if (GameLoop.TESTING)
        {
            NetworkManager.Singleton.StartClient();
            return;
        }
        togglePanels(joinPanel);
    }

    public async void AttemptJoinGame()
    {
        gameCodeErrorText.text = "Attempting to join game...";
        bool success = await RelayManager.Instance.JoinClient(gameCodeInput.text);
        if (!success)
        {
            gameCodeErrorText.text = "Failed to join game";
        }
        else
        {
            gameCodeErrorText.text = "Joining game...";
        }
    }
}
