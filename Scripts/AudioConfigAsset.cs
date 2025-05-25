using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RaytracedAudio
{
    [CreateAssetMenu(menuName = "Audio/Audio Config Asset")]
    public class AudioConfigAsset : ScriptableObject
    {
        [SerializeField] private AudioConfig audioConfig = new();

        public AudioInstance Play()
        {
            return audioConfig.Play();
        }

        public AudioInstance Play(AudioProps props)
        {
            return audioConfig.Play(props);
        }

        public AudioInstance Play(AudioProps props, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            return audioConfig.Play(props, volumeOverride, pitchOverride);
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
