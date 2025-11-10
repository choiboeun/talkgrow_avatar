using UnityEngine;

public class AndroidBridge : MonoBehaviour
{
    public CsvRetargetPlayer player;

    void Start()
    {
        Debug.Log("[AndroidBridge] BridgeObject is initialized and ready.");

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                using (AndroidJavaClass pluginClass = new AndroidJavaClass("com.talkgrow_.AvatarGenerateActivity"))
                {
                    pluginClass.CallStatic("onUnityReady", currentActivity);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[AndroidBridge] Failed to notify Android: " + e.Message);
        }
#endif
    }

    public void OnReceiveText(string json)
    {
        Debug.Log("[AndroidBridge] Received from Android: " + json);

        if (player == null)
        {
            Debug.LogWarning("[AndroidBridge] CsvRetargetPlayer is not assigned.");
            return;
        }

        player.PlayTokensJson(json);
    }

    public void ResetPose()
    {
        Debug.Log("[AndroidBridge] ResetPose called from Android");
        if (player == null)
        {
            Debug.LogWarning("[AndroidBridge] CsvRetargetPlayer is not assigned.");
            return;
        }

        player.ResetPose();
    }

    public void Play()
    {
        Debug.Log("[AndroidBridge] Play called from Android");
        if (player == null)
        {
            Debug.LogWarning("[AndroidBridge] CsvRetargetPlayer is not assigned.");
            return;
        }

        player.Play();
    }

    public void Pause()
    {
        Debug.Log("[AndroidBridge] Pause called from Android");
        if (player == null)
        {
            Debug.LogWarning("[AndroidBridge] CsvRetargetPlayer is not assigned.");
            return;
        }

        player.Pause();
    }
}
