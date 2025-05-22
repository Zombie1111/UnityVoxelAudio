using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RaytracedAudio
{
    public class AudioPlayer : MonoBehaviour
    {
        [SerializeField] private bool playOnAwake = true;
        [SerializeField] private AudioConfig audioConfig = new();
        private readonly AudioProps defaultProps = new();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (audioConfig.attatchTo == null) audioConfig.attatchTo = transform;
        }
#endif

        private void Awake()
        {
            if (playOnAwake == false) return;
            audioConfig.Play();
        }

        public void Play(AudioProps props = null)
        {
            if (props == null)
            {
                defaultProps.pos = transform.position;
                audioConfig.Play(defaultProps);
            }

            audioConfig.Play(props);
        }
    }
}
