using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

public class GridMan : MonoBehaviour
{
    public GameObject grid;
    public CameraController cam;
    public SceneManager sceneManager;
    public double camScale;
    public double modthing;
    public double minV = 1000;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    private static readonly int camPosID = Shader.PropertyToID("_CameraPosition");
    private static readonly int gridSizeID = Shader.PropertyToID("_GridSize");
    void Update()
    {
        double3 worldpos = cam.worldPos;
        Vector3 camPosScaled = (float3)(worldpos / sceneManager.scale);
        Shader.SetGlobalVector(camPosID, camPosScaled);
        double dist = (math.abs(cam.worldPos.y) / camScale);
        Shader.SetGlobalFloat(gridSizeID, (float)math.max(minV, math.pow(modthing, math.floor(math.log(dist) / math.log(modthing)))));


        double3 distance = 0 - worldpos;

        double3 gamepos = (distance) / sceneManager.scale;
        double3 modPos = new double3(gamepos.x % modthing, gamepos.y, gamepos.z % modthing);
        grid.transform.position = (float3)(modPos);
    }
}
