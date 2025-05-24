using Codice.CM.Common.Checkin.Partial;
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
        private readonly HashSet<AudioInstance> ais = new(1);

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (audioConfig.attatchTo == null) audioConfig.attatchTo = transform;
        }
#endif

        private void Awake()
        {
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

        public AudioInstance Play(AudioProps props = null)
        {
            if (props == null)
            {
                defaultProps.pos = transform.position;
                props = defaultProps;
            }

            AudioInstance ai = audioConfig.Play(props);
            
            if (isPaused == true) ai.SetPaused(true);
            ais.Add(ai);
            return ai;
        }

        public void Stop()
        {
            foreach (AudioInstance ai in ais)
            {
                ai.Stop();
            }

            ais.Clear();
        }

        private bool isPaused = false;

        public void SetPaused(bool pause)
        {
            if (pause == isPaused) return;
            isPaused = pause;

            foreach (AudioInstance ai in ais)
            {
                ai.SetPaused(pause);
            }
        }
    }
}
