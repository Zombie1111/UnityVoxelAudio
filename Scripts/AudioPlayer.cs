using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RaytracedAudio
{
    public class AudioPlayer : MonoBehaviour
    {
        [SerializeField] private bool playOnAwake = true;
        [SerializeField] private AudioConfigAsset audioConfigAsset = null;
        [SerializeField] private AudioConfig audioConfig = new();

        [Header("Advanced")]
        [SerializeField] private CustomProp[] customProps = new CustomProp[0];
        [SerializeField] private float pitchOverride = -1.0f;
        [SerializeField] private float volumeOverride = -1.0f;
        private readonly AudioProps defaultProps = new();
        private readonly HashSet<AudioInstanceRef> ais = new(1);

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying == false && audioConfigAsset != null)
                audioConfigAsset.UpdateDefaultCustomProps(ref customProps);

            foreach (AudioInstanceRef ai in ais)
            {
                if (customProps.Length > 0) ai._ai.SetCustomProps(customProps);
                if (volumeOverride >= 0.0f) ai._ai.SetVolume(volumeOverride);
                if (pitchOverride >= 0.0f) ai._ai.SetPitch(pitchOverride);
            }

            defaultProps.customProps = customProps;
        }

        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying == false && audioConfigAsset != null)
                audioConfigAsset.UpdateDefaultCustomProps(ref customProps);
        }
#endif

        private void Awake()
        {
            if (audioConfigAsset != null) audioConfig = audioConfigAsset.GetAudioConfig();
            defaultProps.attatchTo = transform;
            if (customProps.Length > 0) defaultProps.customProps = customProps;
            if (playOnAwake == false) return;
            Play();
        }

        private void OnEnable()
        {
            SetPaused(false);
        }

        private void OnDisable()
        {
            SetPaused(true);
        }

        private void OnDestroy()
        {
            Stop();
        }

        public AudioInstanceRef Play(AudioProps props = null)
        {
            if (props == null)
            {
                defaultProps.pos = transform.position;
                props = defaultProps;
            }
            else if (props.attatchTo == null) props.attatchTo = transform;

            AudioInstanceRef ai = audioConfig.Play(props, volumeOverride, pitchOverride);

            if (isPaused == true) ai._ai.SetPaused(true);
            ais.Add(ai);
            return ai;
        }

        public void Stop()
        {
            foreach (AudioInstanceRef ai in ais)
            {
                ai._ai.Stop();
            }

            ais.Clear();
        }

        private bool isPaused = false;

        public void SetPaused(bool pause)
        {
            if (pause == isPaused) return;
            isPaused = pause;

            foreach (AudioInstanceRef ai in ais)
            {
                ai._ai.SetPaused(pause);
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(AudioPlayer))]
    [CanEditMultipleObjects]
    public class AudioPlayerEditor : Editor
    {
        SerializedProperty playOnAwake;
        SerializedProperty audioConfigAsset;
        SerializedProperty audioConfig;
        SerializedProperty customProps;
        SerializedProperty pitchOverride;
        SerializedProperty volumeOverride;

        void OnEnable()
        {
            playOnAwake = serializedObject.FindProperty("playOnAwake");
            audioConfigAsset = serializedObject.FindProperty("audioConfigAsset");
            audioConfig = serializedObject.FindProperty("audioConfig");
            customProps = serializedObject.FindProperty("customProps");
            pitchOverride = serializedObject.FindProperty("pitchOverride");
            volumeOverride = serializedObject.FindProperty("volumeOverride");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(playOnAwake);
            EditorGUILayout.PropertyField(audioConfigAsset);

            // Check if all selected audioConfigAsset values are null
            bool showAudioConfig = ShouldShowAudioConfig();

            if (showAudioConfig)
            {
                EditorGUILayout.PropertyField(audioConfig, true);
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(customProps, true);
            EditorGUILayout.PropertyField(pitchOverride);
            EditorGUILayout.PropertyField(volumeOverride);

            AudioConfigAsset aca = (AudioConfigAsset)audioConfigAsset.objectReferenceValue;
            if (aca != null && aca.HasDefaultCustomProps() == true)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Custom Props names are controlled by audioConfigAsset", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private bool ShouldShowAudioConfig()
        {
            // If any selected object has audioConfigAsset != null, return false
            foreach (var targetObj in serializedObject.targetObjects)
            {
                SerializedObject so = new SerializedObject(targetObj);
                var prop = so.FindProperty("audioConfigAsset");
                if (prop.objectReferenceValue != null)
                    return false;
            }
            return true;
        }
    }
#endif
}
