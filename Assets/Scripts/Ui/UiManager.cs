using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UiManager : MonoBehaviour
{
    public BodyLabeler BodyLabeler;
    public OrbitViewer OrbitViewer;
    public OrbtiViewerCamera OrbtiViewerCamera;
    public infoDisplay infoDisplay;
    public GameObject settingsPanel;
    public GameObject selectDateObj;
    public SelectDateMan selectDateMan;
    public GameObject taskbarCanvas;

    public ShowHideDropDown tab1;
    public ShowHideDropDown tab2;
    //public ShowHideDropDown tab3;
    //public ShowHideDropDown tab4;

    public void UiInit()
    {
        selectDateMan = selectDateObj.GetComponent<SelectDateMan>();
        BodyLabeler.labelInit();
        infoDisplay.infoDisplayInit();
        OrbitViewer.orbitInit();
        OrbtiViewerCamera.camInit();
    }

    public int frameNum = 1;//skip the first one
    public int frameWait = 10;
    public void UIUpdate()
    {
        BodyLabeler.updateLables(frameNum);
        OrbitViewer.orbitUpdate();
        infoDisplay.infoUpdate();
        OrbtiViewerCamera.camUpdate();

        tab1.buttonUpdate();
        tab2.buttonUpdate();

        frameNum = (frameNum + 1) % frameWait;
    }

    private GameObject selctDatePre;
    private bool selectDateSpawned = false;
    public void selectDate()
    {
        if (!selectDateSpawned)
        {
            selectDateSpawned = true;
            selectDateObj.SetActive(true);
            settingsPanel.SetActive(true);
        }
    }

    public void closeSelectDate()
    {
        if (selectDateSpawned)
        {
            selectDateSpawned = false;
            selectDateObj.SetActive(false);
            settingsPanel.SetActive(false);
        }
    }
}
