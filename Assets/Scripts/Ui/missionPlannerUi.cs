using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class missionPlannerUi : MonoBehaviour
{
    public Button ok;
    public TMP_Dropdown startingPlanet;
    public TMP_Dropdown endingPlanet;
    public TMP_InputField minDays;
    public TMP_InputField minDaysperLeg;
    public EngineManager engineManager;

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
        string value = startingPlanet.options[index].text;

        endingPlanet.ClearOptions();

        List<string> newAvNames = new List<string>(allNames);
        newAvNames.Remove(value);

        endingPlanet.AddOptions(newAvNames);

        endingPlanet.RefreshShownValue();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
