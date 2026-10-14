using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

public class Body : MonoBehaviour
{
    public double id = -1; //unique id
    public string bodyName = "unassigned"; //name shown in engine
    public double mass = 0;
    public double radius = 0;
    public double avgRadius = 0;
    public double3 position = new double3(0, 0, 0);
    public double3 velocity = new double3(0, 0, 0);
    public double3 acceleration = new double3(0, 0, 0);
    public double3 rotation = new double3(0, 0, 0);
    public Mesh mesh; //mesh for the body
    public Material material; //materal used
    public MeshRenderer meshRenderer;
    public MeshFilter meshFilter;
    public int scale;
    public bool shoingTrail = false;

    public CameraController cam;
    public bool showdebug = false;
     //

    // Start is called before the first frame update
    void Start()
    {
        return;
        if (radius < 1) Debug.Log($"Error {name}({id})'s radius ({radius}) is incorrect or missing");
        if(id == -1) Debug.Log($"Error {name}({id})'s id ({id}) is missing");
        if(bodyName == "unassigned") Debug.Log($"Error {name}({id})'s bodyName ({bodyName}) is missing");
        if(mass == 0) Debug.Log($"Error {name}({id})'s mass ({mass}) is incorrect or missing");
        if(radius == 0) Debug.Log($"Error {name}({id})'s radius ({radius}) is incorrect or missing");
        if(position.x == 0 && position.y == 0 && position.z == 0) Debug.Log($"Error {name}({id})'s position ({position}) is incorrect or missing");
        if(velocity.x == 0 && velocity.y == 0 && velocity.z == 0) Debug.Log($"Error {name}({id})'s velocity ({velocity}) is incorrect or missing");
        //if(acceleration.x == 0) Debug.Log($"Error {name}({id})'s acceleration ({acceleration}) is incorrect or missing");
        //if(rotation.x == 0) Debug.Log($"Error {name}({id})'s rotation ({rotation}) is incorrect or missing");
        if(mesh == null) Debug.Log($"Error {name}({id})'s mesh ({mesh}) is incorrect or missing");
        if(material == null) Debug.Log($"Error {name}({id})'s material ({material}) is incorrect or missing");
        if(meshRenderer == null) Debug.Log($"Error {name}({id})'s meshRenderer ({meshRenderer}) is incorrect or missing");
        if(meshFilter == null) Debug.Log($"Error {name}({id})'s meshFilter ({meshFilter}) is incorrect or missing");

        //SphereCollider col = gameObject.AddComponent<SphereCollider>();
        //col.radius = (float)(radius / cam.KmPerUnit);
    }

    public void UpdatePosition(Vector3 newPos)
    {
        transform.position = newPos;
    }
}
