using UnityEngine;

public class QuitGame : MonoBehaviour
{
    //from google
    public void quitGame()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}
