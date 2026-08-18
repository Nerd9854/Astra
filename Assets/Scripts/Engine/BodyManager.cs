using Unity.Mathematics;
using UnityEngine;

public class BodyManager : MonoBehaviour
{
    public CameraController cam;
    public SceneManager SceneManager;
    public PhysicsManager PhysicsManager;

    public OrbitViewer OrbitViewer;

    public double cullingDist;
    public double cullingdistsq;

    private Body[] allBodyscripts;
    private MeshRenderer[] allMeshRenderers;
    private GameObject sun;

    public void bodyManagerInit()
    {
        cullingdistsq = cullingDist * cullingDist;

        double scale = (double)SceneManager.scale;

        int numOfBodyes = SceneManager.allBodys.Values.Count;

        allBodyscripts = new Body[numOfBodyes];
        allMeshRenderers = new MeshRenderer[numOfBodyes];



        int currIndex = 0;
        foreach (GameObject body in SceneManager.allBodys.Values)
        {
            Body bodyscript = body.GetComponent<Body>();
            MeshRenderer renderer = body.GetComponent<MeshRenderer>();

            allBodyscripts[currIndex] = bodyscript;
            allMeshRenderers[currIndex] = renderer;

            double3 position = bodyscript.position;

            double3 gamepos = position / scale;
            bodyscript.UpdatePosition(convertToFloat(gamepos));
            //renderer.enabled = math.lengthsq(position) < cullingdistsq;

            if (bodyscript.name.Contains("SUN ("))
            {
                sun = body;
            }

            currIndex++;
        }
    }

    public void updateBodys()
    {
        double3 worldpos = cam.worldPos;
        double scale = (double)SceneManager.scale;

        int currIndex = 0;
        foreach (GameObject body in SceneManager.allBodys.Values)
        {
            Body bodyscript = allBodyscripts[currIndex];
            MeshRenderer renderer = allMeshRenderers[currIndex];

            double3 distance = PhysicsManager.fullObjData[currIndex].pos - worldpos;

            double3 gamepos = (distance) / scale;
            body.transform.position = (float3)(gamepos);

            //renderer.enabled = math.lengthsq(distance) < cullingdistsq;

            currIndex++;
        }

        updateSunPos(sun.transform.position);
    }

    private Vector3 convertToFloat(double3 gamepos)
    {
        Vector3 gaemposfloat = new(
            (float)gamepos.x,
            (float)gamepos.y,
            (float)gamepos.z
        );

        return gaemposfloat;
    }

    private static readonly int SunPositionID = Shader.PropertyToID("_SunPosition");
    private void updateSunPos(Vector3 sunPos)
    {
        Shader.SetGlobalVector(SunPositionID, sunPos);
        OrbitViewer.updatePos(sunPos);
    }
}
