using UnityEngine;
using WiimoteApi;
using System.Collections;

public class WiimoteLifecycleManager : MonoBehaviour
{
    private static WiimoteLifecycleManager instance;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            Application.quitting += OnApplicationQuitting;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnApplicationQuitting()
    {
        Debug.Log("[WiimoteLifecycle] Application quitting - cleaning up");
        StartCoroutine(CleanupCoroutine());
    }

    IEnumerator CleanupCoroutine()
    {
        // Stop the thread FIRST
        WiimoteManager.StopSendThread();
        yield return new WaitForSecondsRealtime(0.2f);

        // Then cleanup wiimotes
        foreach (var wiimote in WiimoteManager.Wiimotes.ToArray())
        {
            try
            {
                wiimote.RumbleOn = false;
                wiimote.SendPlayerLED(false, false, false, false);
            }
            catch { }
        }

        yield return new WaitForSecondsRealtime(0.1f);

        // Clear all
        WiimoteManager.Wiimotes.Clear();

        Debug.Log("[WiimoteLifecycle] Cleanup complete");
    }

    void OnDestroy()
    {
        Application.quitting -= OnApplicationQuitting;
    }
}