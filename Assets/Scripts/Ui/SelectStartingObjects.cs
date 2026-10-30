using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using static OrbitViewer;

public class SelectStartingObjects : MonoBehaviour
{
    public GameObject SolarSim;
    public EngineManager EngineManager;

    public string folderName;
    public string metaFileName;
    public string[] allNames;

    public GameObject clickPref;
    public GameObject all;
    public GameObject hide;

    public GameObject scrollbar;

    public Toggle planetsCheckBox;
    public Toggle moonsCheckBox;
    public Toggle allCheckBox;
    public Toggle deselectAllCheckBox;

    public Toggle earthCheckBox;
    public Toggle marsCheckBox;
    public Toggle jupiterCheckBox;
    public Toggle saturnCheckBox;
    public Toggle neptuneCheckBox;
    public Toggle uranusCheckBox;
    public Toggle plutoCheckBox;

    private List<string> earthMoons = new List<string> { "EARTH", "MOON" };
    private List<string> marsMoons = new List<string> { "MARS", "PHOBOS", "DEIMOS" };
    private List<string> jupiterMoons = new List<string> { "JUPITER", "IO", "EUROPA", "GANYMEDE", "CALLISTO", "AMALTHEA", "HIMALIA", "THEBE", "ADRASTEA", "METIS" };
    private List<string> saturnMoons = new List<string> { "SATURN", "MIMAS", "ENCELADUS", "TETHYS", "DIONE", "RHEA", "TITAN", "HYPERION", "IAPETUS", "PHOEBE", "JANUS", "EPIMETHEUS", "HELENE", "ATLAS", "PROMETHEUS", "PANDORA" };
    private List<string> uranusMoons = new List<string> { "URANUS", "ARIEL", "UMBRIEL", "TITANIA", "OBERON", "MIRANDA" };
    private List<string> neptuneMoons = new List<string> { "NEPTUNE", "TRITON", "NAIAD", "THALASSA", "DESPINA", "GALATEA", "LARISSA", "PROTEUS" };
    private List<string> plutoMoons = new List<string> { "PLUTO", "CHARON", "NIX", "HYDRA", "KERBEROS", "STYX" };

    public List<string> currentExports;
    public List<int> ignoreIds;


    public Toggle[] allToggles;

    void Start()
    {
        string dataFolderPath = Path.Combine(Application.streamingAssetsPath, folderName);

        // copied from my earlier code
        try { allNames = File.ReadLines($"{dataFolderPath}\\{metaFileName}.txt").Select(line => line.Split(':')).Select(parts => parts[0]).ToArray(); }
        catch (Exception e1) { Debug.Log($"ERROR: file missing \nTrued to open {dataFolderPath}\\{metaFileName}.txt but got:\n{e1}"); }

        allToggles = new Toggle[allNames.Length];
        udpateBodys();
    }

    private string[] planets = new string[] { "MERCURY", "VENUS", "EARTH", "MARS", "JUPITER", "SATURN", "URANUS", "NEPTUNE" };

    public void OnEnable()
    {
        planetsCheckBox.onValueChanged.AddListener(planetChanged);
        moonsCheckBox.onValueChanged.AddListener(moonChanged);
        allCheckBox.onValueChanged.AddListener(allChanged);

        earthCheckBox.onValueChanged.AddListener(earthChanged);
        marsCheckBox.onValueChanged.AddListener(marsChanged);
        jupiterCheckBox.onValueChanged.AddListener(jupiterChanged);
        saturnCheckBox.onValueChanged.AddListener(saturnChanged);
        neptuneCheckBox.onValueChanged.AddListener(neptuneChanged);
        uranusCheckBox.onValueChanged.AddListener(uranusChanged);
        plutoCheckBox.onValueChanged.AddListener(plutoChanged);
    }

    void OnDisable()
    {
        planetsCheckBox.onValueChanged.RemoveListener(planetChanged);
        moonsCheckBox.onValueChanged.RemoveListener(moonChanged);
        allCheckBox.onValueChanged.RemoveListener(allChanged);

        earthCheckBox.onValueChanged.RemoveListener(earthChanged);
        marsCheckBox.onValueChanged.RemoveListener(marsChanged);
        jupiterCheckBox.onValueChanged.RemoveListener(jupiterChanged);
        saturnCheckBox.onValueChanged.RemoveListener(saturnChanged);
        neptuneCheckBox.onValueChanged.RemoveListener(neptuneChanged);
        uranusCheckBox.onValueChanged.RemoveListener(uranusChanged);
        plutoCheckBox.onValueChanged.RemoveListener(plutoChanged);
    }

