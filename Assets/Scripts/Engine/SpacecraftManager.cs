using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor.Purchasing;
using UnityEngine;

public class SpacecraftManager : MonoBehaviour
{
    public GameObject spacecraftPref;
    public spacecraftPathGen spacecraftPathGen;
    public SceneManager sceneManager;
    public EngineManager engineManager;
    public BodyLabeler bodyLabeler;
    public Transform sunTransform;
    public GameObject light;
    public GameObject spacecraft;
    public bool spacecraftActive = false;

    public void updateCraft()
    {
        Vector3 sunPos = sunTransform.position;
        Vector3 craftPos = spacecraft.transform.position;

        Vector3 dir = craftPos - sunPos;
        light.transform.rotation = Quaternion.LookRotation(dir);
    }

    public void craftinint()
    {
        sunTransform = sceneManager.allBodys[engineManager.bodylookup["SUN"]].transform;
        spacecraft = Instantiate(spacecraftPref, this.transform);
        bodyLabeler.spacecraftTransform = spacecraft.transform;
        spacecraftActive = true;
        spacecraftPathGen.genStart();
        testStart();
    }

    private void testStart()
    {
        spacecraftPathGen.genStart();

        // --- Sanity check: hand-picked Earth -> Mars, ~259 day transfer ---
        double t0 = 0;
        double tof = 259 * 86400; // seconds — adjust if your sim's time unit isn't seconds
        double dv = spacecraftPathGen.getVel("EARTH", "MARS", t0, tof);
        Debug.Log($"[Test] Earth->Mars dV at t0=0, tof=259d: {dv} (expect roughly 2-4, in whatever unit your mu/positions use)");

        // --- Automatic search ---
        double t0Min = 0;
        double t0Max = 365 * 86400;
        double tofMin = 100 * 86400;
        double tofMax = 400 * 86400;

        var (bestT0, bestTOF, bestCost) = spacecraftPathGen.SolveSingleLeg("EARTH", "MARS", t0Min, t0Max, tofMin, tofMax, pop: 30, gens: 100);
        Debug.Log($"[Test] Best t0={bestT0 / 86400:F1}d, TOF={bestTOF / 86400:F1}d, dV={bestCost:F4}");

        // --- Actually fly it ---
        spacecraftPathGen.LaunchMission("EARTH", "MARS", bestT0, bestTOF);
        Debug.Log("[Test] Spacecraft spawned, burn commanded.");
    }
}

//start Rot
/*Vector3(1.79999995, 0.660000026, 28);
Quaternion(-0.252925605, -0.256416231, -0.0697148144, 0.930279136);
Vector3(0.100000001, 0.100000001, 0.100000001);*/
