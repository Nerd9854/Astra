using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using UnityEditor.Purchasing;
using UnityEngine;

public class speedViewer : MonoBehaviour
{
    public TMP_InputField targetspeed;
    public TMP_Dropdown speedMulti;
    public TMPro.TextMeshProUGUI date;
    public TMPro.TextMeshProUGUI currentSpeed;
    public TMPro.TextMeshProUGUI currentSpeedMulti;
    public PhysicsManager physicsManager;

    public int targetSpeedS = 0;
    public int targetSpeedMulti = 60;
    private int[] multi = new int[] { 60, 3600, 86400, 2592000 };
    private string[] months = new string[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
    DateTime dateT;

    void OnEnable()
    {
        targetspeed.onValueChanged.AddListener(OnTextChanged);
        speedMulti.onValueChanged.AddListener(OnDropdownChanged);
    }

    void OnDisable()
    {
        targetspeed.onValueChanged.RemoveListener(OnTextChanged);
        speedMulti.onValueChanged.RemoveListener(OnDropdownChanged);
    }

    private void OnTextChanged(string newText)
    {
        if (newText == "")
        {
            newText = "0";
        }

        try
        {
            targetSpeedS = int.Parse(newText);
            updatePhysicsMan();
        }
        catch { }
    }

    private void OnDropdownChanged(int index)
    {
        try
        {
            targetSpeedMulti = multi[index];
            updatePhysicsMan();
            currentSpeedMulti.text = speedMulti.options[index].text.ToLower();
        }
        catch { }
    }

    public void updateDate(string closestDate)
    {
        string[] dateSplit = closestDate.Split("-");
        string month = months[int.Parse(dateSplit[1]) - 1];

        date.text = $"00:00\n{dateSplit[0]} {month} {dateSplit[2]}";
        dateT = new DateTime(int.Parse(dateSplit[2]), int.Parse(dateSplit[1]), int.Parse(dateSplit[0]));
        timeSinceStart = 0;
    }

    private void updatePhysicsMan()
    {
        physicsManager.maxH = math.max(targetSpeedS * targetSpeedMulti, 1);
    }

    public double timeSinceStart = 0;

    public DateTime currentDate;
    public void speedupdate()
    {
        timeSinceStart += physicsManager.h;
        currentSpeed.text = $"{(physicsManager.h / targetSpeedMulti) / Time.deltaTime:F2}"; //fancy stuff
        physicsManager.maxH = math.max((targetSpeedS * targetSpeedMulti) / Time.deltaTime, 1);

        DateTime newDateT = dateT.AddSeconds(timeSinceStart);
        currentDate = newDateT;
        date.text = $"{newDateT:HH:mm}\n{newDateT.Day} {months[newDateT.Month - 1]} {newDateT.Year}";
    }
}