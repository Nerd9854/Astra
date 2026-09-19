using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FixScale : MonoBehaviour
{
    private void OnEnable()
    {
        RectTransform canvasRect = GetComponent<RectTransform>();

        if (canvasRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
        }
    }
}
