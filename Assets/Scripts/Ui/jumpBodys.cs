using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class jumpBodys : MonoBehaviour
{
    public TMPro.TMP_Dropdown selection;
    public EngineManager engineManager;
    public OrbitModeTester orbitModeTester;
    public infoDisplay infoDisplay;
    
    public void jumpBodysInit()
    {
        List<string> allNames = new List<string>();
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
            if (!allNames.Contains(titleName)) allNames.Add(titleName);
        }
        selection.ClearOptions();

        selection.AddOptions(allNames);

        selection.RefreshShownValue();
    }

    public void flyToObject()
    {
        infoDisplay.target = selection.options[selection.value].text.ToUpper();
        infoDisplay.initMenu();
        orbitModeTester.updateOrbit();
    }
}
