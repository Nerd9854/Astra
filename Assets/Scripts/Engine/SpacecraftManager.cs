using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class SpacecraftManager : MonoBehaviour
{
    public GameObject spacecraftPref;
    public spacecraftPathGen spacecraftPathGen;
    public MissionPlanner missionPlanner;
    public speedViewer speedViewer;
    public PhysicsManager physicsManager;
    public SceneManager sceneManager;
    public EngineManager engineManager;
    public BodyLabeler bodyLabeler;
    public Transform sunTransform;
    public GameObject light;
    public GameObject spacecraft;
    public showWarning showWarning;
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
        missionPlanner.Init();
        //testStart();
    }

    private void testStart()
    {
        // Placeholder ship - tune once you have real numbers. WetMass=1750kg, minMass=550kg,
        // Isp=320s gives a full-tank ceiling of ~3.6 km/s (assuming SI units - see the note in
        // MissionPlanner.cs about your sim's unit system).
        var config = new SpacecraftConfig
        {
            DryMass = 500,
            FuelMass = 1200,
            PayloadMass = 50,
            Isp = 320,
            Thrust = 500,
            ObjectiveWeight = 0.3, // lean fuel-optimal, some weight on time
            ArrivalMode = ArrivalMode.OrbitInsertion,
            SearchWindowStart = physicsManager.t,
            SearchWindowEnd = physicsManager.t + 365 * 86400,
            MaxMissionDuration = 3 * 365 * 86400 // 3 year cap, generous
        };

        StartCoroutine(RunTestMissions(config));
    }

    private IEnumerator RunTestMissions(SpacecraftConfig config)
    {
        // --- Parity check: same 2-node Earth->Mars case your old SolveSingleLeg test ran ---
        showWarning.dispalyNotif(1, "Solving Earth->Mars (parity check)");
        var directResult = missionPlanner.PlanMission(
            new[] { "EARTH", "MARS" },
            new[] { NodeType.Start, NodeType.End },
            config,
            tofMinDays: 100, tofMaxDaysPerLeg: 400,
            pop: 30, gens: 100);

        Debug.Log($"[Test] Earth->Mars: t0={directResult.Nodes[0].Epoch / 86400:F1}d, " +
                  $"TOF={directResult.TotalTOF / 86400:F1}d, dV={directResult.TotalDeltaV:F4}, " +
                  $"feasible={directResult.Feasible}, margin={directResult.MassMargin:F1}kg");
        // Should land in the same ballpark as your original SolveSingleLeg test - if the dV is wildly
        // different, that's a sign something in the port (units, node-cost wiring) needs a second look
        // before trusting the multi-leg case below.

        yield return null;

        // --- Multi-leg check: Earth -> Venus flyby -> Mars, exercises the chain + periapsis code ---
        showWarning.dispalyNotif(1, "Solving Earth->Venus->Mars (flyby)");
        var flybyResult = missionPlanner.PlanMission(
            new[] { "EARTH", "VENUS", "MARS" },
            new[] { NodeType.Start, NodeType.Flyby, NodeType.End },
            config,
            tofMinDays: 60, tofMaxDaysPerLeg: 800,
            pop: 40, gens: 150);

        var legTofs = new List<string>();
        foreach (var leg in flybyResult.Legs) legTofs.Add((leg.TOF / 86400).ToString("F1"));

        Debug.Log($"[Test] Earth->Venus->Mars: t0={flybyResult.Nodes[0].Epoch / 86400:F1}d, " +
                  $"leg TOFs={string.Join("/", legTofs)}d, dV={flybyResult.TotalDeltaV:F4}, " +
                  $"feasible={flybyResult.Feasible}, margin={flybyResult.MassMargin:F1}kg");

        if (!flybyResult.Feasible)
        {
            showWarning.dispalyNotif(2, "Flyby route infeasible with current ship - check the log");
            yield break;
        }

        // Fast-forward to the departure epoch (same maxH trick your original waitForStart used).
        // LaunchMissionCoroutine also waits for its own departure epoch internally, so once we've
        // already fast-forwarded here that inner wait just falls through immediately.
        speedViewer.overide = true;
        showWarning.dispalyNotif(1, $"Simulating {flybyResult.TotalTOF / 86400:F1} days\nThis may take a while");
        physicsManager.maxH = 99999999999999;

        double launchEpoch = flybyResult.Nodes[0].Epoch;
        while (physicsManager.t < launchEpoch)
        {
            double timeLeft = launchEpoch - physicsManager.t;
            if (timeLeft < physicsManager.maxH)
                physicsManager.maxH = timeLeft;
            yield return null;
        }

        yield return StartCoroutine(missionPlanner.LaunchMissionCoroutine(flybyResult, config));

        speedViewer.overide = false;
        showWarning.dispalyNotif(2, "Launched mission");
    }
}

//start Rot
/*Vector3(1.79999995, 0.660000026, 28);
Quaternion(-0.252925605, -0.256416231, -0.0697148144, 0.930279136);
Vector3(0.100000001, 0.100000001, 0.100000001);*/
//