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

        public AudioConfig(string eventPath, AudioEffects audioEffects = AudioEffects.all, bool singletone = false)
        {
            clip = EventReference.Find(eventPath);
            this.audioEffects = audioEffects;
            this.singletone = singletone;
        }

        [SerializeField] internal EventReference clip;
        [SerializeField] internal AudioEffects audioEffects = AudioEffects.all;

        [Tooltip("If true calling this.Play() if already playing will update the already playing clip properties instead of creating a new instance")]
        [SerializeField] internal bool singletone = false;

        internal int lastPlayedId = -1;

        public AudioInstanceRef Play()
        {
            return AudioManager._instance.PlaySound(this, null);
        }

        public AudioInstanceRef Play(float volumeOverride, float pitchOverride = -1.0f)
        {
            AudioInstanceRef ai = AudioManager._instance.PlaySound(this, null);
            if (volumeOverride >= 0.0f) ai._ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) ai._ai.SetPitch(pitchOverride);
            return ai;
        }

        public AudioInstanceRef Play(AudioProps props)
        {
            if (props.attatchTo != null && props.pos.x == 0.0f && props.pos.y == 0.0f && props.pos.z == 0.0f)
            {
                props = props.ShallowCopy();
                props.pos = props.attatchTo.position;
            }

            return AudioManager._instance.PlaySound(this, props);
        }

        public AudioInstanceRef Play(AudioProps props, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            if (props.attatchTo != null && props.pos.x == 0.0f && props.pos.y == 0.0f && props.pos.z == 0.0f)
            {
                props = props.ShallowCopy();
                props.pos = props.attatchTo.position;
            }

            AudioInstanceRef ai = AudioManager._instance.PlaySound(this, props);
            if (volumeOverride >= 0.0f) ai._ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) ai._ai.SetPitch(pitchOverride);
            return ai;
        }

        public AudioInstanceRef Play(Transform attatchTo, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            AudioInstanceRef ai = AudioManager._instance.PlaySound(this, attatchTo != null ? new(attatchTo.position) : null);
            if (volumeOverride >= 0.0f) ai._ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) ai._ai.SetPitch(pitchOverride);
            return ai;
        }

        public AudioInstanceRef Play(Vector3 pos, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            AudioInstanceRef ai = AudioManager._instance.PlaySound(this, new(pos));
            if (volumeOverride >= 0.0f) ai._ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) ai._ai.SetPitch(pitchOverride);
            return ai;
        }

        public AudioConfig ShallowCopy()
        {
            return (AudioConfig)this.MemberwiseClone();
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

        public AudioProps(Vector3 pos, Transform attatchTo)
        {
            this.pos = pos;
            this.attatchTo = attatchTo;
        }

        public AudioProps(Vector3 pos, CustomProp[] customProps, Transform attatchTo = null)
        {
            this.pos = pos;
            this.customProps = customProps;
            this.attatchTo = attatchTo;
        }

        public AudioProps(Vector3 pos, string propName, float propValue, Transform attatchTo = null)
        {
            this.pos = pos;
            this.attatchTo = attatchTo;
            customProps = new CustomProp[1] { new(propName, propValue) };
        }

        public AudioProps(string propName, float propValue, Transform attatchTo = null)
        {
            customProps = new CustomProp[1] { new(propName, propValue) };
            this.attatchTo = attatchTo;
        }

        public AudioProps(string propName1, float propValue1, string propName2, float propValue2, Transform attatchTo = null)
        {
            customProps = new CustomProp[2] { new(propName1, propValue1), new(propName2, propValue2) };
            this.attatchTo = attatchTo;
        }

        public Vector3 pos = Vector3.zero;
        public Transform attatchTo = null;
        public CustomProp[] customProps = null;

        /// <summary>
        /// Sets the value of the a prop with the given name, adds new prop if name does not exist
        /// </summary>
        public void SetProp(string propName, float propValue)
        {
            if (customProps == null || customProps.Length == 0) customProps = new CustomProp[1] { new(propName, propValue) };
            else
            {
                foreach (CustomProp prop in customProps)
                {
                    if (prop.name != propName) continue;
                    prop.value = propValue;
                    return;
                }

                CustomProp[] newProps = new CustomProp[customProps.Length + 1];
                customProps.CopyTo(newProps, 0);
                newProps[^1] = new CustomProp(propName, propValue);
                customProps = newProps;
            }
        }

        /// <summary>
        /// Sets the value at the given index if its within array bounds
        /// </summary>
        public void SetProp(int index, float propValue)
        {
            if (customProps == null || customProps.Length <= index) return;
            customProps[index].value = propValue;
        }

        /// <summary>
        /// Sets FMod global parameters from the customProps array
        /// </summary>
        public void ApplyToGlobal()
        {
            if (customProps == null) return;

            foreach (CustomProp prop in customProps)
            {
                RuntimeManager.StudioSystem.setParameterByName(prop.name, prop.value);
            }
        }

        /// <summary>
        /// Sets the props of the given AudioInstance if its valid
        /// </summary>
        public void ApplyTo(AudioInstance ai, bool customPropsOnly = false)
        {
            if (ai == null) return;

            if (customPropsOnly == false) ai.SetProps(this);
            else if (customProps != null) ai.SetCustomProps(customProps);
        }

        /// <summary>
        /// Sets the props of the given AudioInstance if its valid
        /// </summary>
        public void ApplyTo(AudioInstanceRef air, bool customPropsOnly = false)
        {
            if (air == null) return;
            AudioInstance ai = air.TryGetAudioInstance();
            if (ai == null) return;

            if (customPropsOnly == false) ai.SetProps(this);
            else if (customProps != null) ai.SetCustomProps(customProps);
        }

        public AudioProps ShallowCopy()
        {
            return (AudioProps)this.MemberwiseClone();
        }
    }

    [System.Serializable]
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

        public void ApplyGlobal()
        {
            RuntimeManager.StudioSystem.setParameterByName(name, value);
        }
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

        internal static T[] GetWithLenght<T>(this T[] array, int newLength)
        {
            if (newLength < 0) newLength = 0;
            if (newLength == array.Length) return array;

            T[] newArray = new T[newLength];
            Array.Copy(array, newArray, Math.Min(array.Length, newLength));
            return newArray;
        }
    }
    #endregion Audio Callbacks
}