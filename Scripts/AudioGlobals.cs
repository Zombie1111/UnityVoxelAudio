using System;
using UnityEngine;
using FMODUnity;

namespace RaytracedAudio
{
    [System.Serializable]
    public class AudioConfig
    {
        [SerializeField] internal EventReference clip;
        [SerializeField] internal AudioEffects audioEffects = AudioEffects.all;

        [Tooltip("If true calling this.Play() if already playing will update the already playing clip properties instead of creating a new instance")]
        [SerializeField] internal bool singletone = false;
        [SerializeField] internal Transform attatchTo = null;

        /// <summary>
        /// Does not affect already playing clips, call AudioInstance.SetParent() to change the parent of already playing clips
        /// </summary>
        public void SetDefualtParent(Transform newParent)
        {
            attatchTo = newParent;
        }
        
        internal IntPtr lastPlayedEventHandle = IntPtr.Zero;
        
        public AudioInstance Play(AudioProps props = null)
        {
            return AudioManager._instance.PlaySound(this, props);
        }
    }

    public class AudioProps
    {
        public Vector3 pos = Vector3.zero;
        public CustomProp[] customProps = null;
    }

    public class CustomProp
    {
        public string name;
        public float value;
    }


    public enum AudioEffects
    {
        all = 0,
        bypassTracing = 10,
        bypassZones = 11,
        bypassAll = 20
    }

    public static class AudioGlobals
    {
        internal static AudioEffects globalAudioEffects = AudioEffects.all;

        public static bool HasTracing(this AudioEffects aEffects)
        {
            return (aEffects == AudioEffects.all || aEffects == AudioEffects.bypassZones)
                && (globalAudioEffects == AudioEffects.all || globalAudioEffects == AudioEffects.bypassZones);
        }

        public static bool HasZones(this AudioEffects aEffects)
        {
            return (aEffects == AudioEffects.all || aEffects == AudioEffects.bypassTracing)
                && (globalAudioEffects == AudioEffects.all || globalAudioEffects == AudioEffects.bypassTracing);
        }

        public static bool HasAny(this AudioEffects aEffects)
        {
            return aEffects != AudioEffects.bypassAll && globalAudioEffects != AudioEffects.bypassAll;
        }
    }
}