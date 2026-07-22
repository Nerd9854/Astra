using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEditor.ShaderGraph.Internal.KeywordDependentCollection;

public class BodyManager : MonoBehaviour
{
    public CameraController cam;
    public SceneManager sim;

    public double cullingDist;
    public double cullingdistsq;

    private void Start()
    {
        cullingdistsq = cullingDist * cullingDist;
    }
    public void bodyManagerInit()
    {
        double scale = (double)sim.scale;

        foreach (GameObject body in sim.allBodys.Values)
        {
            Body bodyscript = body.GetComponent<Body>();
            MeshRenderer renderer = body.GetComponent<MeshRenderer>();
            double3 position = bodyscript.position;

            double3 gamepos = position / scale;
            bodyscript.UpdatePosition(convertToFloat(gamepos));
            renderer.enabled = math.lengthsq(position) < cullingdistsq;
        }
    }

    // Update is called once per frame
    void Update()
    {
        double3 worldpos = cam.worldPos;
        double scale = (double)sim.scale;

        foreach (GameObject body in sim.allBodys.Values)
        {
            Body bodyscript = body.GetComponent<Body>();
            MeshRenderer renderer = body.GetComponent<MeshRenderer>();

            double3 distance = bodyscript.position - worldpos;

            if(math.lengthsq(distance) < cullingdistsq)
            {
                if (renderer.enabled == false) renderer.enabled = true;

                double3 gamepos = (distance) / scale;
                bodyscript.UpdatePosition(convertToFloat(gamepos));
                Debug.Log($"showing: {bodyscript.name}");
            }
            else
            {
                renderer.enabled = false;
            }
            //Vector3(-17.1270771, -93.4419937, -121.035278)
            if (body.name.Contains("Sun ("))
            {
                Vector3 normalisedSunPos = convertToFloat(distance / scale); //convertToFloat(math.normalize(distance));
                updateSunPos(normalisedSunPos);
            }
        }
    }

    private Vector3 convertToFloat(double3 gamepos)
    {
        Vector3 gaemposfloat = new Vector3(
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
    }
}
