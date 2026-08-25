using UnityEngine;
using UnityEngine.UI;

public class DateButton : MonoBehaviour
{
    public Button button;
    public SelectDateMan SelectDateMan;
    public string date;

    public void Awake()
    {
        button.onClick.AddListener(clicked);
    }

    private void clicked()
    {
        SelectDateMan.chosenDate = date;
        SelectDateMan.updateSelected();
        SelectDateMan.updateTargets();
    }
}