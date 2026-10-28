using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Windows.Speech;
//h
public class simulatingTimeUI : MonoBehaviour
{
    public PhysicsManager physicsManager;
    public SpacecraftManager spacecraftManager;
    public RectTransform estTime;
    public Image progressBar;
    public TMPro.TextMeshProUGUI messageG;
    public TMPro.TextMeshProUGUI top;
    public int showTime;

    public float slideSpeed = 0.4f;
    public bool sliding = false;
    public bool multiple = false;
    public int currHeight = 20;

    public void updatePos(bool up)
    {
        estTime.anchoredPosition = new Vector2(estTime.anchoredPosition.x, up? 93 : 20);//techhh
        currHeight = up ? 93 : 20;
    }

    public IEnumerator showProgress(double launchEpoch, double totalTOF, double startT, bool multiple)
    {
        sliding = true;
        top.text = $"Simulating:<color=#B2B2B2><size=90%>{spacecraftManager.convertTime(launchEpoch - startT)}</size></color>";

        StartCoroutine(slide(true));

        yield return startLoadingBar(launchEpoch);
        if(multiple) yield return startLoadingBar(totalTOF);

        StartCoroutine(slide(false));
        sliding = false;
    }

    private IEnumerator startLoadingBar(double launchEpoch)
    {
        top.text = $"Simulating:<color=#B2B2B2><size=90%>{spacecraftManager.convertTime(launchEpoch - physicsManager.t)}</size></color>";
        while (physicsManager.t < launchEpoch)
        {
            double speed = 0;
            double time = 0;
            int acc = 15;
            for (int i = 0; i < acc; i++)
            {
                yield return null;
                speed += physicsManager.h;
                time += Time.deltaTime;
            }
            double estimatedSpeed = speed * (1.0 / time);
            string estTime = spacecraftManager.convertTime((launchEpoch - physicsManager.t) / estimatedSpeed);

            top.text = $"Simulating:<color=#B2B2B2><size=90%>{spacecraftManager.convertTime(launchEpoch - physicsManager.t)}</size></color>";
            messageG.text = $"Etimated <color=#B2B2B2><size=90%>{estTime}</size></color> remaning";

            progressBar.rectTransform.localScale = new Vector3((float)(physicsManager.t / launchEpoch), 1, 1);
            yield return null;
        }
    }

    private IEnumerator slide(bool slideIn)
    {
        float timeE = 0f;
        int startPos = slideIn ? 340 : -10;
        int endPos = slideIn ? -10 : 340;

        estTime.anchoredPosition = new Vector2(startPos, currHeight);

        while (timeE < slideSpeed)
        {
            timeE += Time.deltaTime;
            float t = timeE / slideSpeed;
            t = Mathf.SmoothStep(0, 1, t);

            if (slideIn) t = 1 - t;
            float movePos = (350 * t) - 10;
            estTime.anchoredPosition = new Vector2(movePos, currHeight);

            yield return null;
        }

        estTime.anchoredPosition = new Vector2(endPos, currHeight);
    }
}