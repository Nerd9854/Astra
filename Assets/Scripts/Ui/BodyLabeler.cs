using System;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public class BodyLabeler : MonoBehaviour
{
    public GameObject parent;
    public Camera cam;
    public SceneManager SceneManager;
    public EngineManager EngineManager;
    public PhysicsManager PhysicsManager;
    public infoDisplay infoDisplay;
    public GameObject textPrefab;
    public GameObject ringPrefab;
    public float offsetx;
    public float offsety;
    public string[] alwaysShow = { "Sun", "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto" };
    public Color[] labelColors = new Color[10];
    public Transform[] bodyTransforms;
    public GameObject[] labels;
    public GameObject[] rings;
    private int[] indexes;
    private double[] radi;
    private TMPro.TextMeshProUGUI[] lColors;
    private TMPro.TextMeshProUGUI[] rColors;
    private CameraController camCont;
    private double[] lengthsSq;
    public int extraLabels = 10;
    public GameObject[] eLabels;
    public LabelButton[] labelButtons;
    public TMPro.TextMeshProUGUI[] labelTexts;
    public GameObject[] eRings;

    private int indexLength;

    public void labelInit()
    {
        camCont = cam.GetComponent<CameraController>();
        int length = alwaysShow.Length;
        labels = new GameObject[length];
        rings = new GameObject[length];
        eLabels = new GameObject[extraLabels];
        eRings = new GameObject[extraLabels];
        labelButtons = new LabelButton[extraLabels];
        labelTexts = new TMPro.TextMeshProUGUI[extraLabels];
        inUse = new double[extraLabels];
        bodyTransforms = new Transform[length];
        indexes = new int[length];
        radi = new double[length];
        lColors = new TMPro.TextMeshProUGUI[length];
        rColors = new TMPro.TextMeshProUGUI[length];
        indexLength = length;
        lengthsSq = new double[PhysicsManager.fullObjData.Length];

        int indexNum = 0;
        foreach (string i in alwaysShow)
        {
            int lookupIndex = EngineManager.bodylookup[i.ToUpper()];

            addLabel(i, indexNum, lookupIndex);
            addRing(i, indexNum, lookupIndex);
            setupArray(indexNum, lookupIndex);
            indexNum++;
        }

        for (int i = 0; i < extraLabels; i++)
        {
            eLabels[i] = Instantiate(textPrefab, parent.transform);
            labelButtons[i] = eLabels[i].GetComponent<LabelButton>();
            labelButtons[i].buttonInit();
            labelButtons[i].infoDisplay = infoDisplay;
            eLabels[i].name = $"Extra Label {i}";
            labelTexts[i] = eLabels[i].GetComponent<TMPro.TextMeshProUGUI>();
            labelTexts[i].color = Color.white;

            eRings[i] = Instantiate(ringPrefab, parent.transform);
            eRings[i].GetComponent<TMPro.TextMeshProUGUI>().color = Color.white;
            eRings[i].name = $"Extra Ring {i}";
        }
    }

    private void setupArray(int indexNum, int lookupIndex)
    {
        GameObject currBody = SceneManager.allBodys[lookupIndex];

        indexes[indexNum] = lookupIndex;
        bodyTransforms[indexNum] = SceneManager.allBodys[lookupIndex].transform;
        radi[indexNum] = currBody.GetComponent<Body>().avgRadius;
        lColors[indexNum] = labels[indexNum].GetComponent<TMPro.TextMeshProUGUI>();
        rColors[indexNum] = rings[indexNum].GetComponent<TMPro.TextMeshProUGUI>();
    }

    private void addLabel(string name, int indexNum, int lookupIndex)
    {
        labels[indexNum] = Instantiate(textPrefab, parent.transform);
        labels[indexNum].GetComponent<TMPro.TextMeshProUGUI>().text = name;
        labels[indexNum].GetComponent<TMPro.TextMeshProUGUI>().color = labelColors[indexNum];
        labels[indexNum].GetComponent<LabelButton>().buttonInit();
        labels[indexNum].GetComponent<LabelButton>().name = name.ToUpper();
        labels[indexNum].GetComponent<LabelButton>().infoDisplay = infoDisplay;
        labels[indexNum].name = $"{SceneManager.allBodys[lookupIndex].name} Label";
    }

    private void addRing(string name, int indexNum, int lookupIndex)
    {
        rings[indexNum] = Instantiate(ringPrefab, parent.transform);
        rings[indexNum].GetComponent<TMPro.TextMeshProUGUI>().color = labelColors[indexNum];
        rings[indexNum].name = $"{SceneManager.allBodys[lookupIndex].name} Ring";
    }

    public double startFadeFactor;
    public double endFadeFactor;

    public void updateLables(int framecount)
    {
        if (framecount == 0) getLengths();
        for (int i = 0; i < indexLength; i++)
        {
            bool hide = false;
            Transform targetBody = bodyTransforms[i];
            Vector3 labelPos = Vector3.zero;
            Vector3 ringPos = Vector3.zero;
            float alpha = 1f;

            if (targetBody.gameObject.activeInHierarchy)
            {
                double bodyRadius = radi[i];
                Vector3 screenPos = cam.WorldToScreenPoint(targetBody.position);
                if (onScreen(screenPos))
                {
                    double distThreshold = bodyRadius * startFadeFactor;
                    if (screenPos.z > distThreshold)
                    {
                        labelPos = new Vector3(screenPos.x + offsetx, screenPos.y + offsety, 0f);
                        ringPos = new Vector3(screenPos.x, screenPos.y, 0f);
                    }
                    else
                    {
                        double fadeThreshold = bodyRadius * endFadeFactor;
                        if (screenPos.z > fadeThreshold)
                        {
                            double diff = distThreshold - fadeThreshold;
                            double normalisedScreenPos = distThreshold - screenPos.z;
                            alpha = (float)(1 - (normalisedScreenPos / diff));

                            labelPos = new Vector3(screenPos.x + offsetx, screenPos.y + offsety, 0f);
                            ringPos = new Vector3(screenPos.x, screenPos.y, 0f);
                        }
                        else hide = true;
                    }
                }
                else hide = true;
            }
            else hide = true;


            labels[i].transform.position = labelPos;
            rings[i].transform.position = ringPos;

            Color lCol = lColors[i].color;
            Color rCol = rColors[i].color;
            if(!(lCol.a == 1 && alpha == 1))
            {
                lCol.a = alpha;
                rCol.a = alpha;

                lColors[i].color = lCol;
                rColors[i].color = rCol;
            }

            //labels[i].gameObject.SetActive(!hide);
            //rings[i].gameObject.SetActive(!hide);
        }
    }

    bool onScreen(Vector3 pos)
    {
        return pos.z > 0 &&
               pos.x >= 0 &&
               pos.x <= Screen.width &&
               pos.y >= 0 &&
               pos.y <= Screen.height;
    }



    public double cullSq;
    private double[] inUse;
    private void getLengths()
    {
        double3 campos = camCont.worldPos;
        int length = PhysicsManager.fullObjData.Length;
        int[] top10 = new int[extraLabels];
        int currLabel = 0;
        bool hide = true;
        for (int i = 0; i < length; i++)
        {
            hide = true;
            double3 pos = campos - PhysicsManager.fullObjData[i].pos;
            double sq = pos.x * pos.x + pos.y * pos.y + pos.z * pos.z;
            eLabels[currLabel].transform.position = new Vector3(100, 0, 0);
            eRings[currLabel].transform.position = Vector3.zero;

            lengthsSq[i] = sq;
            if (sq < cullSq)
            {
                string name = EngineManager.namelookup[i];
                if (!alwaysShow.Contains(char.ToUpper(name[0]) + name[1..].ToLower()) && !name.Contains("BARYCENTER"))
                {
                    Vector3 screenPos = cam.WorldToScreenPoint(SceneManager.allBodys[i].transform.position);
                    if (onScreen(screenPos))
                    {
                        inUse[currLabel] = 0;
                        if (currLabel < extraLabels)
                        {
                            eLabels[currLabel].transform.position = new Vector3(screenPos.x + offsetx, screenPos.y + offsety, 0f);
                            eRings[currLabel].transform.position = new Vector3(screenPos.x, screenPos.y, 0f);
                            labelTexts[currLabel].text = EngineManager.namelookup[i];
                            labelButtons[currLabel].name = EngineManager.namelookup[i];
                            inUse[currLabel] = 1;
                            hide = false;
                            currLabel++;
                        }
                    }
                }
            }
        }
    }
}
