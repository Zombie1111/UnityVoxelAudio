using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using UnityEditor.DeviceSimulation;

namespace RaytracedAudio
{
    public readonly struct AudioInstanceWrap//We cant use AudioInstance directly because it may be reused so the user accidentally sets another eventInstance
    {
        internal AudioInstanceWrap(AudioInstance ai)
        {
            this.ai = ai;
            id = ai.id;
        }

        internal readonly AudioInstance ai;
        private readonly int id;

        /// <summary>
        /// Returns the AudioInstance if it is valid, otherwise returns null.
        /// </summary>
        public AudioInstance TryGetAudioInstance()
        {
            if (ai == null || id != ai.id) return null;
            return ai;
        }

        public bool TryGetAudioInstance(out AudioInstance ai)
        {
            if (this.ai == null || id != this.ai.id)
            {
                ai = null;
                return false;
            }

            ai = this.ai;
            return true;
        }

        /// <summary>
        /// Returns true if the AudioInstance is still active and has not been reused for another AudioInstance
        /// </summary>
        public bool IsValid()
        {
            return ai != null && id == ai.id;
        }
    }

    public class AudioInstance
    {
        internal static int nextId = 1;//0 is unitlized

        internal AudioInstance()//Prevents external creation
        {

        }

        /// <summary>
        /// Negative or 0 if inactive
        /// </summary>
        internal volatile int id = -1;
        internal EventInstance clip;
        internal EVENT_CALLBACK callback;
        internal AudioTracer.TraceInput traceInput = null;
        internal AudioZones.ZoneInput zoneInput = null;
        internal bool isPersistent = true;

        /// <summary>
        /// Assign only allowed on creation
        /// </summary>
        internal FMOD.DSP reverbFilter;

        /// <summary>
        /// Assign only allowed on creation
        /// </summary>
        internal FMOD.DSP lowpassFilter;
        internal Transform parentTrans = null;
        internal bool hadParentTrans = false;

        private readonly object selfLock = new();

        /// <summary>
        /// Must be locked with selfLock
        /// </summary>
        internal State state;

        internal State GetStateSafe()
        {
            State state;

            lock (selfLock)
            {
                state = this.state;
            }

            return state;
        }

        internal void SetStateSafe(State newState)
        {
            lock (selfLock)
            {
                state = newState;
            }
        }

        /// <summary>
        /// Write only allowed on creation/play
        /// </summary>
        internal AudioEffects audioEffects = AudioEffects.all;

        /// <summary>
        /// Worldspace real position of the source
        /// </summary>
        internal Vector3 position = Vector3.zero;
        /// <summary>
        /// Worldspace real position of the source
        /// </summary>
        public Vector3 _position => position;

        /// <summary>
        /// Worldspace direction where the sound is coming from (Direction from listener)
        /// </summary>
        internal Vector3 direction = Vector3.zero;
        /// <summary>
        /// Worldspace direction where the sound is coming from (Direction from listener)
        /// </summary>
        public Vector3 _direction => direction;

        /// <summary>
        /// Worldspace occluded distance from listener to the source
        /// </summary>
        internal float distance = -1.0f;
        /// <summary>
        /// Worldspace occluded distance from listener to the source
        /// </summary>
        public float _distance => distance;

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
            inActive = 30,
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
            hadParentTrans = newParent != null;
            if (parentTrans == newParent) return;

            if (newParent == null)
            {
                parentTrans = null;
                return;
            }

            posL = newParent.InverseTransformPoint(position);
            parentTrans = newParent;
        }

        /// <summary>
        /// Returns true if the source is currently playing an event (True even if paused)
        /// </summary>
        public bool IsPlaying()
        {
            return GetStateSafe() == State.playing;
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

            State state = GetStateSafe();
            if (state == State.pendingCreation || state == State.pendingPlay)//Event has not started playing yet, clip.stop() wont work
            {
                AudioManager.EventCallback(EVENT_CALLBACK_TYPE.STOPPED, clip.handle, System.IntPtr.Zero);
                return;
            }
            
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

        internal void Destroy()
        {
            if (clip.isValid() == false) return;
            AudioManager.EventCallback(EVENT_CALLBACK_TYPE.DESTROYED, clip.handle, System.IntPtr.Zero);
        }

        #region Effects

        internal void ResetSource()
        {
            distance = -1.0f;
        }

        internal void TickSource(float deltaTime)
        {
            //Is still valid?
            if (hadParentTrans == true && parentTrans == null)
            {
                Stop(true);
                return;
            }

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
                float dis = AudioTracer.SampleOcclusionAtPos(position, out Vector3 dir, out float ocAmount);
                ocAmount = 1.0f - ocAmount;
                float ocSpeed = AudioSettings._occlusionLerpSpeed / (1.0f + (dis / AudioSettings._voxComputeDistanceMeter));

                if (distance < 0.0f)
                {
                    distance = dis;
                    direction = dir;
                }
                else
                {
                    distance = Mathf.Lerp(distance, dis, AudioSettings._occlusionLerpSpeed * deltaTime);
                    direction = Vector3.Slerp(direction, dir, AudioSettings._occlusionLerpSpeed * deltaTime);

                }

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

                //lowpassFilter.setParameterFloat((int)FMOD.DSP_LOWPASS_SIMPLE.CUTOFF, -2409 * Mathf.Log(traceInput.resSurface.passthrough) - 217.15f);
                lowpassFilter.setParameterFloat((int)FMOD.DSP_LOWPASS_SIMPLE.CUTOFF, Mathf.Lerp(AudioSettings._fullyOccludedLowPassFreq, 17000.0f, ocAmount * ocAmount));
                Debug.Log(ocAmount * ocAmount);
            }
            else
            {
                direction = (position - AudioManager.camPos).normalized;
                distance = (position - AudioManager.camPos).magnitude;
            }

#if UNITY_EDITOR
            if (AudioSettings._debugMode == DebugMode.drawAudioDirection)
            {
                Debug.DrawLine(AudioManager.camPos, AudioManager.camPos + (0.25f * distance * direction), Color.magenta, 0.1f);
            }
#endif

            //Apply 3D
            fmod3D.position = (AudioManager.camPos + (direction * (distance / AudioSettings._minMaxDistanceFactor))).ToFMODVector();
            clip.set3DAttributes(fmod3D);
        }

        internal void Dispose(bool destroying)//Destroyed state is set before calling this
        {
            if (clip.isValid() == false) return;

            if (audioEffects.HasAny() == true && clip.getChannelGroup(out FMOD.ChannelGroup cg) == FMOD.RESULT.OK)
            {//Its expected to fail if called before OnCreated()
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

            clip.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            clip.release();
            clip.clearHandle();
        }

        #endregion Effects
    }
}


