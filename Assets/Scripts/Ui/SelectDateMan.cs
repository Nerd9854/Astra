using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SelectDateMan : MonoBehaviour
{
    public TMP_Dropdown dayDrop;
    public TMP_Dropdown monthDrop;
    public TMP_Dropdown yearDrop;
    public TMP_InputField hours;
    public TMP_InputField mins;
    public Toggle exactDate;
    public GameObject scrollBarContent;
    public GameObject textPrefab;
    public TextMeshProUGUI selcectedDate;
    public TextMeshProUGUI simulate;
    public TextMeshProUGUI startDate;
    public TextMeshProUGUI endDate;
    public SelectDateMan selectDateMan;

    public int currDay;
    public string currMonth;
    public int currYear;
    public string chosenDate;
    public int currHour;
    public int currMin;

    private string[] months = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
    public List<Vector3Int> dates = new List<Vector3Int>();
    public List<string> showDates = new List<string>();
    private List<int> years = new List<int>();

    public bool selectedDataDate = false;
    public bool simulateExactDate = false;

    private void Awake()
    {
        string path = $"{Application.streamingAssetsPath}/BodyData/pos";

        string[] allPaths = Directory.GetFiles(path);
        string[] temp = allPaths.Select(path => Path.GetFileName(path)).Where(name => !name.EndsWith(".meta")).ToArray();

        int yearInt;
        string[] dateStr;
        foreach (string i in temp)
        {
            dateStr = i.Substring(0, 10).Split("-");
            dates.Add(new Vector3Int(int.Parse(dateStr[0]), int.Parse(dateStr[1]), int.Parse(dateStr[2])));
            yearInt = int.Parse(i.Substring(6, 4));
            if (!years.Contains(yearInt))
            {
                years.Add(yearInt);
            }
        }
        updateYears();
        updatedates();
    }

    public void OnEnable()
    {
        dayDrop.onValueChanged.AddListener(dayDropChanged);
        monthDrop.onValueChanged.AddListener(monthDropChanged);
        yearDrop.onValueChanged.AddListener(yearDropChanged);
        hours.onValueChanged.AddListener(hourChanged);
        mins.onValueChanged.AddListener(minsChanged);
    }
    void OnDisable()
    {
        dayDrop.onValueChanged.RemoveListener(dayDropChanged);
        monthDrop.onValueChanged.RemoveListener(monthDropChanged);
        yearDrop.onValueChanged.RemoveListener(yearDropChanged);
        hours.onValueChanged.RemoveListener(hourChanged);
        mins.onValueChanged.RemoveListener(minsChanged);
    }

    private void dayDropChanged(int index)
    {
        string rawInput = dayDrop.options[index].text;

        currDay = rawInput == " " ? -1 : int.Parse(rawInput);
        updatedates();

    }

    private void monthDropChanged(int index)
    {
        string rawInput = monthDrop.options[index].text;

        currMonth = rawInput;
        if(!(currMonth == " ")) updateDays();
        updatedates();
    }

    private void yearDropChanged(int index)
    {
        string rawInput = yearDrop.options[index].text;

        currYear = rawInput == " " ? -1 : int.Parse(rawInput);
        updatedates();
    }

    private void hourChanged(string value)
    {
        if (int.TryParse(value, out int intValue))
        {
            currHour = intValue;
            if (intValue > 24)
            {
                hours.text = "24";
                currHour = 24;
            }
            if (intValue < 0)
            {
                hours.text = "0";
                currHour = 0;
            }
            updateTargets();
        }
    }

    private void minsChanged(string value)
    {
        if (int.TryParse(value, out int intValue))
        {
            currMin = intValue;
            if (intValue >= 60)
            {
                mins.text = "59";
                currMin = 59;
            }
            if (intValue < 0)
            {
                mins.text = "0";
                currMin = 0;

            }
            updateTargets();
        }
    }






    private int convertMonthToDays(string month, int year)
    {
        return DateTime.DaysInMonth(year, Array.IndexOf(months, month) + 1);
    }

    private void updateDays()
    {
        int year = currYear == -1 ? 2001 : currYear;
        int numOfDays = convertMonthToDays(currMonth, year);

        dayDrop.ClearOptions();
        List<string> days = new List<string>();

        days.Add(" ");
        for (int i = 1; i <= numOfDays; i++)
        {
            days.Add(i.ToString());
        }
        dayDrop.AddOptions(days);
        dayDrop.RefreshShownValue();
    }

    private void updateYears()
    {
        yearDrop.ClearOptions();
        List<string> rawYears = new List<string>();

        rawYears.Add(" ");

        foreach(int i in years)
        {
            rawYears.Add(i.ToString());
        }

        yearDrop.AddOptions(rawYears);
        yearDrop.RefreshShownValue();
    }

    private string convertDayToStRdRdSt(int day) { return day == 1 || day == 21 || day == 31 ? "st" : day == 2 || day == 22 ? "nd" : day == 3 || day == 23 ? "rd" : "th"; }

    private void updatedates()
    {
        List<string> filteredDates = new List<string>();
        foreach(Vector3Int i in dates)
        {
            if (currMonth == " " || currMonth == months[Math.Abs(i.y - 1)])
            {
                if (currYear == -1 || currYear == i.z)
                {
                    filteredDates.Add($"1st {months[i.y - 1]} {i.z}");
                }
            }
        }
        showDates = filteredDates;

        int existingCount = scrollBarContent.transform.childCount;
        int requiredCount = filteredDates.Count;

        for (int i = 0; i < Math.Max(existingCount, requiredCount); i++)
        {
            GameObject text;

            if (i < requiredCount)
            {
                if (i < existingCount) text = scrollBarContent.transform.GetChild(i).gameObject;
                else text = Instantiate(textPrefab, scrollBarContent.transform);

                text.SetActive(true);
                text.name = filteredDates[i];

                var textT = text.transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>();
                DateButton button = text.transform.GetComponent<DateButton>();

                button.date = filteredDates[i];
                button.SelectDateMan = selectDateMan;

                textT.text = filteredDates[i];
            }
            else scrollBarContent.transform.GetChild(i).gameObject.SetActive(false);

            scrollBarContent.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (float)(requiredCount * 12 + 10));
        }

        updateTargets();
    }

    public void updateTargets()
    {
        if (chosenDate == "Not Selected")
        {
            simulate.text = $"Not Selected";
            endDate.text = $"Not Selected";
            return;
        }
        string[] splitted = chosenDate.Split(" ");
        int day = currDay == -1 ? 1 : currDay;
        string month = splitted[1];
        int year =  int.Parse(splitted[2]);
        string rdstnd = convertDayToStRdRdSt(day);

        DateTime startTime = new DateTime(year, Array.IndexOf(months, month) + 1, 1, 0, 0, 0);
        DateTime endTime = new DateTime(year, Array.IndexOf(months, month) + 1, day, currHour, currMin, 0);
        TimeSpan diffrence = endTime - startTime;

        simulate.text = $"{diffrence.Days} days {diffrence.Hours.ToString("D2")} hours {diffrence.Minutes.ToString("D2")} mins";
        endDate.text = $"{day}{rdstnd} {month} {year} {currHour.ToString("D2")}:{currMin.ToString("D2")}";
    }

    public void updateSelected()
    {
        selcectedDate.text = chosenDate;
        startDate.text = $"{chosenDate} 00:00";
    }

    public class dateObj
    {
        public Vector3Int dataDate;
        public bool simulatingTime;
        public int timeToSim;
        public string endDate;
    }

    public dateObj getData()
    {
        dateObj data = new dateObj();

        string[] splitted = chosenDate.Split(" ");

        int day = int.Parse(splitted[0].Substring(0, splitted[0].Length - 2));
        int month = Array.IndexOf(months, splitted[1]) + 1;
        int year = int.Parse(splitted[2]);

        DateTime startTime = new DateTime(year, month, 1, 0, 0, 0);
        DateTime endTime = new DateTime(year, month, currDay == -1 ? 1 : currDay, currHour, currMin, 0);
        TimeSpan diffrence = endTime - startTime;

        data.dataDate = new Vector3Int(day, month, year);
        data.simulatingTime = exactDate.isOn && currDay != -1;
        data.timeToSim = (diffrence.Days * 86400) + (diffrence.Hours * 3600) + (diffrence.Minutes * 60);
        data.endDate = endDate.text;

        return data;
    }
}
