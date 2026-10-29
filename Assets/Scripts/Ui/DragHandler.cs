using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private Transform itemTransform;
    private Transform contentContainer;
    public Transform canvasTransform;
    public TMP_Dropdown dropdown;
    private int originalSiblingIndex;

    void Awake()
    {
        itemTransform = transform.parent;
        contentContainer = itemTransform.parent;
    }

    public void updateValues(EngineManager engineManager)
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
        dropdown.ClearOptions();

        dropdown.AddOptions(allNames);

        dropdown.RefreshShownValue();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        originalSiblingIndex = itemTransform.GetSiblingIndex();

        if (itemTransform.TryGetComponent<CanvasGroup>(out CanvasGroup group))
        {
            group.blocksRaycasts = false;
        }

        itemTransform.SetParent(canvasTransform, true);
        itemTransform.SetAsLastSibling();
    }


    public void OnDrag(PointerEventData eventData)
    {
        itemTransform.position = eventData.position;

        int newSiblingIndex = contentContainer.childCount;

        for (int i = 0; i < contentContainer.childCount; i++)
        {
            if (itemTransform.position.y > contentContainer.GetChild(i).position.y)
            {
                newSiblingIndex = i;
                break;
            }
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        itemTransform.SetParent(contentContainer, true);

        int finalSiblingIndex = contentContainer.childCount - 1;
        for (int i = 0; i < contentContainer.childCount; i++)
        {
            if (contentContainer.GetChild(i) == itemTransform) continue;

            if (itemTransform.position.y > contentContainer.GetChild(i).position.y)
            {
                finalSiblingIndex = i;
                break;
            }
        }

        itemTransform.SetSiblingIndex(finalSiblingIndex);

        if (itemTransform.TryGetComponent<CanvasGroup>(out CanvasGroup group))
        {
            group.blocksRaycasts = true;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(contentContainer.GetComponent<RectTransform>());
    }
}