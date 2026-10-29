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
        craft = new SpacecraftConfig
        {
            DryMass = 500,
            FuelMass = 1200,
            PayloadMass = 50,
            Isp = 320,
            Thrust = 500,
            ObjectiveWeight = 0.3,
            ArrivalMode = ArrivalMode.OrbitInsertion,
            SearchWindowStart = physicsManager.t,
            SearchWindowEnd = physicsManager.t + 365 * 86400,
            MaxMissionDuration = 3 * 365 * 86400
        };

        MissionResult h = new MissionResult();

        //StartCoroutine(flyMission(h));//craft));
    }

    public void disableOrEnableText(bool enable) //holy jank
    {
        selectDate.interactable = enable;
        selectDateText.color = enable? new Color32(255, 255, 255, 255) : new Color32(128, 128, 128, 255);

        if (!enable)
        {
            dateObj.SetActive(false);
            settings.SetActive(false);
        }
    }

    public void flyMission(MissionResult mission)
    {
        StartCoroutine(startMission(mission));
    }

    private IEnumerator startMission(MissionResult mission)
    {
        Debug.Log("yo2");
        // --- Parity check: same 2-node Earth->Mars case your old SolveSingleLeg test ran ---
        /*showWarning.dispalyNotif(1, "Solving Earth->Mars (parity check)");
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

        yield return null;*/

        // --- Multi-leg check: Earth -> Venus flyby -> Mars, exercises the chain + periapsis code ---

        /*showWarning.dispalyNotif(1, "Solving Earth->Venus->Mars (flyby)");
        var mission = missionPlanner.PlanMission(
            new[] { "EARTH", "VENUS", "MARS" },
            new[] { NodeType.Start, NodeType.Flyby, NodeType.End },
            craft,
            tofMinDays: 60, tofMaxDaysPerLeg: 800,
            pop: 40, gens: 150);
        */

        var legTofs = new List<string>();
        foreach (var leg in mission.Legs) legTofs.Add((leg.TOF / 86400).ToString("F1"));

        /*Debug.Log($"[Test] Earth->Venus->Mars: t0={flybyResult.Nodes[0].Epoch / 86400:F1}d, " +
                  $"leg TOFs={string.Join("/", legTofs)}d, dV={flybyResult.TotalDeltaV:F4}, " +
                  $"feasible={flybyResult.Feasible}, margin={flybyResult.MassMargin:F1}kg");*/

        if (!mission.Feasible)
        {
            showWarning.dispalyNotif(2, "Mission infeasible");
            yield break;
        }

        Debug.Log("yo3");
        speedViewer.overide = true;
        double launchEpoch = mission.Nodes[0].Epoch;

        showWarning.dispalyNotif(2, $"Found suitable mission, launching in {(launchEpoch - physicsManager.t) / 86400:F1} days");

        //showWarning.dispalyNotif(1, $"Simulating {(launchEpoch - physicsManager.t) / 86400:F1} days\nCalculating Estimate");
        //StartCoroutine(estTime(launchEpoch - physicsManager.t));

        physicsManager.maxH = 99999999999999;

        disableOrEnableText(false);
        editSpacefraftData.disableText();
        StartCoroutine(simulatingTimeUI.showProgress(launchEpoch, mission.TotalTOF, physicsManager.t, true));

        while (physicsManager.t < launchEpoch)
        {
            double timeLeft = launchEpoch - physicsManager.t;
            if (timeLeft < physicsManager.maxH)
                physicsManager.maxH = timeLeft;
            yield return null;
            Debug.Log("Looping");
            if(timeLeft < 1)
            {
                break;
            }
        }
        Debug.Log("yo4");

        speedViewer.overide = false;
        spacecraftActive = true;
        yield return StartCoroutine(missionPlanner.LaunchMissionCoroutine(mission, craft));
        spacecraftActive = false;


        disableOrEnableText(false);
        editSpacefraftData.enableText();
        showWarning.dispalyNotif(2, "Launched mission");
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