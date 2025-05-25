#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using System;

namespace RaytracedAudio
{
    [CustomEditor(typeof(AudioSettings))]
    public class AudioSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // Draw the default inspector
            DrawDefaultInspector();

            // Get reference to the target object
            AudioSettings audioSettings = (AudioSettings)target;

            // Use reflection to get the private 'buses' field
            var busesField = typeof(AudioSettings).GetField("buses", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (busesField == null) return;
            if (busesField.GetValue(audioSettings) is not List<BusConfig> buses || buses.Count == 0) return;
            
            //No easy way to verify bus paths in editor
            // Count duplicates
            var duplicatePaths = buses
                .GroupBy(b => b.path)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            // Show warning if duplicates exist
            if (duplicatePaths.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    $"Duplicate bus path(s) found:\n- {string.Join("\n- ", duplicatePaths)}",
                    MessageType.Warning);
            }
        }

        [MenuItem("Tools/Raytraced Audio/Edit Settings", priority = 0)]
        public static void EditSettings()
        {
            Selection.activeObject = AudioSettings._instance;
            EditorApplication.ExecuteMenuItem("Window/General/Inspector");
        }
    }
}
#endif
