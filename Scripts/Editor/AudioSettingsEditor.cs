#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using System;
using System.IO;
using VoxelAudio;
using AudioSettings = VoxelAudio.AudioSettings;

namespace VoxelAudioEditor
{
    [CustomEditor(typeof(AudioSettings))]
    internal class AudioSettingsEditor : Editor
    {
        private FMOD.Studio.System fmodSystem;
        private readonly HashSet<string> allBusPaths = new();
        private bool showDefaultSurfaces = true;

        private void Awake()
        {
            var result = FMOD.Studio.System.create(out fmodSystem);
            allBusPaths.Clear();

            if (result != FMOD.RESULT.OK) return;

            try
            {
                fmodSystem.initialize(1, FMOD.Studio.INITFLAGS.NORMAL, FMOD.INITFLAGS.NORMAL, IntPtr.Zero);

                var fmodSettings = FMODUnity.Settings.Instance;
                string bankFolder = GetBankFolder();
                const string BankExtension = ".bank";

                foreach (string bankName in BanksToLoad(fmodSettings))
                {
                    string bankPath;

                    if (System.IO.Path.GetExtension(bankName) != BankExtension)
                    {
                        bankPath = string.Format("{0}/{1}{2}", bankFolder, bankName, BankExtension);
                    }
                    else
                    {
                        bankPath = string.Format("{0}/{1}", bankFolder, bankName);
                    }

                    FMOD.RESULT loadResult = fmodSystem.loadBankFile(bankPath, FMOD.Studio.LOAD_BANK_FLAGS.NORMAL, out var bank);
                    bank.getBusList(out var busList);
                    foreach (var bus in busList)
                    {
                        bus.getPath(out var path);
                        allBusPaths.Add(path);
                    }
                }
            }
            finally
            {
                if (fmodSystem.isValid() == true)
                {
                    fmodSystem.unloadAll();
                    fmodSystem.release();
                    fmodSystem.clearHandle();
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            //Get reference to the target object
            AudioSettings audioSettings = (AudioSettings)target;

            //Set default surfaces
            Array surfTypes = Enum.GetValues(typeof(AudioSurface.SurfaceType));
            if (audioSettings.defaultAudioSurfaces == null || audioSettings.defaultAudioSurfaces.Length == 0)
                audioSettings.defaultAudioSurfaces = AudioSurface.defualtInitSurfaces;
            Array.Resize(ref audioSettings.defaultAudioSurfaces, surfTypes.Length);

            int i = 0;
            foreach (var surfT in surfTypes)
            {
                if (audioSettings.defaultAudioSurfaces[i] == null) audioSettings.defaultAudioSurfaces[i] = new();
                audioSettings.defaultAudioSurfaces[i].type = (AudioSurface.SurfaceType)surfT;
                i++;
            }

            var sp_DefaultAudioSurfaces = serializedObject.FindProperty("defaultAudioSurfaces");
            EditorGUI.indentLevel++;

            showDefaultSurfaces = EditorGUILayout.Foldout(showDefaultSurfaces, "Default Audio Surfaces", true);

            if (showDefaultSurfaces)
            {
                EditorGUI.indentLevel++;

                for (i = 0; i < sp_DefaultAudioSurfaces.arraySize; i++)
                {
                    SerializedProperty elementProp = sp_DefaultAudioSurfaces.GetArrayElementAtIndex(i);
                    SerializedProperty typeProp = elementProp.FindPropertyRelative("type");

                    AudioSurface.SurfaceType surfTypeEnum = (AudioSurface.SurfaceType)typeProp.enumValueIndex;
                    string label = surfTypeEnum.ToString();

                    EditorGUILayout.PropertyField(elementProp, new GUIContent(label), true);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            //Default surface type must be 0
            if (audioSettings.defaultAudioSurfaces[0].type != audioSettings.defaultSurfaceType)
            {
                EditorGUILayout.HelpBox("defaultSurfaceType should be at index 0 in defaultAudioSurfaces to work properly!", MessageType.Warning);
            }

            //Draw the default inspector
            DrawPropertiesExcluding(serializedObject, "m_Script", "defaultAudioSurfaces");
            //DrawDefaultInspector();

            // Use reflection to get the private 'buses' field
            var busesField = typeof(AudioSettings).GetField("buses", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (busesField == null) return;
            if (busesField.GetValue(audioSettings) is not List<BusConfig> buses || buses.Count == 0) return;

            //Is bus paths valid?
            HashSet<string> allPaths = new();

            foreach (var bus in buses)
            {
                string path = bus.GetFullPath();
                allPaths.Add(path);
                if (allBusPaths.Contains(path) == true) continue;

                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(path + " is not a valid bus path!", MessageType.Warning);
            }

            //Count duplicates
            var duplicatePaths = buses
                .GroupBy(b => b.path)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            //Show warning if duplicates exist
            if (duplicatePaths.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    $"Duplicate bus path(s) found:\n- {string.Join("\n- ", duplicatePaths)}",
                    MessageType.Warning);
            }

            //Does all bus paths exist?
            foreach (string path in allBusPaths)
            {
                if (allPaths.Contains(path) == true) continue;

                EditorGUILayout.HelpBox(
                    path.Replace("bus:/", string.Empty) + " has not been added to the bus list yet, all bus paths should exist in list!",
                    MessageType.Warning);
            }

            //Show error
            if (AudioSettings._defaultAudioConfigAsset == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("defaultAudioConfigAsset cannot be null!", MessageType.Error);
            }

            serializedObject.ApplyModifiedProperties();
        }

        [MenuItem("Tools/Voxel Audio/Edit Settings", priority = 0)]
        public static void EditSettings()
        {
            Selection.activeObject = AudioSettings._instance;
            EditorApplication.ExecuteMenuItem("Window/General/Inspector");
        }

        private static IEnumerable<string> BanksToLoad(Settings fmodSettings)//Stolen from runtimemanager
        {
            switch (fmodSettings.BankLoadType)
            {
                case BankLoadType.All:
                    foreach (string masterBankFileName in fmodSettings.MasterBanks)
                    {
                        yield return masterBankFileName + ".strings";
                        yield return masterBankFileName;
                    }

                    foreach (var bank in fmodSettings.Banks)
                    {
                        yield return bank;
                    }
                    break;
                case BankLoadType.Specified:
                    foreach (var bank in fmodSettings.BanksToLoad)
                    {
                        if (!string.IsNullOrEmpty(bank))
                        {
                            yield return bank;
                        }
                    }
                    break;
                case BankLoadType.None:
                    break;
                default:
                    break;
            }
        }

        private string GetBankFolder()
        {
            // Use original asset location because streaming asset folder will contain platform specific banks
            Settings globalSettings = Settings.Instance;

            string bankFolder = globalSettings.SourceBankPath;
            if (globalSettings.HasPlatforms)
            {
                var editor = ScriptableObject.CreateInstance<PlatformPlayInEditor>();
                bankFolder = RuntimeUtils.GetCommonPlatformPath(Path.Combine(bankFolder, editor.BuildDirectory));
                DestroyImmediate(editor);
            }

            return bankFolder;
        }
    }
}
#endif
