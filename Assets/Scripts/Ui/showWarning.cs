using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class showWarning : MonoBehaviour
{
    public RectTransform warning;
    public int showTime;

    void Update()
    {

        if (!sliding) { StartCoroutine(slideIn()); }
    }

    public float multi;
    public float slideSpeed = 0.4f;
    public bool sliding = false;

    private IEnumerator slideIn()
    {
        sliding = true;
        Debug.Log("IN");
        StartCoroutine(slide(false));

        yield return new WaitForSeconds(showTime + slideSpeed);

        Debug.Log("OUT");
        StartCoroutine(slide(true));
        yield return new WaitForSeconds(slideSpeed);
        sliding = false;
    }

    private IEnumerator slide(bool slideIn)
    {
        float timeE = 0f;
        float basewidth = warning.rect.width;
        float lMulti = slideIn ? multi : -multi;
        float finalPos = slideIn ? 340 : -10;

        while (timeE < slideSpeed)
        {
            timeE += Time.deltaTime;
            float t = timeE / slideSpeed;
            t = Mathf.SmoothStep(0, lMulti, t);

            float movePos = basewidth * t;
            if (!slideIn) movePos -= basewidth;
            warning.anchoredPosition = new Vector2(movePos, 20);

            yield return null;
        }

        warning.anchoredPosition = new Vector2(finalPos, 20);
    }
}
