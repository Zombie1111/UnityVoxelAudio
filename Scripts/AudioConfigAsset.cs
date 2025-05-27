using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RaytracedAudio
{
    [CreateAssetMenu(menuName = "Audio/Audio Config Asset")]
    public class AudioConfigAsset : ScriptableObject
    {
        [SerializeField] private AudioConfig audioConfig = new();
        [SerializeField] private CustomProp[] defaultCustomProps = new CustomProp[0];

        public bool HasDefaultCustomProps()
        {
            return defaultCustomProps != null && defaultCustomProps.Length > 0;
        }

        public void UpdateDefaultCustomProps(ref CustomProp[] props)
        {
            if (HasDefaultCustomProps() == false) return;

            props = props.GetWithLenght(defaultCustomProps.Length);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i] == null) props[i] = new();
                if (props[i].name == null || (props[i].name.Length == 0 && props[i].value == 0.0f))
                    props[i].value = defaultCustomProps[i].value;//Set default value if new element
                props[i].name = defaultCustomProps[i].name;
            }
        }

        public AudioInstanceRef Play()
        {
            return audioConfig.Play();
        }

        public AudioInstanceRef Play(float volumeOverride, float pitchOverride = -1.0f)
        {
            return audioConfig.Play(volumeOverride, pitchOverride);
        }

        public AudioInstanceRef Play(AudioProps props)
        {
            return audioConfig.Play(props);
        }

        public AudioInstanceRef Play(AudioProps props, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            return audioConfig.Play(props, volumeOverride, pitchOverride);
        }

        public AudioInstanceRef Play(Transform attatchTo, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            return audioConfig.Play(attatchTo, volumeOverride, pitchOverride);
        }

        /// <summary>
        /// Returns a shallow copy of the AudioConfig
        /// </summary>
        public AudioConfig GetAudioConfig()
        {
            return audioConfig.ShallowCopy();
        }
    }
}
