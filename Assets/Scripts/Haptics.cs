using UnityEngine;

// Vibration courte (20 ms) sur Android, plus discrète que Handheld.Vibrate.
public static class Haptics
{
    public static bool Enabled = true;

    public static void Tap(long milliseconds = 20)
    {
        if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
            {
                if (vibrator != null) vibrator.Call("vibrate", milliseconds);
            }
        }
        catch (System.Exception)
        {
            // Repli : vibration standard. Sa présence dans le code fait aussi ajouter la permission VIBRATE au build.
            Handheld.Vibrate();
        }
#endif
    }
}
