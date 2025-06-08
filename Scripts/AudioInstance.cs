using UnityEngine;
using FMOD.Studio;

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
            if (ai == null || id != ai.id || this.ai.GetStateSafe() == AudioInstance.State.pendingStop) return null;
            return ai;
        }

        public bool TryGetAudioInstance(out AudioInstance ai)
        {
            if (this.ai == null || id != this.ai.id || this.ai.GetStateSafe() == AudioInstance.State.pendingStop)
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
            return ai != null && id == ai.id && ai.GetStateSafe() != AudioInstance.State.pendingStop;
        }
    }

    public class AudioInstance
    {
        internal static int nextId = 1;//0 is unitlized

        internal AudioInstance()//Prevents external creation
        {

        }

        internal struct ReverbData
        {
            internal ReverbData(AudioInstance ai)
            {
                pos = ai.position;
                reverbFilter = ai.reverbFilter;
                surf = new(-2.0f);
                isNew = true;
            }

            internal FMOD.DSP reverbFilter;
            internal Vector3 pos;
            internal AudioSurface.Surface surf;
            internal bool isNew;
        }

        internal struct BasicData
        {
            internal BasicData(AudioInstance ai)
            {
                pos = ai.position;
                lowpassFilter = ai.lowpassFilter;
                clip = ai.clip;
                direction = Vector3.zero;
                distance = -1.0f;
            }

            internal Vector3 pos;
            internal FMOD.DSP lowpassFilter;
            internal EventInstance clip;
            internal Vector3 direction;
            internal float distance;
        }

        /// <summary>
        /// Negative or 0 if inactive
        /// </summary>
        internal volatile int id = -1;
        internal int prevReverbDataI = -1;
        internal int prevBasicDataI = -1;
        internal EventInstance clip;
        internal EVENT_CALLBACK callback;
        internal AudioEffects audioEffects = AudioEffects.all;
        internal bool hasReverb = true;
        internal bool isPersistent = true;
        /// <summary>
        /// Is float.MaxValue if no spatializer, in fmod distance
        /// </summary>
        internal float maxDistance = 20.0f;

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
        /// Not invoked from main thread! (timelineData may be for old event if type has not been invoked on this event before)
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
            pendingStop = 25,
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
            State state = GetStateSafe();
            return state == State.playing || state == State.pendingStop;//Pending stop is still playing
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

            if (state == State.playing) SetStateSafe(State.pendingStop);
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
            //distance = -1.0f;
            prevReverbDataI = -1;
            prevBasicDataI = -1;
        }

        internal void TickSource(float deltaTime, ref int aiReverbDataI, ref int aiBasicDataI)
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
                if (state == State.pendingPlay && (audioEffects.HasOcclusion() == false || AudioOcclusion.hasComputedOcclusion == true))
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

            //Basic data
            if (aiReverbDataI + 1 >= AudioSettings._maxActiveVoices)
            {
                Debug.LogError("Too many active voices, please increase AudioSettings.maxActiveVoices or play less audio: " + AudioSettings._maxActiveVoices);
                return;
            }

            if (prevBasicDataI > -1)
            {
                var basicD = AudioBasics.aisBasicData_native[prevBasicDataI];
                basicD.pos = position;
                direction = basicD.direction;
                distance = basicD.distance;
                AudioBasics.aisBasicData_native[aiBasicDataI] = basicD;
            }
            else AudioBasics.aisBasicData_native[aiBasicDataI] = new(this);

            prevBasicDataI = aiBasicDataI++;

            //Reverb data
            if (hasReverb == true)
            {
                //clip.isVirtual(out bool isVirtual);//isVirtual is always false, does not work. Also tried getting it from channels but they are also always false
                if (distance / AudioSettings._minMaxDistanceFactor <= maxDistance)//No reason to update reverb if too far away from source
                {
#if UNITY_EDITOR
                    if (aiReverbDataI + 2 >= AudioSettings._maxReverbVoices)//+1 for the internal one
                    {
                        Debug.LogError("Too many voices with reverb, please increase AudioSettings.maxReverbVoices or play less audio with reverb: " + (AudioSettings._maxReverbVoices - 1));
                        return;
                    }
#endif

                    if (prevReverbDataI > -1)
                    {
                        var reverbD = AudioReverb.aisReverbData_native[prevReverbDataI];
                        reverbD.pos = position;
                        AudioReverb.aisReverbData_native[aiReverbDataI] = reverbD;
                    }
                    else AudioReverb.aisReverbData_native[aiReverbDataI] = new(this);

                    prevReverbDataI = aiReverbDataI++;
                }
                else prevReverbDataI = -1;
            }

            //Debug
#if UNITY_EDITOR
            if (AudioSettings._debugMode == DebugMode.drawAudioDirection)
            {
                Debug.DrawLine(AudioManager.camPos, AudioManager.camPos + (0.25f * distance * direction), Color.magenta, 0.1f);
            }
#endif
        }

        internal void Dispose()//Destroyed state is set before calling this
        {
            if (clip.isValid() == false) return;

            if (clip.getChannelGroup(out FMOD.ChannelGroup cg) == FMOD.RESULT.OK)
            {//Its expected to fail if called before OnCreated() or occlusion was toggled
                if (lowpassFilter.hasHandle() == true)
                {
                    cg.removeDSP(lowpassFilter);
                    lowpassFilter.release();
                }

                if (reverbFilter.hasHandle())
                {
                    cg.removeDSP(reverbFilter);
                    reverbFilter.release();
                }
            }

            clip.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            clip.release();
            clip.clearHandle();
        }

        #endregion Effects
    }
}


