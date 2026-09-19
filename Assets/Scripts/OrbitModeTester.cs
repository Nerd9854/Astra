using Unity.Mathematics;
using UnityEngine;

public class OrbitModeTester : MonoBehaviour
{
    public CameraController cameraController;
    public SpacecraftManager spacecraftManager;
    public PhysicsManager physicsManager;
    public EngineManager engineManager;
    public SceneManager sceneManager;
    public infoDisplay infoDisplay;
    public KeyCode activateKey = KeyCode.O;
    public GameObject orbittarget;

    private void Start()
    {
        cameraController.target.transform = orbittarget.transform;
        cameraController.target.worldPosition = orbittarget.GetComponent<Body>().position;
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
    public  void updateOrbit()
    {
        try
        {
            if (infoDisplay.target == "Spacecraft")
            {
                string gameObj = infoDisplay.target;
                GameObject objToTarget = spacecraftManager.spacecraft;
                cameraController.target.transform = objToTarget.transform;
                cameraController.target.worldPosition = physicsManager.fullObjData[physicsManager.spacecraftID].pos;
                cameraController.EnterOrbitMode();
            }
            else
            {
                string gameObj = infoDisplay.target;
                GameObject objToTarget = sceneManager.allBodys[engineManager.bodylookup[gameObj]];
                cameraController.target.transform = objToTarget.transform;
                cameraController.target.worldPosition = objToTarget.GetComponent<Body>().position;
                cameraController.EnterOrbitMode();
            }
        }
        catch
        {

        }
    }
}
