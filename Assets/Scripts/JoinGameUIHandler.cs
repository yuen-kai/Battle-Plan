using UnityEngine;

public class JoinGameUIHandler : MonoBehaviour
{
    public GameObject optionsPanel;
    public GameObject createPanel;
    public GameObject joinPanel;

    public void togglePanels(GameObject panel)
    {
        optionsPanel.SetActive(false);
        createPanel.SetActive(false);
        joinPanel.SetActive(false);
        panel.SetActive(true);
    }
}
