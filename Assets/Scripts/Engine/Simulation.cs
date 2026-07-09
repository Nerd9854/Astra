using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Simulation : MonoBehaviour
{
    public int timeScale = 10; // how fast the sim is spedup compared to normal time
    public bool paused = true; // is the simulation running or not? true=simulation is paused
    public int selectedObjectID = -1; //the ID of the currently selected object

    [Header("Prefabs")]
    [SerializeField] private GameObject starPrefab;
    [SerializeField] private GameObject planetPrefab;
    [SerializeField] private GameObject moonPrefab;
    [SerializeField] private GameObject asteroidPrefab;
    [SerializeField] private GameObject spacecraftPrefab;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
