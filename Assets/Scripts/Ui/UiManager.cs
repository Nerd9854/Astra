using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UiManager : MonoBehaviour
{
    public BodyLabeler BodyLabeler;
    public OrbitViewer OrbitViewer;
    public OrbtiViewerCamera OrbtiViewerCamera;
    public infoDisplay infoDisplay;

    public void UiInit()
    {
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

        frameNum = (frameNum + 1) % frameWait;
    }
}
