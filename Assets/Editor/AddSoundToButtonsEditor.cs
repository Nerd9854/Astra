using UnityEngine;
using UnityEditor;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class AddSoundToButtonsEditor : EditorWindow
{
    public AudioSource targetAudioSource;

    [MenuItem("Tools/Auto Assign Button Sounds")]
    public static void ShowWindow()
        => GetWindow<AddSoundToButtonsEditor>("Assign Button Sounds");

    private void OnGUI()
        => DrawSettingsUI();

    private void DrawSettingsUI()
    {
        GUILayout.Label("Mass Assign Pointer Enter Sounds", EditorStyles.boldLabel);
        targetAudioSource = (AudioSource)EditorGUILayout.ObjectField("Target AudioSource", targetAudioSource, typeof(AudioSource), true);

        if (GUILayout.Button("Find and Setup Selection"))
        {
            if (targetAudioSource == null)
            {
                EditorUtility.DisplayDialog("Error", "Please drag in an AudioSource first!", "OK");
                return;
            }

            SetupSelectedButtons();
        }
    }

    private void SetupSelectedButtons()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        int modifiedCount = 0;

        foreach (GameObject obj in selectedObjects)
        {
            Undo.RecordObject(obj, "Assign Button Sound");

            EventTrigger trigger = obj.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = obj.AddComponent<EventTrigger>();
                Undo.RegisterCreatedObjectUndo(trigger, "Create Event Trigger");
            }
            else
            {
                // Register undo for the existing EventTrigger component data
                Undo.RecordObject(trigger, "Clear Old Event Triggers");
            }

            // FIX: Wipe the entire EventTrigger list completely clean to remove all leftovers
            trigger.triggers = new List<EventTrigger.Entry>();

            // Create a fresh new PointerEnter entry
            EventTrigger.Entry pointerEnterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            trigger.triggers.Add(pointerEnterEntry);

            // Add the clean, single AudioSource listener
            UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(
                pointerEnterEntry.callback,
                targetAudioSource.Play
            );

            EditorUtility.SetDirty(obj);
            modifiedCount++;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Success", $"Successfully wiped and configured {modifiedCount} buttons!", "Boom, Done");
    }
}
