using Unity.Mathematics;
using UnityEngine;

public class OrbtiViewerCamera : MonoBehaviour
{
    public Camera mainCam;
    public CameraController cam;
    public Camera thisCam;
    public GameObject orbits;
    public OrbitViewer orbitViewer;
    public double scale;

    public void camInit()
    {
        scale = orbitViewer.scale;
    }

    public void camUpdate()
    {
        orbits.transform.position = (float3)(-(cam.worldPos / scale));
        thisCam.transform.rotation = mainCam.transform.rotation;
    }
}
