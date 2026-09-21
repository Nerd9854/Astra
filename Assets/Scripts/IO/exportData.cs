using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using static OrbitViewer;
using static spacecraftPathGen;

public class exportData : MonoBehaviour
{
    public showWarning showWarning;
    public EngineManager engineManager;
    public PhysicsManager physicsManager;
    public OrbitViewer orbitViewer;
    public speedViewer speedViewer;
    public GameObject clickPref;
    public GameObject scrollbar;

    public TMP_Dropdown fileFormat;

    public Toggle posCheckBox;
    public Toggle velCheckBox;
    public Toggle accCheckBox;
    public Toggle kepDataCheckBox;
    public Toggle massCheckBox;

    public Toggle planetsCheckBox;
    public Toggle moonsCheckBox;
    public Toggle allCheckBox;
    public Toggle deselectAllCheckBox;

    public Toggle[] allToggles;

    private string[] planets = new string[] { "MERCURY", "VENUS", "EARTH", "MARS", "JUPITER", "SATURN", "URANUS", "NEPTUNE" };

    public void OnEnable()
    {
        planetsCheckBox.onValueChanged.AddListener(planetChanged);
        moonsCheckBox.onValueChanged.AddListener(moonChanged);
        allCheckBox.onValueChanged.AddListener(allChanged);
    }
    void OnDisable()
    {
        planetsCheckBox.onValueChanged.RemoveListener(planetChanged);
        moonsCheckBox.onValueChanged.RemoveListener(moonChanged);
        allCheckBox.onValueChanged.RemoveListener(allChanged);
    }

    public void dataExporterInit()
    {
        string dataPath = Path.Combine(Application.persistentDataPath, "ExportedData");

        if (!Directory.Exists(dataPath))
        {
            Directory.CreateDirectory(dataPath);
        }
        allToggles = new Toggle[engineManager.bodylookup.Keys.Count];
        udpateBodys();


        //openFolder();
    }

