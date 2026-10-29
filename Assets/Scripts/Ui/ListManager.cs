using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class ListManager : MonoBehaviour
{
    // from a tutorial, being rushed so baciaclly copyed/pasted + a few edits
    public GameObject itemPrefab;
    public Transform contentPanel;
    public Transform canvasTransform;

    private List<GameObject> spawnedItems = new List<GameObject>();
    private int itemCounter = 0;

    public void AddItem()
    {
        itemCounter++;
        GameObject newItem = Instantiate(itemPrefab, contentPanel);
        newItem.name = "Item_" + itemCounter;

        //newItem.GetComponentInChildren<TMPro.TMP_Dropdown>().name = "List Item #" + itemCounter;
        newItem.transform.Find("Drag").GetComponent<DragHandler>().canvasTransform = canvasTransform; // 0% jank
        Button removeBtn = newItem.transform.Find("RemoveButton").GetComponent<Button>();
        removeBtn.onClick.AddListener(() => RemoveItem(newItem));

        spawnedItems.Add(newItem);
    }

    public void RemoveItem(GameObject item)
    {
        spawnedItems.Remove(item);
        Destroy(item);
    }

    public void PrintCurrentPositions()
    {
        Debug.Log("--- Current List Order ---");

        for (int i = 0; i < contentPanel.childCount; i++)
        {
            Transform child = contentPanel.GetChild(i);
            Debug.Log($"Position {i + 1}: {child.name}");
        }
    }
}
