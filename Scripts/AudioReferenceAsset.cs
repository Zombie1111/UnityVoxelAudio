using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelAudio
{
    [CreateAssetMenu(menuName = "Audio/Audio Reference Asset")]
    public class AudioReferenceAsset : ScriptableObject
    {
        [SerializeField] private AudioReference audioRef = new();
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

        public AudioInstanceWrap Play()
        {
            return audioRef.Play();
        }

        public AudioInstanceWrap Play(float volumeOverride, float pitchOverride = -1.0f)
        {
            return audioRef.Play(volumeOverride, pitchOverride);
        }

        public AudioInstanceWrap Play(AudioProps props)
        {
            return audioRef.Play(props);
        }

        public AudioInstanceWrap Play(AudioProps props, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            return audioRef.Play(props, volumeOverride, pitchOverride);
        }

        public AudioInstanceWrap Play(Transform attatchTo, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            return audioRef.Play(attatchTo, volumeOverride, pitchOverride);
        }

        public AudioInstanceWrap Play(Vector3 pos, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            return audioRef.Play(pos, volumeOverride, pitchOverride);
        }

        public AudioInstanceWrap Play(string propName, float propValue, Transform attatchTo = null, Vector3 pos = default, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            return audioRef.Play(propName, propValue, attatchTo, pos, volumeOverride, pitchOverride);
        }

        /// <summary>
        /// If aiRef is valid tries to apply props to it otherwise plays and assigns played instance to aiRef
        /// </summary>
        public void PlaySingletone(ref AudioInstanceWrap aiRef, AudioProps props = null, bool setCustomPropsOnly = false, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            if (aiRef.IsValid() == true)
            {
                props?.ApplyTo(in aiRef, setCustomPropsOnly);
                return;
            }

            aiRef = Play(props);
            if (volumeOverride >= 0.0f) aiRef.ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) aiRef.ai.SetPitch(pitchOverride);
        }

        public void Stop(in AudioInstanceWrap aiRef)
        {
            if (aiRef.TryGetAudioInstance(out AudioInstance ai) == false) return;
            ai.Stop();
        }

        public void SetPaused(in AudioInstanceWrap aiRef, bool pause)
        {
            if (aiRef.TryGetAudioInstance(out AudioInstance ai) == false) return;
            ai.SetPaused(pause);
        }

        /// <summary>
        /// Returns a shallow copy of the AudioReference
        /// </summary>
        public AudioReference GetAudioReference()
        {
            return audioRef.ShallowCopy();
        }

        /// <summary>
        /// Returns true if a clip has been assigned to the reference
        /// </summary>
        public bool HasValidClip()
        {
            return audioRef.HasValidClip();
        }
    }
}