    private void udpateBodys()
    {
        int iter = 0;
        foreach (string i in engineManager.bodylookup.Keys)
        {
            GameObject body = Instantiate(clickPref, scrollbar.transform);
            body.name = i;
            body.transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>().text = i;
            allToggles[iter] = body.transform.GetChild(1).GetComponent<Toggle>();

            iter++;
        }

        scrollbar.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (float)(engineManager.bodylookup.Keys.Count * 12.6 + 10));
    }

    public void openFolder()
    {
        string dataPath = Path.Combine(Application.persistentDataPath, "ExportedData");

        //stack overflow, self expalnotory just confusing
        string sanitizedPath = dataPath.Replace(@"/", @"\");

        if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
        {
            Process.Start("explorer.exe", sanitizedPath);
        }
        else if (Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor)
        {
            Process.Start("open", dataPath);
        }
        else
        {
            showWarning.dispalyNotif(0, "Can only open folders on Windows and macOS");//i love this system so much bro i may be the blueprint
        }
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
                    allToggles[engineManager.bodylookup[i]].isOn = true;
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

            foreach (string i in engineManager.bodylookup.Keys)
            {
                try
                {
                    if (!planets.Contains(i))
                    {
                        allToggles[engineManager.bodylookup[i]].isOn = true;
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

            foreach (string i in engineManager.bodylookup.Keys)
            {
                try
                {
                    allToggles[engineManager.bodylookup[i]].isOn = true;
                }
                catch { }
            }
        }
        else
        {
            deslectAll();
        }
    }

    [Serializable] //no clue why i need this
    public class jsonData
    {
        public string objectName;
        public double3 position;
        public double3 velocity;
        public double3 acceleration;
        public double massGM;
        public KeplerianElements kepElem;
    }

    [Serializable]
    public class JsonDataList
    {
        public List<jsonData> data;
    }

    public void exportTheData()
    {
        string fileType = fileFormat.value == 1 ? "csv" : "json";
        string fileName = $"{speedViewer.currentDate.Day}-{speedViewer.currentDate.Month}-{speedViewer.currentDate.Year} Planet data";
        string folderPath = Path.Combine(Application.persistentDataPath, "ExportedData");
        string dataPath = Path.Combine(folderPath, $"{fileName}.{fileType}");

        int counter = 1;
        while (File.Exists(dataPath))
        {
            dataPath = Path.Combine(folderPath, $"{fileName} ({counter}).{fileType}");
            counter++;
        }

        if (fileFormat.value == 1)
        {
            writeCSVFile(dataPath);
        }
        else
        {
            writeJSONFile(dataPath);
        }


            posCheckBox.isOn = false;
        velCheckBox.isOn = false;
        accCheckBox.isOn = false;
        kepDataCheckBox.isOn = false;
        massCheckBox.isOn = false;

        planetsCheckBox.isOn = false;
        moonsCheckBox.isOn = false;
        allCheckBox.isOn = false;
        deselectAllCheckBox.isOn = false;

        showWarning.dispalyNotif(2, "Exported planet data ");
    }

    private void writeCSVFile(string dataPath)
    {
        StreamWriter writer = new StreamWriter(dataPath, false, Encoding.UTF8);
        writer.WriteLine("Object Name,Position,Velocity,Acceleration,Mass (GM),Keplerian Data,Semi-major Axis,Eccentricity,Inclination,Longitude of the Ascending Node,Argument of Periapsis,Mean Anomaly,True Anomaly,Eccentric Anomaly,Standard Gravitational Parameter");
        foreach (Toggle i in allToggles)
        {
            if (i.isOn)
            {
                string name = engineManager.namelookup[Array.IndexOf(allToggles, i)];
                string data = $"{name},";
                int id = Array.IndexOf(allToggles, i);

                PhysicsManager.physicsObject body = physicsManager.fullObjData[id];

                if (posCheckBox.isOn) data += $"\"{body.pos.x}, {body.pos.y}, {body.pos.z}\",";
                else data += "NA,";

                if (velCheckBox.isOn) data += $"\"{body.vel.x}, {body.vel.y}, {body.vel.z}\",";
                else data += "NA,";

                if (accCheckBox.isOn) data += $"\"{body.acc.x}, {body.acc.y}, {body.acc.z}\",";
                else data += "NA,";

                if (massCheckBox.isOn) data += $"{body.mass},";
                else data += "NA,";


                data += "-->,";
                if (kepDataCheckBox.isOn)
                {
                    KeplerianElements kepData = orbitViewer.calcKep(body.pos, body.vel, body.mass, orbitViewer.sunMass);
                    data += $"{kepData.semiMajorA},{kepData.eccentricity},{kepData.inclination},{kepData.longOfAsendingNode},{kepData.argOfPeri},{kepData.meanAnom},{kepData.trueAno},{kepData.eccAnom},{kepData.gravParam},";
                }
                else data += "NA,NA,NA,NA,NA,NA,NA,NA,NA,NA,";

                writer.WriteLine(data);
            }
        }
        writer.Dispose();
    }

    private void writeJSONFile(string dataPath)
    {
        List<jsonData> fullJsonData = new List<jsonData>();
        foreach (Toggle i in allToggles)
        {
            if (i.isOn)
            {
                string name = engineManager.namelookup[Array.IndexOf(allToggles, i)];
                string data = $"{name},";
                int id = Array.IndexOf(allToggles, i);

                PhysicsManager.physicsObject body = physicsManager.fullObjData[id];
                jsonData dataObj = new jsonData();
                dataObj.objectName = name;
                dataObj.position = body.pos;
                dataObj.velocity = body.vel;
                dataObj.acceleration = body.acc;
                dataObj.massGM = body.mass;

                KeplerianElements kepData = orbitViewer.calcKep(body.pos, body.vel, body.mass, orbitViewer.sunMass);
                dataObj.kepElem = kepData;

                fullJsonData.Add(dataObj);
            }
        }


        JsonDataList fixFormat = new JsonDataList();
        fixFormat.data = fullJsonData;

        string jsonString = JsonUtility.ToJson(fixFormat, true);

        File.WriteAllText(dataPath, jsonString);
    }
}
