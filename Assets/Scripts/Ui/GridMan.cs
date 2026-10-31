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
    public double distThresh;

    public GameObject miniGrid;

    private static readonly int camPosID = Shader.PropertyToID("_CameraPosition");
    private static readonly int gridSizeID = Shader.PropertyToID("_GridSize");
    private static readonly int hideIndexID = Shader.PropertyToID("_HideIndex");

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


        Shader.SetGlobalFloat(hideIndexID, (distThresh < math.length(modPos)) ? 1f : 0f);
        Debug.Log((distThresh < math.length(modPos)));

        grid.transform.position = (float3)(modPos);
    }
}
