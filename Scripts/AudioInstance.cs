using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using Unity.Android.Types;

namespace RaytracedAudio
{
    public class AudioInstanceRef//We cant use AudioInstance directly because it may be reused so the user accidentally sets another eventInstance
    {
        private static readonly AudioInstance dummyAI = new();

        internal AudioInstanceRef(AudioInstance ai)//Prevents external creation
        {
            this.ai = ai;
            id = ai.id;
        }

        private readonly AudioInstance ai;
        private readonly int id;

        /// <summary>
        /// Returns the AudioInstance, returns a dummy AudioInstance if its invalid
        /// </summary>
        public AudioInstance _ai
        {
            get
            {
                if (id != ai.id) return dummyAI;
                return ai;
            }
        }

        /// <summary>
        /// Returns the AudioInstance if it is valid, otherwise returns null.
        /// </summary>
        public AudioInstance TryGetAudioInstance()
        {
            if (id != ai.id) return null;
            return ai;
        }

        public bool IsValid()
        {
            return id == ai.id;
        }
    }

    public class AudioInstance
    {
        internal static int nextId = 0;

        internal AudioInstance()//Prevents external creation
        {

        }

        /// <summary>
        /// Negative if inactive
        /// </summary>
        internal volatile int id = -1;
        internal EventInstance clip;
        internal EVENT_CALLBACK callback;
        internal AudioTracer.TraceInput traceInput = null;
        internal AudioZones.ZoneInput zoneInput = null;

        /// <summary>
        /// Assign only allowed on creation
        /// </summary>
        internal FMOD.DSP reverbFilter;

        /// <summary>
        /// Assign only allowed on creation
        /// </summary>
        internal FMOD.DSP lowpassFilter;
        internal Transform parentTrans = null;

        internal readonly object selfLock = new();

        /// <summary>
        /// Must be locked with selfLock
        /// </summary>
        internal State state;

        /// <summary>
        /// Write only allowed on creation
        /// </summary>
        internal AudioEffects audioEffects = AudioEffects.all;

        /// <summary>
        /// Worldspace real position of the source
        /// </summary>
        internal Vector3 position = Vector3.zero;
        /// <summary>
        /// Worldspace direction where the sound is coming from (Direction from player ear pos)
        /// </summary>
        internal Vector3 direction = Vector3.zero;
        /// <summary>
        /// Worldspace occluded distance from the player ear pos to the source
        /// </summary>
        internal float distance = 0.0f;
        /// <summary>
        /// If parentTrans != null, this is the local position of the source relative to parentTrans
        /// </summary>
        internal Vector3 posL = Vector3.zero;

        internal FMOD.ATTRIBUTES_3D fmod3D = new()
        {
            forward = new() { x = 0, y = 0, z = 1 },
            up = new() { x = 0, y = 1, z = 0 },
        };

        internal AudioTimelineData latestTimelineData = null;

        public delegate void Event_OnAudioCallback(AudioInstance ai, AudioCallback type, AudioTimelineData timelineData);
        /// <summary>
        /// Not invoked from main thread!
        /// (Please subscribe directly after calling PlaySound and always unsubscribe in this.OnAudioCallback when type == Stopped)
        /// </summary>
        public event Event_OnAudioCallback OnAudioCallback;

        internal void InvokeAudioCallback(AudioCallback type)
        {
            OnAudioCallback?.Invoke(this, type, latestTimelineData);
        }

        public delegate ProgrammerSoundInput Event_OnProgrammerSound(AudioInstance ai, string programmerName, bool finished);
        /// <summary>
        /// Not invoked from main thread!
        /// (Please subscribe directly after calling PlaySound and always unsubscribe in this.OnAudioCallback when type == Stopped)
        /// Should always return null if finished == true and never return null if its false
        /// </summary>
        public event Event_OnProgrammerSound OnProgrammerSound;

        internal ProgrammerSoundInput InvokeProgrammerSound(string programmerName, bool finished)
        {
            if (OnProgrammerSound == null)
            {
                Debug.LogError("Nothing is subscribed to OnProgrammerSound, either remove programmerSounds in Fmod studio or subscribe!");
                return null;
            }

            return OnProgrammerSound.Invoke(this, programmerName, finished);
        }

        internal enum State
        {
            pendingCreation = 0,
            pendingPlay = 10,
            playing = 20,
            pendingDestroy = 30,
            destroyed = 40
        }

        public void SetProps(AudioProps newProps)
        {
            if (newProps == null) return;

            SetParent(newProps.attatchTo);
            SetPosition(newProps.pos);
            if (newProps.customProps != null) SetCustomProps(newProps.customProps);
        }

        /// <summary>
        /// Does not check if newCustomProps is null
        /// </summary>
        public void SetCustomProps(CustomProp[] newCustomProps)
        {
            foreach (CustomProp prop in newCustomProps)
            {
                clip.setParameterByName(prop.name, prop.value);
            }
        }

        public void SetCustomProp(CustomProp prop)
        {
            clip.setParameterByName(prop.name, prop.value);
        }

        public void SetCustomProp(string name, float value)
        {
            clip.setParameterByName(name, value);
        }

