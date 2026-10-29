using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using Unity.VisualScripting.FullSerializer;
using UnityEditor.Experimental.GraphView;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UI;

public class missionPlannerUi : MonoBehaviour
{
    public Button ok;
    public MissionPlanner missionPlanner;
    public SpacecraftManager spacecraftManager;
    public ListManager listManager;
    public TMP_Dropdown startingPlanet;
    public TMP_Dropdown endingPlanet;
    public TMP_InputField minDays;
    public TMP_InputField minDaysperLeg;
    public EngineManager engineManager;

    public showWarning showWarning;

    public List<string> allNames;

    public void missionPlannerUiStart()
    {
        startingPlanet.onValueChanged.AddListener(startPlanChanged);
        allNames.Add("Mercury");
        allNames.Add("Venus");
        allNames.Add("Earth");
        allNames.Add("Mars");
        allNames.Add("Jupiter");
        allNames.Add("Saturn");
        allNames.Add("Uranus");
        allNames.Add("Neptune");

        foreach (string name in engineManager.namelookup.Values)
        {
            string titleName = char.ToUpper(name[0]) + name[1..].ToLower();
            if(!allNames.Contains(titleName)) allNames.Add(titleName);
        }
        startingPlanet.ClearOptions();
        endingPlanet.ClearOptions();

        startingPlanet.AddOptions(allNames);
        endingPlanet.AddOptions(allNames);

        startingPlanet.RefreshShownValue();
        endingPlanet.RefreshShownValue();
    }

    private void startPlanChanged(int index)
    {
        return;
        string value = startingPlanet.options[index].text;

        endingPlanet.ClearOptions();

        List<string> newAvNames = new List<string>(allNames);
        newAvNames.Remove(value);

        endingPlanet.AddOptions(newAvNames);

        endingPlanet.RefreshShownValue();
    }

    public void readValues()
    {
        try
        {
            string[] dests = listManager.addInterSteps(startingPlanet.options[startingPlanet.value].text.ToUpper(), endingPlanet.options[endingPlanet.value].text.ToUpper());
            NodeType[] types = setupNodes(dests.Count());
            int minDays = int.Parse(this.minDays.text);
            int maxDaysPerLeg = int.Parse(minDaysperLeg.text);

            foreach (var item in dests)
            {
                Debug.Log(item);
            }
            foreach (var item in types)
            {
                Debug.Log(item);
            }
            Debug.Log(minDays);
            Debug.Log(maxDaysPerLeg);

            if (minDays < 0)
            {
                showWarning.dispalyNotif(0, "Minimum Days Per Leg is negative");
                return;
            }

            if (maxDaysPerLeg == 0)
            {
                showWarning.dispalyNotif(0, "Max days per leg cannot be 0");
                return;
            }

            string prev = "";
            foreach(string i in dests)
            {
                if(i == prev)
                {
                    showWarning.dispalyNotif(0, "Succsessive points cannot be the same planet/moon");
                    return;
                }
                prev = i;
            }

            showWarning.dispalyNotif(1, "Planning mission");
            MissionResult mission = missionPlanner.PlanMission(dests, types, spacecraftManager.craft, tofMinDays: minDays, tofMaxDaysPerLeg: maxDaysPerLeg, pop: 40, gens: 150);

            if (!mission.Feasible)
            {
                showWarning.dispalyNotif(0, "Could not find valid mission");
                return;
            }

            Debug.Log("yo1");
            spacecraftManager.flyMission(mission);

        }
        catch (System.Exception e)
        {
            showWarning.dispalyNotif(0, $"Error with inputted feilds\n{e}");
        }
    }

    private NodeType[] setupNodes(int len)
    {
        List<NodeType> nodes = new List<NodeType>();
        nodes.Add(NodeType.Start);

        for (int i = 0; i < len-2; i++)
        {
            nodes.Add(NodeType.Flyby);
        }

        nodes.Add(NodeType.End);

        return nodes.ToArray();
    }
}
