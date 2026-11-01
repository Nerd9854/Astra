using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class SpacecraftManager : MonoBehaviour
{
    public EditSpacefraftData editSpacefraftData;
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
    public simulatingTimeUI simulatingTimeUI;
    public bool spacecraftActive = false;

    public Button selectDate;
    public TMPro.TextMeshProUGUI selectDateText;
    public GameObject dateObj;
    public GameObject settings;

    public SpacecraftConfig craft;

    [Header("Mission debugging")]
    [Tooltip("Fly a mission even when the planner says the ship can't afford it. Handy for checking the physics side on its own.")]
    public bool launchEvenIfInfeasible = false;
    public string testOrigin = "EARTH";
    public string testTarget = "MARS";

    // how close to the launch epoch counts as "there", the integrator lands on stopTime to within float error
    private const double TimeTolerance = 1e-3;

    // true from the moment a mission is handed to flyMission until it has finished (or bailed out)
    private bool missionRunning = false;

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
        missionPlanner.Init();
        spacecraftPathGen.genStart();
        testStart();
    }

    private void testStart()
    {
        // The old test ship (Isp 320, 1200 kg fuel) only carries about 3.6 km/s of dV, but the planner bills
        // the launch from rest next to the planet (~11.3 km/s) plus the arrival match, ~14 km/s for Earth -> Mars,
        // so it could never be feasible and every launch stopped at "Mission infeasible".
        // This one is an ion-class ship (Isp 3000) which does fit, with a few hundred kg to spare.
        // The search window is also long enough to contain a Mars launch window (they come every ~26 months).
        craft = new SpacecraftConfig
        {
            DryMass = 500,
            FuelMass = 1200,
            PayloadMass = 50,
            Isp = 3000,
            Thrust = 500,
            EnginePreset = EnginePreset.Ion,
            ObjectiveWeight = 0.3,
            ArrivalMode = ArrivalMode.OrbitInsertion,
            SearchWindowStart = physicsManager.t,
            SearchWindowEnd = physicsManager.t + 800 * 86400,
            MaxMissionDuration = 3 * 365 * 86400
        };
    }

    // Right click this component in the inspector (play mode) -> "Run test mission" to plan and fly
    // testOrigin -> testTarget without going through the UI. If this works and the UI doesn't, the
    // problem is in whatever calls the planner, not in the planner or the physics.
    [ContextMenu("Run test mission")]
    public void RunTestMission()
    {
        if (craft == null) testStart();
        PlanAndFly(
            new[] { testOrigin, testTarget },
            new[] { NodeType.Start, NodeType.End },
            tofMinDays: 100, tofMaxDaysPerLeg: 500,
            pop: 30, gens: 100, restarts: 4,
            searchWindowDays: 800);
    }

    // The whole chain in one call: plan -> check -> fast-forward to launch -> spawn + burn.
    // Anything that used to call missionPlanner.PlanMission and then forgot to hand the result to
    // flyMission (the old code never did) should call this instead.
    // searchWindowDays > 0 resets the search window to [now, now + days], 0 leaves craft's window alone
    public void PlanAndFly(
        string[] bodies, NodeType[] types,
        double tofMinDays = 30, double tofMaxDaysPerLeg = 1500,
        int pop = 0, int gens = 300, int restarts = 8,
        double searchWindowDays = 0)
    {
        if (missionRunning)
        {
            showWarning.dispalyNotif(0, "A mission is already running");
            return;
        }
        if (craft == null)
        {
            Debug.LogError("[Mission] No SpacecraftConfig, craft is null.");
            showWarning.dispalyNotif(0, "No spacecraft configured");
            return;
        }

        if (searchWindowDays > 0)
        {
            craft.SearchWindowStart = physicsManager.t;
            craft.SearchWindowEnd = physicsManager.t + searchWindowDays * 86400;
        }

        // note: this blocks the main thread until the optimizer is done, so the game hitches for a bit
        MissionResult result = missionPlanner.PlanMission(bodies, types, craft, tofMinDays, tofMaxDaysPerLeg, pop, gens, restarts);

        // null means the planner already showed its own warning
        if (result == null) return;

        flyMission(result);
    }

    public void disableOrEnableText(bool enable) //holy jank
    {
        selectDate.interactable = enable;
        selectDateText.color = enable ? new Color32(255, 255, 255, 255) : new Color32(128, 128, 128, 255);

        if (!enable)
        {
            dateObj.SetActive(false);
            settings.SetActive(false);
        }
    }

    // hands an already planned mission to the physics side, plan it with missionPlanner.PlanMission
    // (or just use PlanAndFly)
    public void flyMission(MissionResult mission)
    {
        if (missionRunning)
        {
            showWarning.dispalyNotif(0, "A mission is already running");
            return;
        }
        StartCoroutine(runMission(mission));
    }

    private IEnumerator runMission(MissionResult mission)
    {
        missionRunning = true;
        yield return StartCoroutine(startMission(mission));
        missionRunning = false;
    }

    private IEnumerator startMission(MissionResult mission)
    {
        // PlanMission returns null after showing its own warning when the inputs or search were bad, and a
        // bare `new MissionResult()` has no nodes or legs, there is nothing to fly in either case
        if (mission == null || mission.Nodes == null || mission.Nodes.Count < 2 || mission.Legs == null || mission.Legs.Count == 0)
        {
            Debug.LogWarning("[Mission] flyMission was given an empty mission, was PlanMission run and its result passed in?");
            showWarning.dispalyNotif(0, "No planned mission to launch");
            yield break;
        }

        if (!mission.Feasible)
        {
            string why = string.IsNullOrEmpty(mission.FailureReason) ? "unknown reason" : mission.FailureReason;
            Debug.LogWarning($"[Mission] Infeasible: {why}");

            if (mission.MassMargin < 0)
            {
                showWarning.dispalyNotif(2, $"Mission infeasible: needs {mission.TotalDeltaV:F0} m/s, ship has {mission.AvailableDeltaV:F0} m/s");
            }
            else
            {
                showWarning.dispalyNotif(2, "Mission infeasible (see console for why)");
            }

            if (!launchEvenIfInfeasible) yield break;
            Debug.LogWarning("[Mission] launchEvenIfInfeasible is on, flying it anyway.");
        }

        double launchEpoch = mission.Nodes[0].Epoch;
        double daysToLaunch = (launchEpoch - physicsManager.t) / 86400;

        Debug.Log($"[Mission] Going ahead: launch in {daysToLaunch:F1} days, dV = {mission.TotalDeltaV:F0} m/s, TOF = {mission.TotalTOF / 86400:F1} days");
        showWarning.dispalyNotif(2, $"Found suitable mission, launching in {daysToLaunch:F1} days");

        // the fast-forward only advances if the sim is actually running
        if (physicsManager.paused)
        {
            Debug.LogWarning("[Mission] Physics was paused, resuming it so the mission can run.");
            physicsManager.paused = false;
        }

        // fast-forward to the launch, the integrator lands exactly on stopTime by itself so there is
        // no need to shrink maxH as the epoch approaches, and it gets put back afterwards
        if (launchEpoch - physicsManager.t > TimeTolerance)
        {
            speedViewer.overide = true;

            double oldMaxH = physicsManager.maxH;
            physicsManager.maxH = 99999999999999;

            disableOrEnableText(false);
            editSpacefraftData.disableText();
            StartCoroutine(simulatingTimeUI.showProgress(launchEpoch, mission.TotalTOF, physicsManager.t, true));

            physicsManager.stopTime = launchEpoch;
            while (physicsManager.t < launchEpoch - TimeTolerance)
            {
                yield return null;
            }
            physicsManager.stopTime = double.MaxValue;
            physicsManager.maxH = oldMaxH;

            speedViewer.overide = false;
        }
        else
        {
            // already at the launch epoch, the buttons still need to be locked for the rest of the mission
            disableOrEnableText(false);
            editSpacefraftData.disableText();
        }

        spacecraftActive = true;
        showWarning.dispalyNotif(2, "Launched mission");
        yield return StartCoroutine(missionPlanner.LaunchMissionCoroutine(mission, craft));
        //spacecraftActive = false;

        // was disableOrEnableText(false), which left the date button greyed out after the mission finished
        disableOrEnableText(true);
        editSpacefraftData.enableText();
    }

    public IEnumerator estTime(double estTOF)
    {
        double speed = 0;
        double time = 0;
        int acc = 5;
        for (int i = 0; i < acc; i++)
        {
            yield return null;
            speed += physicsManager.h;
            time += Time.deltaTime;
        }
        double estimatedSpeed = speed * (1.0 / time);
        double estTime = estTOF / estimatedSpeed;

        showWarning.dispalyNotif(1, $"Etimated {convertTime(estTime)} remaning");
    }

    public string convertTime(double estTime)
    {
        // the estimate is TOF / sim speed, so a stalled sim gives Infinity/NaN and TimeSpan.FromSeconds throws
        if (double.IsNaN(estTime) || double.IsInfinity(estTime) || estTime < 0 || estTime > TimeSpan.MaxValue.TotalSeconds)
        {
            return "calculating...";
        }

        TimeSpan timeSpan = TimeSpan.FromSeconds(estTime); //fancyy

        //theres prolly a better way but theres 6 days left so idc   im in my toby fox arc
        if (timeSpan.Days > 0)
        {
            return $"{timeSpan.Days} Days {timeSpan.Hours} Hours {timeSpan.Minutes} Minutes";// {timeSpan.Seconds} Seconds";
        }
        else if (timeSpan.Hours > 0)
        {
            return $"{timeSpan.Hours} Hours {timeSpan.Minutes} Minutes";//  {timeSpan.Seconds} Seconds";
        }
        else if (timeSpan.Minutes > 0)
        {
            return $"{timeSpan.Minutes} Minutes {timeSpan.Seconds} Seconds";
        }
        else if (timeSpan.Seconds > 0)
        {
            return $"{timeSpan.Seconds} Seconds";
        }

        return "0 Seconds";
    }
}

//start Rot
/*Vector3(1.79999995, 0.660000026, 28);
Quaternion(-0.252925605, -0.256416231, -0.0697148144, 0.930279136);
Vector3(0.100000001, 0.100000001, 0.100000001);*/
//