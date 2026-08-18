using UnityEngine;
using UnityEngine.UI;

public class LabelButton : MonoBehaviour
{
    public Button button;
    public infoDisplay infoDisplay;
    public string name;

    public void buttonInit()
    {
        button.onClick.AddListener(clicked);
    }

    private void clicked()
    {
        infoDisplay.target = name;
        infoDisplay.initMenu();
    }
}
