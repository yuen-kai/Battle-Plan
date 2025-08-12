using TMPro;
using UnityEngine;

public class JoinGame : MonoBehaviour
{
    public TMP_InputField gameCodeInput;

    public void JoinGame()
    {
        bool success = RelayService.Instance.JoinRelay(gameCodeInput.text);
        if (success)
        {
            Debug.Log("Joined game successfully");
        }
        else
        {
            Debug.Log("Failed to join game");
        }
    }
}
