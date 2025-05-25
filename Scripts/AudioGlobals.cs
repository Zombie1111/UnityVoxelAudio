using System;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

namespace RaytracedAudio
{
    #region Bus Config
    [System.Serializable]
    public class BusConfig
    {
        [SerializeField] internal string path = string.Empty;
        [SerializeField] private bool isPausable = true;

        internal Bus bus = new(IntPtr.Zero);
        internal string GetFullPath()
        {
            return "bus:/" + path;
        }

        internal void VerifyBus()
        {
            if (bus.hasHandle() == true) return;
            bus = RuntimeManager.GetBus(GetFullPath());
        }

        public void SetPaused(bool pause, bool forceSet = false)
        {
            if (isPausable == false && forceSet == false) return;

            VerifyBus();
            bus.setPaused(pause);
        }

        public void StopAll(FMOD.Studio.STOP_MODE mode = FMOD.Studio.STOP_MODE.ALLOWFADEOUT, bool forceStop = false)
        {
            if (isPausable == false && forceStop == false) return;

            VerifyBus();
            bus.stopAllEvents(mode);
        }

        public void SetVolume(float newVolume)
        {
            bus.setVolume(newVolume);
        }
    }
    #endregion Bus Config

    #region Audio Config

    [System.Serializable]
    public class AudioConfig
    {
        public AudioConfig()
        {

        }

        public AudioConfig(string eventPath, Transform attatchTo = null, AudioEffects audioEffects = AudioEffects.all, bool singletone = false)
        {
            clip = EventReference.Find(eventPath);
            this.attatchTo = attatchTo;
            this.audioEffects = audioEffects;
            this.singletone = singletone;
        }

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

        public AudioInstance Play()
        {
            return AudioManager._instance.PlaySound(this, attatchTo != null ? new(attatchTo.position) : new());
        }

        public AudioInstance Play(AudioProps props)
        {
            return AudioManager._instance.PlaySound(this, props);
        }

        public AudioInstance Play(Vector3 pos)
        {
            return AudioManager._instance.PlaySound(this, new(pos));
        }
    }

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

        public AudioInstance Play(Vector3 pos)
        {
            return audioConfig.Play(pos);
        }
    }

    #endregion Audio Config

    public class AudioProps
    {
        public AudioProps()
        {

        }

        public AudioProps(Vector3 pos)
        {
            this.pos = pos;
        }

        public AudioProps(Vector3 pos, CustomProp[] customProps)
        {
            this.pos = pos;
            this.customProps = customProps;
        }

        public AudioProps(Vector3 pos, string propName, float propValue)
        {
            this.pos = pos;
            customProps = new CustomProp[1] { new(propName, propValue) };
        }

        public Vector3 pos = Vector3.zero;
        public CustomProp[] customProps = null;
    }

    public class CustomProp
    {
        public CustomProp()
        {

        }

        public CustomProp(string name, float value)
        {
            this.name = name;
            this.value = value;
        }

        public string name;
        public float value;
    }


    public enum AudioEffects
    {
        all = 0,
        noTracing = 10,
        noZones = 11,
        nothing = 20
    }

    #region Audio Callbacks

    public enum AudioCallback
    {
        /// <summary>
        /// When the sound actually starts playing
        /// </summary>
        started = 0,
        beat = 10,
        marker = 11,
        stopped = 20,
    }

    public class AudioTimelineData
    {
        public TIMELINE_BEAT_PROPERTIES lastBeat;
        public TIMELINE_MARKER_PROPERTIES lastMarker;
    }

    public class ProgrammerSoundInput
    {
        public ProgrammerSoundInput(FMOD.Sound soundToPlay, int subsoundIndex = -1)
        {
            this.soundToPlay = soundToPlay;
            this.subsoundIndex = subsoundIndex;
        }

        internal readonly FMOD.Sound soundToPlay;
        internal readonly int subsoundIndex = -1;
    }

    public static class AudioHelpMethods
    {
        public static bool HasTracing(this AudioEffects aEffects)
        {
            return (aEffects == AudioEffects.all || aEffects == AudioEffects.noZones)
                && (AudioSettings._globalAudioEffects == AudioEffects.all || AudioSettings._globalAudioEffects == AudioEffects.noZones);
        }

        public static bool HasZones(this AudioEffects aEffects)
        {
            return (aEffects == AudioEffects.all || aEffects == AudioEffects.noTracing)
                && (AudioSettings._globalAudioEffects == AudioEffects.all || AudioSettings._globalAudioEffects == AudioEffects.noTracing);
        }

        public static bool HasAny(this AudioEffects aEffects)
        {
            return aEffects != AudioEffects.nothing && AudioSettings._globalAudioEffects != AudioEffects.nothing;
        }

        internal static bool IsActive(this AudioInstance.State state)
        {
            return (int)state < 25;
        }
    }
    #endregion Audio Callbacks
}