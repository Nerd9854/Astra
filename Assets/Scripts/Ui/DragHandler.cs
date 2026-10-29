using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private Transform itemTransform;
    private Transform contentContainer;
    public Transform canvasTransform;
    private int originalSiblingIndex;

    void Awake()
    {
        itemTransform = transform.parent;
        contentContainer = itemTransform.parent;
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