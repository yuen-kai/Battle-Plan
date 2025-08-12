using TMPro;
using UnityEngine;

public class CreateGame : MonoBehaviour
{
    public TMP_Text gameCodeText;

    public void CreateGame()
    {
        string joinCode = RelayService.Instance.CreateRelay();
        gameCodeText.text = joinCode;
    }
}
