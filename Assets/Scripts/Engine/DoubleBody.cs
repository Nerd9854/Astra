using Unity.Mathematics;
using UnityEngine;

public class DoubleBody : MonoBehaviour
{
    public double3 worldPosition;


    public void ApplyCameraDelta(double3 delta)
    {
        worldPosition -= delta;
    }
}