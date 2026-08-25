using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;


public class ShowHideDropDown : MonoBehaviour, IPointerEnterHandler
{
    public GameObject dropdown;
    public GameObject border;
    public void showHideDrop()
    {
        bool isActive = dropdown.activeSelf;
        dropdown.SetActive(!isActive);
        border.SetActive(!isActive);
    }

    public void OnPointerEnter(PointerEventData data) //from a tutorial
    {
        ShowHideDropDown[] all = FindObjectsOfType<ShowHideDropDown>();
        foreach (var tab in all)
        {
            if (tab != this && tab.dropdown.activeSelf)
            {
                tab.dropdown.SetActive(false);
                tab.border.SetActive(false);
                dropdown.SetActive(true);
                border.SetActive(true);
                break;
            }
        }
    }

    public void buttonUpdate()
    {
        if (dropdown.activeSelf && Input.GetMouseButtonDown(0))
        {
            if(!RectTransformUtility.RectangleContainsScreenPoint(GetComponent<RectTransform>(), Input.mousePosition) && !RectTransformUtility.RectangleContainsScreenPoint(dropdown.GetComponent<RectTransform>(), Input.mousePosition))
            {
                dropdown.SetActive(false);
                border.SetActive(false);
            }
        }
    }
}
