using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class EngineManager : MonoBehaviour
{
    //This is the setup manager for the entier porject
    [Header("ManagerRefs")]
    public CameraController cam;
    public DataExtractor DataExtractor;
    public SceneManager SceneManager;
    public BodyManager BodyManager;
    public PhysicsManager PhysicsManager;
    public UiManager UiManager;
    public SpacecraftManager SpacecraftManager;

    [Header("Materals/Prefabs")]
    public Material[] allMaterials;
    public Dictionary<string, Material> materialLookup = new Dictionary<string, Material>();
    public GameObject[] allPrefabs;
    public Dictionary<string, GameObject> prefabsLookup = new Dictionary<string, GameObject>();

    [Header("Bodydata")]
    public Dictionary<string, int> bodylookup = new Dictionary<string, int>();
    public Dictionary<int, string> namelookup = new Dictionary<int, string>();
    public double3[] initalPos;
    public double3[] initalVel;
    public double[] GMValues;
    public double3[] RadValues;
    public double4[] KepValues;
    public string[] KepValuesMeta;
    public double4[] tiltValues;
    public double[] rotationSpeedValues;
    public double g = 0.000000000066743;

    private int numOfObjects;
    void Start()
    {
        materalLoader();
        prebadLoader();
        DataExtractor.extractData(false, "");
        numOfObjects = SceneManager.simulationInit(); //returns the num of obejcts
        BodyManager.bodyManagerInit();
        PhysicsManager.physicsInit(numOfObjects);
        SpacecraftManager.craftinint();

        UiManager.UiInit();
    }

    private void materalLoader() //this converts each materal(and prefab in the otehr one) into a array and a lookup diconary so i can say 'Earth' and it gives me earhts index
    {
        allMaterials = Resources.LoadAll<Material>("Materials");

        foreach (Material material in allMaterials) materialLookup.Add(material.name.ToUpper(), material);
    }

    private void prebadLoader()
    {
        allPrefabs = Resources.LoadAll<GameObject>("Prefabs");

        foreach (GameObject prefaab in allPrefabs) prefabsLookup.Add(prefaab.name, prefaab);
    }

    private void Update() //entire scripts update loop (not bodys tho)
    {
        PhysicsManager.physicsUpdate();
        BodyManager.updateBodys();
        cam.camUpdate();
        UiManager.UIUpdate();
        SpacecraftManager.updateCraft();
    }

    public void jumpDate()
    {
        SelectDateMan.dateObj dateData = UiManager.selectDateMan.getData();
        DataExtractor.extractData(true, $"{dateData.dataDate.x.ToString("D2")}-{dateData.dataDate.y.ToString("D2")}-{dateData.dataDate.z}");

        BodyManager.bodyManagerInit();
        PhysicsManager.physicsInit(numOfObjects);
    }
}
