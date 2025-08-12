using TMPro;
using UnityEngine;

public class JoinGame : MonoBehaviour
{
    public TMP_InputField gameCodeInput;

    public async void JoinGameFunction()
    {
        await RelayService.Instance.JoinRelay(gameCodeInput.text);
    }
}
