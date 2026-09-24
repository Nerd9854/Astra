using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.UI;

public class showWarning : MonoBehaviour
{
    public RectTransform warning;
    public TMPro.TextMeshProUGUI messageG;
    public TMPro.TextMeshProUGUI top;
    public Image back;
    public Image line;
    public int showTime;

    public string[] texts = new string[] { "Warning", "Info", "Success" };
    public Color[] colors = new Color[] { new Color32(0xFF, 0x4B, 0x1E, 0xFF), new Color32(0x46, 0x70, 0xFF, 0xFF), new Color32(0x16, 0xFF, 0x7A, 0xFF)};
    public Color[] tColors = new Color[] { new Color32(0xFF, 0x4B, 0x1E, 0x80), new Color32(0x46, 0x70, 0xFF, 0x80), new Color32(0x16, 0xFF, 0x7A, 0x80)};

    public float slideSpeed = 0.4f;
    public bool sliding = false;
    public bool multiple = false;

    private Queue<(int type, string message)> messages = new Queue<(int type, string message)> ();

    public void dispalyNotif(int type, string message) //0 is warning, 1 is info, 2 is succ
    {
        if (!sliding)
        {
            messageG.text = message;
            top.text = texts[type];
            back.color = tColors[type];
            line.color = colors[type]; 
            StartCoroutine(slideIn());
        }
        else
        {
            multiple = true;
            messages.Enqueue((type, message));
        }
    }

    private IEnumerator slideIn()
    {
        sliding = true;
        StartCoroutine(slide(true));

        yield return new WaitForSeconds(showTime + slideSpeed);

        StartCoroutine(slide(false));
        yield return new WaitForSeconds(slideSpeed);
        sliding = false;
    }

    private IEnumerator slide(bool slideIn)
    {
        float timeE = 0f;
        int startPos = slideIn? 340 : -10;
        int endPos = slideIn? -10 : 340;

        warning.anchoredPosition = new Vector2(startPos, 20);

        while (timeE < slideSpeed)
        {
            timeE += Time.deltaTime;
            float t = timeE / slideSpeed;
            t = Mathf.SmoothStep(0, 1, t);

            if (slideIn) t = 1 - t;
            float movePos = (350 * t) - 10;
            warning.anchoredPosition = new Vector2(movePos, 20);

            yield return null;
        }

        warning.anchoredPosition = new Vector2(endPos, 20);
    }

    void Update()
    {
        if (multiple && !sliding)
        {
            (int type, string message) messagee = messages.Dequeue();
            messageG.text = messagee.message;
            top.text = texts[messagee.type];
            back.color = tColors[messagee.type];
            line.color = colors[messagee.type];
            StartCoroutine(slideIn());

            if (messages.Count == 0) multiple = false;
        }
    }
}
