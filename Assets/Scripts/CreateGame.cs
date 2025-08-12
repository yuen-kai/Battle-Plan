using TMPro;
using UnityEngine;

public class CreateGame : MonoBehaviour
{
    public TMP_Text gameCodeText;

    public async void CreateGameFunction()
    {
        gameCodeText.text = await RelayService.Instance.CreateRelay() ?? "Failed to create game";
    }
}
