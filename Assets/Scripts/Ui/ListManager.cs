using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using TMPro;

public class ListManager : MonoBehaviour
{
    // from a tutorial, being rushed so baciaclly copyed/pasted + a few edits
    public EngineManager engineManager;
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
        newItem.transform.Find("Drag").GetComponent<DragHandler>().updateValues(engineManager); // 0% jank
        Button removeBtn = newItem.transform.Find("RemoveButton").GetComponent<Button>();
        removeBtn.onClick.AddListener(() => RemoveItem(newItem));

        spawnedItems.Add(newItem);
    }

    public void RemoveItem(GameObject item)
    {
        spawnedItems.Remove(item);
        Destroy(item);
    }

    public string[] addInterSteps(string startPos, string endPos)
    {
        List<string> points = new List<string>();

        points.Add(startPos);
        for (int i = 0; i < contentPanel.childCount; i++)
        {
            TMP_Dropdown drop = contentPanel.GetChild(i).transform.Find("Text").GetComponent<TMPro.TMP_Dropdown>();
            points.Add(drop.options[drop.value].text.ToUpper());
        }
        points.Add(endPos);

        return points.ToArray();
    }
}
