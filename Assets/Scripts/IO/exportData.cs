using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class exportData : MonoBehaviour
{
    public showWarning showWarning;
    public EngineManager engineManager;
    public GameObject clickPref;
    public GameObject scrollbar;

    public Toggle posCheckBox;
    public Toggle velCheckBox;
    public Toggle accCheckBox;
    public Toggle kepDataCheckBox;
    public Toggle massCheckBox;

    public Toggle planetsCheckBox;
    public Toggle moonsCheckBox;
    public Toggle allCheckBox;
    public Toggle deselectAllCheckBox;

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

        udpateBodys();


        //openFolder();
    }

    private void udpateBodys()
    {
        foreach (string i in engineManager.bodylookup.Keys)
        {
            GameObject body = Instantiate(clickPref, scrollbar.transform);
            body.name = i;
            body.transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>().text = i;
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

    }

    private void planetChanged(bool value)
    {
        if (value)
        {
            deslectAll();
        }
    }

    private void moonChanged(bool value)
    {
        if (value)
        {
            deslectAll();
        }
    }

    private void allChanged(bool value)
    {
        if (value)
        {
            deslectAll();
        }
        else
        {
            deslectAll();
        }
    }
}
