using System.Collections;
using UnityEngine;

/// <summary>
/// A coroutine host for effects that have to outlive whatever started them. An ability sequence
/// frequently continues past the point where its caster is paused, hidden by fog, deactivated on
/// death or despawned, and a coroutine started on the caster would be cut off mid-beat — usually
/// leaving a telegraph on the board forever.
///
/// Local, hidden, and survives scene loads.
/// </summary>
public sealed class FXRunner : MonoBehaviour
{
    private static FXRunner instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    public static FXRunner Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject host = new("FXRunner") { hideFlags = HideFlags.DontSave };
                DontDestroyOnLoad(host);
                instance = host.AddComponent<FXRunner>();
            }
            return instance;
        }
    }

    /// <summary>Starts a detached FX coroutine.</summary>
    public static Coroutine Run(IEnumerator routine)
    {
        return routine == null ? null : Instance.StartCoroutine(routine);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