    public void confirmSelect()
    {
        if (!systems) getValues();
        ignoreIds.Clear();

        if (currentExports.Count == 0) return;
        else
        {
            foreach (string i in currentExports)
            {
                ignoreIds.Add(Array.IndexOf(allNames, i));
            }

            all.SetActive(false);
            SolarSim.SetActive(true);
            EngineManager.certainObjects = true;
            EngineManager.startingObj = currentExports[0].ToUpper();
            EngineManager.greyOut();
            EngineManager.ignoreIds = ignoreIds.ToArray();
            EngineManager.simulationStart();
        }
    }
    private void getValues()
    {
        currentExports.Clear();
        foreach (Toggle toggle in allToggles)
        {
            if (toggle.isOn)
            {
                currentExports.Add(allNames[Array.IndexOf(allToggles, toggle)]);
            }
        }
    }

    private void udpateBodys()
    {
        int iter = 0;
        foreach (string i in allNames)
        {
            GameObject body = Instantiate(clickPref, scrollbar.transform);
            body.name = i;
            body.transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>().text = i;
            allToggles[iter] = body.transform.GetChild(1).GetComponent<Toggle>();

            iter++;
        }

        scrollbar.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (float)(allNames.Length * 12.6 + 10));
    }

    private void deslectAll()
    {
        foreach (Toggle i in allToggles)
        {
            i.isOn = false;
        }
    }

    private void planetChanged(bool value)
    {
        if (value)
        {
            deslectAll();
            planetsCheckBox.isOn = true;

            foreach (string i in planets)
            {
                try
                {
                    allToggles[Array.IndexOf(allNames, i)].isOn = true;
                }
                catch { }
            }
        }
    }

    private void moonChanged(bool value)
    {
        if (value)
        {
            deslectAll();

            foreach (string i in allNames)
            {
                try
                {
                    if (!planets.Contains(i))
                    {
                        allToggles[Array.IndexOf(allNames, i)].isOn = true;
                    }
                }
                catch { }
            }
        }
    }

    private void allChanged(bool value)
    {
        if (value)
        {
            deslectAll();

            foreach (string i in allNames)
            {
                try
                {
                    allToggles[Array.IndexOf(allNames, i)].isOn = true;
                }
                catch { }
            }
        }
        else
        {
            deslectAll();
        }
    }


    public bool systems = false;

    private void earthChanged(bool value)
    {
        systems = !checkToggles();
        toggleRest(checkToggles());

        currentExports.Clear();
        if (value) currentExports = earthMoons;
    }

    private void marsChanged(bool value)
    {
        systems = !checkToggles();
        toggleRest(checkToggles());

        currentExports.Clear();
        if(value) currentExports = marsMoons;
    }

    private void jupiterChanged(bool value)
    {
        systems = !checkToggles();
        toggleRest(checkToggles());

        currentExports.Clear();
        if(value) currentExports = jupiterMoons;
    }

    private void saturnChanged(bool value)
    {
        systems = !checkToggles();
        toggleRest(checkToggles());

        currentExports.Clear();
        if(value) currentExports = saturnMoons;
    }

    private void neptuneChanged(bool value)
    {
        systems = !checkToggles();
        toggleRest(checkToggles());

        currentExports.Clear();
        if(value) currentExports = neptuneMoons;
    }

    private void uranusChanged(bool value)
    {
        systems = !checkToggles();
        toggleRest(checkToggles());

        currentExports.Clear();
        if(value) currentExports = uranusMoons;
    }

    private void plutoChanged(bool value)
    {
        systems = !checkToggles();
        toggleRest(checkToggles());

        currentExports.Clear();
        if(value) currentExports = plutoMoons;
    }

    private void toggleRest(bool on)
    {
        planetsCheckBox.interactable = on;
        moonsCheckBox.interactable = on;
        allCheckBox.interactable = on;
        hide.SetActive(!on);
    }

    private bool checkToggles()
    {
        return !earthCheckBox.isOn && !marsCheckBox.isOn && !jupiterCheckBox.isOn && !saturnCheckBox.isOn && !neptuneCheckBox.isOn && !uranusCheckBox.isOn && !plutoCheckBox.isOn;
    }
}