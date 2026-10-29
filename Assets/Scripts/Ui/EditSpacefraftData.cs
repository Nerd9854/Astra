using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;
using UnityEngine.UI;

public class EditSpacefraftData : MonoBehaviour
{
    public PhysicsManager PhysicsManager;
    public GameObject settings;
    public GameObject editDataObj;

    public SpacecraftManager SpacecraftManager;
    public showWarning showWarning;

    public Button planMissionButton;
    public TMPro.TextMeshProUGUI planMissionText;

    public Button editDataButton;
    public TMPro.TextMeshProUGUI editDataText;

    public TMPro.TMP_InputField dryMassIN;
    public TMPro.TMP_InputField fuelMassIN;
    public TMPro.TMP_InputField payloadMassIN;
    public TMPro.TMP_InputField ispIN;
    public TMPro.TMP_InputField thrustIN;

    public TMPro.TMP_Dropdown dryMassUnits;
    public TMPro.TMP_Dropdown fuelMassUnits;
    public TMPro.TMP_Dropdown payloadMassUnits;
    public TMPro.TMP_Dropdown ispUnits;
    public TMPro.TMP_Dropdown thrustUnits;

    public void editData()
    {
        //all in KG
        double dryMass;
        double fuelMass;
        double payloadMass;
        double isp;
        double thrust;
        try
        {
            dryMass = double.Parse(dryMassIN.text);
            fuelMass = double.Parse(fuelMassIN.text);
            payloadMass = double.Parse(payloadMassIN.text);
            isp = double.Parse(ispIN.text);
            thrust = double.Parse(thrustIN.text);
        }
        catch
        {
            showWarning.dispalyNotif(0, "Issue with inputted values\n(no fields can be left blank)");
            return;
        }

        double dryMassUnitsMulti = dryMassUnits.value == 1? 1 : 0.45359237;
        double fuelMassUnitsMulti = fuelMassUnits.value == 1? 1 : 0.45359237 ;
        double payloadMassUnitsMulti = payloadMassUnits.value == 1? 1 : 0.45359237 ;
        double ispUnitsMulti = 1;//ispUnits.value == 1? 1 : 2;
        double thrustUnitsMulti = thrustUnits.value == 1? 1 : 4.4482216152605;

        SpacecraftManager.craft.DryMass = dryMass * dryMassUnitsMulti;
        SpacecraftManager.craft.FuelMass = fuelMass * fuelMassUnitsMulti;
        SpacecraftManager.craft.PayloadMass = payloadMass * payloadMassUnitsMulti;
        SpacecraftManager.craft.Isp = isp * ispUnitsMulti;
        SpacecraftManager.craft.Thrust = thrust * thrustUnitsMulti;

        if(SpacecraftManager.spacecraftActive) PhysicsManager.fullObjData[PhysicsManager.spacecraftID].mass = (SpacecraftManager.craft.DryMass + SpacecraftManager.craft.FuelMass + SpacecraftManager.craft.PayloadMass) * PhysicsManager.EngineManager.g;
        
        settings.SetActive(false);
        editDataObj.SetActive(false);

        showWarning.dispalyNotif(2, "Updated craft data");
        enableMissionPlanning();
    }

    public void enableMissionPlanning()
    {
        planMissionButton.interactable = true;
        planMissionText.color = new Color32(255, 255, 255, 255);
    }

    public void enableText()
    {
        editDataButton.interactable = true;
        editDataText.color = new Color32(255, 255, 255, 255);
    }

    public void disableText()
    {
        editDataButton.interactable = false;
        editDataText.color = new Color32(128, 128, 128, 255);
    }
}
