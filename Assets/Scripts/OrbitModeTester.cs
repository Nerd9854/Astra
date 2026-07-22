using Unity.Mathematics;
using UnityEngine;

public class OrbitModeTester : MonoBehaviour
{
    public CameraController cameraController;
    public KeyCode activateKey = KeyCode.O;
    public GameObject orbittarget;

    private void Start()
    {
        // Quick/local test: just point the pivot straight at this object.
        // Works immediately, no doubles involved - good for testing EnterOrbitMode().
        cameraController.target.transform = orbittarget.transform;
        cameraController.target.worldPosition = orbittarget.GetComponent<Body>().position;

        // Once you've got a real double3 position from your planet/physics
        // script, switch to that instead so it stays correct at any distance:
        // cameraController.target.worldPosition = somePlanet.TruePositionKm;
        // cameraController.target.useWorldPosition = true;
    }

    private void Update()
    {
        if (Input.GetKeyDown(activateKey))
        {
            cameraController.target.transform = orbittarget.transform;
            cameraController.target.worldPosition = orbittarget.GetComponent<Body>().position;
            cameraController.EnterOrbitMode();
        }
    }
}