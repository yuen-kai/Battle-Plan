using UnityEngine;
using Unity.Netcode;

public class UnitCard : NetworkBehaviour
{
    public void DisableUI()
    {
        if(!transform.GetChild(0).GetComponent<NetworkObject>().IsOwner)
        {
            CanvasGroup cg = gameObject.GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();

            cg.alpha = 0f;                 // Makes UI invisible  
            cg.interactable = false;       // Blocks input (buttons, etc.)  
            cg.blocksRaycasts = false;     // Prevents UI from blocking clicks behind it
        }
    }
}
