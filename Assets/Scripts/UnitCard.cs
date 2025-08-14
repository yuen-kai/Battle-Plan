using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class UnitCard : NetworkBehaviour
{
    public void DisableUI()
    {
        StartCoroutine(DisableUIAfterDelay());
    }

    IEnumerator DisableUIAfterDelay()
    {
        while (transform.childCount == 0)
            yield return null;

        if (!transform.GetChild(0).GetComponent<NetworkObject>().IsOwner)
        {
            CanvasGroup cg = gameObject.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = gameObject.AddComponent<CanvasGroup>();

            cg.alpha = 0f; // Makes UI invisible
            cg.interactable = false; // Blocks input (buttons, etc.)
            cg.blocksRaycasts = false; // Prevents UI from blocking clicks behind it
        }
    }
}
