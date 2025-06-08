using System;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using FMOD;
using Unity.Collections;

namespace RaytracedAudio
{
    #region Bus Config
    [System.Serializable]
    public class BusConfig
    {
        [SerializeField] internal string path = string.Empty;
        [SerializeField] internal bool isPausable = true;
        internal Bus bus = new(IntPtr.Zero);

        internal string GetFullPath()
        {
            return "bus:/" + path;
        }

        public Bus GetFModBus()
        {
            if (bus.isValid() == true) return bus;
            bus = RuntimeManager.GetBus(GetFullPath());
            return bus;
        }

        /// <summary>
        /// Pauses or unpauses all audio in this bus
        /// </summary>
        public void SetPaused(bool pause)
        {
            GetFModBus().setPaused(pause);
        }

        /// <summary>
        /// Tries to stop all audio in this bus, may not stop events that was recently started. Use AudioSettings.StopAll() or stop individual AudioInstances instead
        /// </summary>
        public void TryStopAll(FMOD.Studio.STOP_MODE mode = FMOD.Studio.STOP_MODE.ALLOWFADEOUT)
        {
            GetFModBus().stopAllEvents(mode);
        }

        public void SetVolume(float newVolume)
        {
            GetFModBus().setVolume(newVolume);
        }
    }
    #endregion Bus Config

    #region Audio Config

    [System.Serializable]
    public class AudioReference
    {
        public AudioReference()
        {

        }

        public AudioReference(string eventPath, AudioConfigAsset audioConfigOverride = null)
        {
            clip = EventReference.Find(eventPath);
            this.audioConfigOverride = audioConfigOverride;
        }

        [SerializeField] internal EventReference clip;
        [SerializeField] private AudioConfigAsset audioConfigOverride = null;

        internal AudioConfigAsset GetAudioConfig()
        {
            if (audioConfigOverride != null) return audioConfigOverride;
            return AudioSettings._defaultAudioConfigAsset;
        }

        public AudioInstanceWrap Play()
        {
            return AudioManager._instance.PlaySound(this, null);
        }

        public AudioInstanceWrap Play(float volumeOverride, float pitchOverride = -1.0f)
        {
            AudioInstanceWrap ai = AudioManager._instance.PlaySound(this, null);
            if (volumeOverride >= 0.0f) ai.ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) ai.ai.SetPitch(pitchOverride);
            return ai;
        }

        public AudioInstanceWrap Play(AudioProps props)
        {
            if (props != null && props.attatchTo != null && props.pos.x == 0.0f && props.pos.y == 0.0f && props.pos.z == 0.0f)
            {
                props = props.ShallowCopy();
                props.pos = props.attatchTo.position;
            }

            return AudioManager._instance.PlaySound(this, props);
        }

        public AudioInstanceWrap Play(AudioProps props, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            if (props != null && props.attatchTo != null && props.pos.x == 0.0f && props.pos.y == 0.0f && props.pos.z == 0.0f)
            {
                props = props.ShallowCopy();
                props.pos = props.attatchTo.position;
            }

            AudioInstanceWrap ai = AudioManager._instance.PlaySound(this, props);
            if (volumeOverride >= 0.0f) ai.ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) ai.ai.SetPitch(pitchOverride);
            return ai;
        }

        public AudioInstanceWrap Play(Transform attatchTo, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            AudioInstanceWrap ai = AudioManager._instance.PlaySound(this, attatchTo != null ? new(attatchTo.position) : null);
            if (volumeOverride >= 0.0f) ai.ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) ai.ai.SetPitch(pitchOverride);
            return ai;
        }

        public AudioInstanceWrap Play(Vector3 pos, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            AudioInstanceWrap ai = AudioManager._instance.PlaySound(this, new(pos));
            if (volumeOverride >= 0.0f) ai.ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) ai.ai.SetPitch(pitchOverride);
            return ai;
        }

        public AudioInstanceWrap Play(string propName, float propValue, Transform attatchTo = null, Vector3 pos = default, float volumeOverride = -1.0f, float pitchOverride = -1.0f)
        {
            AudioInstanceWrap ai = Play(new(pos, propName, propValue, attatchTo));
            if (volumeOverride >= 0.0f) ai.ai.SetVolume(volumeOverride);
            if (pitchOverride >= 0.0f) ai.ai.SetPitch(pitchOverride);
            return ai;
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

        public AudioReference ShallowCopy()
        {
            return (AudioReference)this.MemberwiseClone();
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
        public void ApplyTo(in AudioInstanceWrap air, bool customPropsOnly = false)
        {
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
        noOcclusion = 10,
        noReverb = 11,
        none = 20
    }

#if UNITY_EDITOR
    internal enum DebugMode
    {
        none,
        drawAudioOcclusion,
        drawAudioDirection,
        drawAudioReverb,
    }
#endif

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
        public static bool HasOcclusion(this AudioEffects aEffects)
        {
            return (aEffects == AudioEffects.all || aEffects == AudioEffects.noReverb)
                && (AudioSettings.__globalAudioEffects == AudioEffects.all || AudioSettings.__globalAudioEffects == AudioEffects.noReverb);
        }

        public static bool HasReverb(this AudioEffects aEffects)
        {
            return (aEffects == AudioEffects.all || aEffects == AudioEffects.noOcclusion)
                && (AudioSettings.__globalAudioEffects == AudioEffects.all || AudioSettings.__globalAudioEffects == AudioEffects.noOcclusion);
        }

        public static bool HasAny(this AudioEffects aEffects)
        {
            return aEffects != AudioEffects.none && AudioSettings.__globalAudioEffects != AudioEffects.none;
        }

        internal static T[] GetWithLenght<T>(this T[] array, int newLength)
        {
            if (newLength < 0) newLength = 0;
            if (newLength == array.Length) return array;

            T[] newArray = new T[newLength];
            Array.Copy(array, newArray, Math.Min(array.Length, newLength));
            return newArray;
        }

        /// <summary>
        /// Returns directions with temp allocator (Always 6)
        /// </summary>
        internal static NativeArray<Vector3> GetAxisDirsNative()
        {
            NativeArray<Vector3> dirs = new(6, Allocator.Temp);

            dirs[0] = Vector3.right;
            dirs[1] = Vector3.up;
            dirs[2] = Vector3.forward;
            dirs[3] = -Vector3.right;
            dirs[4] = -Vector3.up;
            dirs[5] = -Vector3.forward;
           
            return dirs;
        }
    }
    #endregion Audio Callbacks
}