        public void SetPosition(Vector3 newPos)
        {
            if (parentTrans != null) posL = parentTrans.InverseTransformPoint(newPos);
            position = newPos;
        }

        public void SetParent(Transform newParent)
        {
            //Clear old parent
            if (parentTrans != null) parentTrans = null;
            if (newParent == parentTrans) return;

            //newParent is garanteed to not be null here
            posL = newParent.InverseTransformPoint(position);
            parentTrans = newParent;
        }

        /// <summary>
        /// Returns true if the source is currently playing an event (True even if paused)
        /// </summary>
        public bool IsPlaying()
        {
            return state == State.playing;
        }

        /// <summary>
        /// Returns true if the source is or will be playing an event (True even if paused)
        /// </summary>
        public bool IsActive()
        {
            return id >= 0;
        }

        public bool IsPaused()
        {
            return clip.getPaused(out bool paused) != FMOD.RESULT.OK && paused == true;
        }

        /// <summary>
        /// Stops playing this source. if immediately == false, FMOD fadeout is allowed
        /// </summary>
        public void Stop(bool immediately = false)
        {
            if (clip.isValid() == false) return;

            clip.stop(immediately == false ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT
                : FMOD.Studio.STOP_MODE.IMMEDIATE);
        }

        /// <summary>
        /// Pauses or unpauses this source (Wont unpause if audio is paused globally)
        /// </summary>
        public void SetPaused(bool pause)
        {
            if (clip.isValid() == false) return;
            clip.setPaused(pause);
        }

        public void SetVolume(float newVolume)
        {
            clip.setVolume(newVolume);
        }

        public void SetPitch(float newPitch)
        {
            clip.setPitch(newPitch);
        }

        #region Effects

        internal void TickSource(float deltaTime)
        {
            //Update state
            lock (selfLock)
            {
                if (state == State.pendingPlay && (traceInput == null || traceInput.resultIsReady == true)
                    && (zoneInput == null || zoneInput.resultIsReady == true))
                {
                    clip.start();
                    InvokeAudioCallback(AudioCallback.started);
                    state = State.playing;
                }

                if (state != State.playing) return;
            }

            //Update 3D
            //Update pos
            if (parentTrans != null)
            {
                position = parentTrans.TransformPoint(posL);
            }

            //Update effects
            if (traceInput != null)
            {
                direction = traceInput.resDirection;
                distance = traceInput.resDistance;

                //Bounce brightness (0.0 == di0, de100: 0.5 == di50, de50, 1.0, di100, de0)
                reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DIFFUSION, Mathf.Lerp(0.0f, 100.0f, traceInput.resSurface.brightness));
                reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DENSITY, Mathf.Lerp(100.0f, 0.0f, traceInput.resSurface.brightness));

                reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HIGHCUT, Mathf.Lerp(20.0f, 4000.0f, traceInput.resSurface.metallicness));

                reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.EARLYLATEMIX, Mathf.Lerp(0.0f, 66.0f, traceInput.resSurface.tail * 2.0f));
                reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HFDECAYRATIO, Mathf.Lerp(0.0f, 100.0f, (traceInput.resSurface.tail - 0.5f) * 2.0f));

                //float wetL = Mathf.Lerp(-80.0f, 20.0f, traceData.resSurface.reflectness);
                //float wetL = Mathf.Lerp(-80.0f, 20.0f, traceData.resSurface.reflectness);
                float wetL = 10.377f * Mathf.Log(Mathf.Clamp01(traceInput.resSurface.reflectness)) + 95.029f; 
                reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DECAYTIME, Mathf.Exp(0.0981f * wetL));
                //reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DECAYTIME, 2.0f * Mathf.Exp(0.0906f * (wetL + 80.00001f)));

                lowpassFilter.setParameterFloat((int)FMOD.DSP_LOWPASS_SIMPLE.CUTOFF, -2409 * Mathf.Log(traceInput.resSurface.passthrough) - 217.15f);
            }
            else
            {
                direction = (position - AudioManager.camPos).normalized;
                distance = (position - AudioManager.camPos).magnitude;
            }

            //Apply 3D
            fmod3D.position = (AudioManager.camPos + (direction * (distance / AudioSettings._minMaxDistanceFactor))).ToFMODVector();
            clip.set3DAttributes(fmod3D);
        }

        internal void Dispose(bool destroying)
        {
            if (audioEffects.HasAny() == true)
            {
                FMOD.RESULT result = clip.getChannelGroup(out FMOD.ChannelGroup cg);
                if (result != FMOD.RESULT.OK)
                {
                    Debug.LogError("Error getting ChannelGroup for " + this);
                    return;
                }

                if (reverbFilter.hasHandle())
                {
                    cg.removeDSP(reverbFilter);
                    reverbFilter.release();
                }

                if (lowpassFilter.hasHandle() == true)
                {
                    cg.removeDSP(lowpassFilter);
                    lowpassFilter.release();
                }
            }

            if (destroying == false && clip.isValid() == true)
            {
                clip.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                clip.release();
                clip.clearHandle();
            }
        }

        #endregion Effects
    }
}


