using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EngineManager : MonoBehaviour
{
    //This is the setup manager for the entier porject
    [Header("ManagerRefs")]
    public PhysicsManager physicsManager;
    public SceneManager SimulationManager;
    public BodyManager BodyManager;

    [Header("Materals/Prefabs")]
    public Material[] allMaterials;
    public Dictionary<string, Material> materialLookup = new Dictionary<string, Material>();
    public GameObject[] allPrefabs;
    public Dictionary<string, GameObject> prefabsLookup = new Dictionary<string, GameObject>();

    void Start()
    {
        materalLoader();
        prebadLoader();
        SimulationManager.simulationInit();
        BodyManager.bodyManagerInit();
    }

    private void materalLoader() //this converts each materal(and prefab in the otehr one) into a array and a lookup diconary so i can say 'Earth' and it gives me earhts index
    {
        allMaterials = Resources.LoadAll<Material>("Materials");

        foreach (Material material in allMaterials) materialLookup.Add(material.name, material);
    }

    private void prebadLoader()
    {
        allPrefabs = Resources.LoadAll<GameObject>("Prefabs");

        foreach (GameObject prefaab in allPrefabs) prefabsLookup.Add(prefaab.name, prefaab); 
    }
}