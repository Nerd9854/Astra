using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class Body : MonoBehaviour
{
    public double id; //unique id
    public string bodyName; //name shown in engine
    public double mass;
    public double radius;
    public double position;
    public double velocity;
    public double acceleration;
    public double rotation;
    public Mesh mesh; //mesh for the body
    public Material material; //materal used
    public MeshRenderer meshRenderer;
    public MeshFilter meshFilter;


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
