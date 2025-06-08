using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RaytracedAudio
{
    public class AudioPlayer : MonoBehaviour
    {
        [SerializeField] private bool playOnAwake = true;
        [SerializeField] private AudioReferenceAsset audioRefAsset = null;
        [SerializeField] private AudioReference audioRef = new();

        [Header("Advanced")]
        [SerializeField] private CustomProp[] customProps = new CustomProp[0];
        [SerializeField] private float pitchOverride = -1.0f;
        [SerializeField] private float volumeOverride = -1.0f;
        [Tooltip("If true stops the audio on disable instead of pausing")]
        [SerializeField] private bool stopOnDisable = false;
        private readonly AudioProps defaultProps = new();
        private AudioInstanceWrap aiw;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying == false && audioRefAsset != null)
                audioRefAsset.UpdateDefaultCustomProps(ref customProps);

            defaultProps.customProps = customProps;
            if (aiw.TryGetAudioInstance(out AudioInstance ai) == false) return;

            if (customProps.Length > 0) ai.SetCustomProps(customProps);
            if (volumeOverride >= 0.0f) ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) ai.SetPitch(pitchOverride);
        }

        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying == false && audioRefAsset != null)
                audioRefAsset.UpdateDefaultCustomProps(ref customProps);
        }
#endif

        private void Awake()
        {
            if (audioRefAsset != null) audioRef = audioRefAsset.GetAudioReference();
            defaultProps.attatchTo = transform;
            if (customProps.Length > 0) defaultProps.customProps = customProps;
            if (stopOnDisable == false && playOnAwake == true) Play();
        }

        private void OnEnable()
        {
            isActive = true;
            if (stopOnDisable == true)
            {
                if (playOnAwake == true) Play();
                return;
            }

            SetPaused(false);
        }

        private void OnDisable()
        {
            isActive = false;
            if (stopOnDisable == true)
            {
                Stop();
                return;
            }

            SetPaused(true);
        }

        private void OnDestroy()
        {
            Stop();
        }

        public AudioInstanceWrap Play(AudioProps props = null)
        {
            if (stopOnDisable == true && isActive == false) return new();//Dont allow playing if disabled

            if (props == null)
            {
                defaultProps.pos = transform.position;
                props = defaultProps;
            }
            else if (props.attatchTo == null) props.attatchTo = transform;

            audioRef.PlaySingletone(ref aiw, props, true, volumeOverride, pitchOverride);
            if (isPaused == true) aiw.TryGetAudioInstance().SetPaused(true);//Garanteed to be valid
            return aiw;
        }

        public void Stop()
        {
            audioRef.Stop(aiw);
        }

        private bool isActive = false;
        private bool isPaused = false;

        public void SetPaused(bool pause)
        {
            if (pause == isPaused) return;
            isPaused = pause;

            audioRef.SetPaused(aiw, pause);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(AudioPlayer))]
    [CanEditMultipleObjects]
    public class AudioPlayerEditor : Editor
    {
        SerializedProperty playOnAwake;
        SerializedProperty audioRefAsset;
        SerializedProperty audioRef;
        SerializedProperty customProps;
        SerializedProperty pitchOverride;
        SerializedProperty volumeOverride;
        SerializedProperty stopOverride;

        void OnEnable()
        {
            playOnAwake = serializedObject.FindProperty("playOnAwake");
            audioRefAsset = serializedObject.FindProperty("audioRefAsset");
            audioRef = serializedObject.FindProperty("audioRef");
            customProps = serializedObject.FindProperty("customProps");
            pitchOverride = serializedObject.FindProperty("pitchOverride");
            volumeOverride = serializedObject.FindProperty("volumeOverride");
            stopOverride = serializedObject.FindProperty("stopOnDisable");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(playOnAwake);
            EditorGUILayout.PropertyField(audioRefAsset);

            // Check if all selected audioRefAsset values are null
            bool showAudioReference = ShouldShowAudioReference();

            if (showAudioReference)
            {
                EditorGUILayout.PropertyField(audioRef, true);
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(customProps, true);
            EditorGUILayout.PropertyField(pitchOverride);
            EditorGUILayout.PropertyField(volumeOverride);
            EditorGUILayout.PropertyField(stopOverride);

            AudioReferenceAsset aca = (AudioReferenceAsset)audioRefAsset.objectReferenceValue;
            if (aca != null && aca.HasDefaultCustomProps() == true)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Custom Props names are controlled by audioRefAsset", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private bool ShouldShowAudioReference()
        {
            foreach (var targetObj in serializedObject.targetObjects)
            {
                SerializedObject so = new(targetObj);
                var prop = so.FindProperty("audioRefAsset");
                if (prop.objectReferenceValue != null)
                    return false;
            }
            return true;
        }
    }
#endif
}
