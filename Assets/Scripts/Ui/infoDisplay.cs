using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using static OrbitViewer;
using static PhysicsManager;
using static UnityEngine.Rendering.DebugUI;

public class infoDisplay : MonoBehaviour
{
    public GameObject global;
    public PhysicsManager physicsManager;
    public OrbitViewer orbitViewer;
    public EngineManager engineManager;
    public RectTransform moveButton;
    public RectTransform basePlate;
    public RectTransform globalRect;
    public int numOfLab;
    public string[] exlude = new string[] { "InfoBase" };
    public string target = "Earth";
    private int targetID;

    public TextMeshProUGUI[] texts;
    public Material planetMat;

    // Start is called before the first frame update
    public void infoDisplayInit()
    {
        numOfLab = global.GetComponentsInChildren<Transform>(false).Length - 1;
        texts = new TextMeshProUGUI[numOfLab];
        updateTargetID();

        int curr = 0;
        foreach (Transform child in global.transform)
        {
            string name = child.gameObject.name;
            if (!exlude.Contains(name))
            {
                if (name == "Pos" | name == "Vel" | name == "Acc")
                {
                    foreach (Transform childchild in child)
                    {
                        texts[curr] = childchild.GetComponent<TMPro.TextMeshProUGUI>();
                        curr++;
                    }
                    curr--;
                }
                else texts[curr] = child.GetComponent<TMPro.TextMeshProUGUI>();

                curr++;
            }
        }
        initMenu();
    }



    public void updateTargetID()
    {
        targetID = engineManager.bodylookup[target];
    }


    public void updatePosVelAcc(double3 pos, double3 vel, double3 acc)
    {
        updateSingle(pos, 1);
        updateSingle(vel, 7);
        updateSingle(acc, 13);
    }

    private void updateSingle(double3 data, int index)
    {
        convertOne(data.x, index);
        convertOne(data.y, index + 2);
        convertOne(data.z, index + 4);
    }

    private void convertOne(double data, int index)
    {
        string sign = (data < 0)? "-" : "+";
        string number = math.abs(data).ToString("N0");

        texts[index].text = number;
        texts[index + 1].text = sign;
    }



    private void updateDefo()
    {
        texts[0].text = char.ToUpper(target[0]) + target[1..].ToLower();
        texts[19].text = convertMass(engineManager.GMValues[targetID]);

        physicsObject data = physicsManager.fullObjData[targetID];
        updateKep(data);

        planetMat.SetTexture("_Texture", getTex());
    }

    private string convertMass(double mass)
    {
        mass /= engineManager.g;
        string sci = mass.ToString("0.###E0");
        sci = sci.Replace("E", " × 10^");
        return sci;
    }

    private string[] paths = { "Planets", "DwafPlanets", "Moons" };

    private Texture2D getTex()
    {
        Texture2D tex;
        string uppTar = target.ToUpper();
        foreach (string path in paths)
        {
            tex = Resources.Load<Texture2D>($"Textures/{path}/{uppTar}");
            if (tex != null)
            {
                return tex;
            }
        }
        return Resources.Load<Texture2D>($"Textures/Misc/Missing Texture Texture");
    }

    public void updateKep(physicsObject data)
    {
        KeplerianElements kepData = orbitViewer.calcKep(data.pos, data.vel, data.mass, orbitViewer.sunMass);
        texts[22].text = $"{convertMass(kepData.semiMajorA)}"; //same format as mass
        texts[23].text = $"{Math.Truncate(kepData.eccentricity * 10000000) / 10000000}";
        texts[24].text = convertRadToDeg(kepData.inclination, 100);
        texts[25].text = convertRadToDeg(kepData.longOfAsendingNode, 100);
        texts[26].text = convertRadToDeg(kepData.argOfPeri, 100);
        texts[27].text = convertRadToDeg(kepData.trueAno, 100);
    }

    public string convertRadToDeg(double number, int deciPlaces)
    {
        return $"{math.trunc(math.degrees(number) * deciPlaces) / deciPlaces}°";
    }

    public void infoUpdate()
    {
        physicsObject data = physicsManager.fullObjData[targetID];
        updatePosVelAcc(data.pos, data.vel, data.acc);
    }

    public void initMenu()
    {
        updateTargetID();
        updateDefo();
        infoUpdate();
        updateKep(physicsManager.fullObjData[targetID]);
    }



    public float scaleF = 0.9f;
    public float multi = -1.01683501684f; //as there is 10px of padding we need to have a weidr number (604/594)
    public float slideSpeed = 0.4f;

    private bool slideLeft = true;
    public void move()
    {
        if (!slideLeft)
        {
            initMenu();
        }
        Coroutine slideA = StartCoroutine(slide(slideLeft));
        slideLeft = !slideLeft;
    }

    private IEnumerator slide(bool slideLeft)
    {
        float timeE = 0f;
        float basewidth = basePlate.rect.width * scaleF;
        float lMulti = slideLeft ? multi : -multi;
        float finalPos = slideLeft ? basewidth * (multi) : 0;

        while (timeE < slideSpeed)
        {
            timeE += Time.deltaTime;
            float t = timeE / slideSpeed;
            t = Mathf.SmoothStep(0, lMulti, t);

            float movePos = basewidth * t;
            if (!slideLeft) movePos -= basewidth;
            globalRect.anchoredPosition = new Vector2(movePos, 0);

            yield return null;
        }

        globalRect.anchoredPosition = new Vector2(finalPos, 0);
    }
}
