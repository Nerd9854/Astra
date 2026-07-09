using UnityEngine;

public class OrbitModeTester : MonoBehaviour
{
    public CameraController cameraController;

    public KeyCode activateKey = KeyCode.O;

    private void Update()
    {
        if (Input.GetKeyDown(activateKey))
        {
            cameraController.EnterOrbitMode();
        }
    }
}