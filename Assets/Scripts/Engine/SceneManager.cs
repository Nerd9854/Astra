using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

public class SceneManager : MonoBehaviour
{
    public GameObject parent;
    public EngineManager mainEngineManager;
    public PhysicsManager physicsManager;
    public TextAsset objecthierarchy;
    public GameObject rings;
    public int timeScale = 10; // how fast the sim is spedup compared to normal time
    public bool paused = true; // is the simulation running or not? true=simulation is paused
    public int selectedObjectID = -1; //the ID of the currently selected object
    public int scale = 10000000; //this will never change 100000 meansevery unity unit is 100000km

    [Header("Bodys")]
    public Dictionary<int, GameObject> allBodys = new Dictionary<int, GameObject>();
    private int currID = 0;

    public CameraController cam;

    public int simulationInit()
    {
        foreach (string name in mainEngineManager.bodylookup.Keys)
        {
            bodyLoader(name, "Planet", mainEngineManager.bodylookup[name]);
        }
        return(currID - 1);
    }

    private void bodyLoader(string name, string type, int index)
    {
        BodyData bodyData = getData(name, index);
        GameObject prefab = mainEngineManager.prefabsLookup[type];
        Vector3 radius = bodyData.worldRadius;
        Material materal;
        GameObject ringsGO;

        string[] splitname = name.Split("|");
        if (splitname[0] == "UNKNOWN") name = splitname[1];
        else name = splitname[0];

        try { materal = mainEngineManager.materialLookup[name.Split("|")[0]]; }
        catch (Exception e) { materal = mainEngineManager.materialLookup["Missing Texture".ToUpper()]; }

        allBodys.Add(currID, Instantiate(prefab, parent.transform));
        allBodys[currID].GetComponent<MeshRenderer>().material = materal;
        allBodys[currID].GetComponent<Body>().material = materal;
        allBodys[currID].GetComponent<Body>().position = bodyData.initPos;
        allBodys[currID].GetComponent<Body>().velocity = bodyData.initVel;
        allBodys[currID].GetComponent<Body>().radius = bodyData.avgRad;
        allBodys[currID].GetComponent<Body>().avgRadius = bodyData.avgRadScaled;
        allBodys[currID].GetComponent<Body>().mass = bodyData.GM;
        allBodys[currID].GetComponent<Body>().id = currID;
        allBodys[currID].GetComponent<Body>().bodyName = name;
        allBodys[currID].GetComponent<Body>().cam = cam;
        allBodys[currID].GetComponent<Body>().scale = scale;
        allBodys[currID].GetComponent<Body>().rotation = bodyData.rotationSpeed;
        allBodys[currID].transform.rotation = bodyData.tilt;
        allBodys[currID].name = $"{name} ({currID})";
        allBodys[currID].transform.localScale = radius;

        if (name == "SATURN")
        {
            ringsGO = Instantiate(rings, allBodys[currID].transform);
            float v = Mathf.Min(radius.x, radius.y, radius.z) / 2;
            Shader.SetGlobalFloat(Shader.PropertyToID("_SatRad"), v);
        }

        currID++;
    }

    private class BodyData
    {
        public double3 initPos;
        public double3 initVel;
        public double GM;
        public double3 radius;
        public Vector3 worldRadius;
        public double avgRad;
        public double avgRadScaled;
        public quaternion tilt;
        public double rotationSpeed;

        public void convertRad(int scale)
        {
            double doubleScale = (double)scale;
            double3 doubleWorldRadius = (radius * 2) / new double3(doubleScale, doubleScale, doubleScale);
            worldRadius = math.float3(doubleWorldRadius);

            avgRad = (radius[0] + radius[1] + radius[2]) / 3;
            avgRadScaled = avgRad / scale;
        }

        public void convertToQuart(double4 quat)
        {
            tilt = new quaternion((float4)quat);
            Quaternion.Normalize(tilt);
        }
    }

    private BodyData getData(string name, int index)
    {
        BodyData data = new BodyData();
        data.initPos = mainEngineManager.initalPos[index];
        data.initVel = mainEngineManager.initalVel[index];
        data.GM = mainEngineManager.GMValues[index];
        data.radius = mainEngineManager.RadValues[index];
        data.convertRad(scale);
        data.convertToQuart(mainEngineManager.tiltValues[index]);
        data.rotationSpeed = mainEngineManager.rotationSpeedValues[index];
        return data;
    }
}

