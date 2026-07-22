using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public class SceneManager : MonoBehaviour
{
    public GameObject parent;
    public EngineManager mainEngineManager;
    public PhysicsManager physicsManager;
    public TextAsset objecthierarchy;
    public int timeScale = 10; // how fast the sim is spedup compared to normal time
    public bool paused = true; // is the simulation running or not? true=simulation is paused
    public int selectedObjectID = -1; //the ID of the currently selected object
    public int scale = 10000000; //this will never change 100000 meansevery unity unit is 100000km
    public float temp;

    [Header("Bodys")]
    public Dictionary<int, GameObject> allBodys = new Dictionary<int, GameObject>();
    private int currID = 0;

    public CameraController cam; //temp

    public void simulationInit()
    {
        /*objectnames names = JsonUtility.FromJson<objectnames>(objecthierarchy.text);

        foreach (string _object in names.planets) bodyLoader(_object, "Planet", true);
        foreach (string _object in names.dwarfPlanets) bodyLoader(_object, "Planet");
        foreach (string _object in names.moons) bodyLoader(_object, "Moon");*/
        bodyLoader("Sun", "Planet", true);
    }

    private void bodyLoader(string name, string type, bool temp=false)
    {
        ExtractedBodyData bodyData = extractDataFromPath(name);
        GameObject prefab = mainEngineManager.prefabsLookup[type];
        Material materal = mainEngineManager.materialLookup["Missing Texture"];

        if (temp) materal = mainEngineManager.materialLookup[name];


        allBodys.Add(currID, Instantiate(prefab, parent.transform));
        allBodys[currID].GetComponent<MeshRenderer>().material = materal;
        allBodys[currID].GetComponent<Body>().material = materal;
        allBodys[currID].GetComponent<Body>().position = bodyData.Position;
        allBodys[currID].GetComponent<Body>().velocity = bodyData.Velocity;
        allBodys[currID].GetComponent<Body>().radius = bodyData.Radius;
        allBodys[currID].GetComponent<Body>().mass = bodyData.Mass;
        allBodys[currID].GetComponent<Body>().id = currID;
        allBodys[currID].GetComponent<Body>().bodyName = name;
        allBodys[currID].GetComponent<Body>().cam = cam;
        allBodys[currID].GetComponent<Body>().scale = scale;
        allBodys[currID].name = $"{name} ({currID})";
        allBodys[currID].transform.localScale = new Vector3(bodyData.Radius, bodyData.Radius, bodyData.Radius);

        currID++;
    }

    [System.Serializable]
    private class RawBodyData
    {
        public string parent;
        public string origin;
        public double[] position_km;
        public double[] velocity_km_s;
        public double mass_kg;
        public RadiusData radius;
    }

    [System.Serializable]
    private class RadiusData {public float mean_km;}

    public class ExtractedBodyData
    {
        public string Parent;
        public string Origin;
        public double3 Position;
        public double3 Velocity;
        public double Mass;
        public float Radius;
    }

    private ExtractedBodyData extractDataFromPath(string name)
    {
        TextAsset jsonFile = Resources.Load<TextAsset>($"BodyData/{name}");

        RawBodyData rawBodyData = JsonUtility.FromJson<RawBodyData>(jsonFile.text);

        Resources.UnloadAsset(jsonFile);

        ExtractedBodyData final = new ExtractedBodyData();
        final.Parent = rawBodyData.parent;
        final.Origin = rawBodyData.origin;
        //final.Position = new double3(rawBodyData.position_km[0] / (double)scale, rawBodyData.position_km[1] / (double)scale, rawBodyData.position_km[2] / (double)scale);
        //final.Velocity = new double3(rawBodyData.velocity_km_s[0] / (double)scale, rawBodyData.velocity_km_s[1] / (double)scale, rawBodyData.velocity_km_s[2] / (double)scale);
        final.Position = new double3(rawBodyData.position_km[0], rawBodyData.position_km[1], rawBodyData.position_km[2]);
        final.Velocity = new double3(rawBodyData.velocity_km_s[0], rawBodyData.velocity_km_s[1], rawBodyData.velocity_km_s[2]);

        if (rawBodyData.mass_kg != null)
        {
            final.Mass = rawBodyData.mass_kg;
        }

        if (rawBodyData.radius != null)
        {
            final.Radius = ((rawBodyData.radius.mean_km*2f)/((float)scale))*temp;
        }

        return final;
    }
}
