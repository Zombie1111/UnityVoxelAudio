using FMOD.Studio;
using FMODUnity;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VoxelAudio
{
    public class AudioManager : MonoBehaviour
    {
        #region Singleton

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (Application.isPlaying == true)
            {
                AudioOcclusion.DebugDrawGizmos();
                return;
            }

            Debug.LogError("AudioManager should never be created manually!");
            DestroyImmediate(this);
        }
#endif

        private static AudioManager instance;
        public static AudioManager _instance
        {
            get
            {
                if (instance != null) return instance;

                instance = GameObject.FindAnyObjectByType<AudioManager>(FindObjectsInactive.Exclude);
                if (instance == null)
                {
                    GameObject newObj = new("Audio Manager")
                    {
                        hideFlags =
#if UNITY_EDITOR
                        AudioSettings._debugMode != DebugMode.none ? HideFlags.None :
#endif
                        HideFlags.HideInHierarchy
                    };

                    instance = newObj.AddComponent<AudioManager>();
                }
                else Debug.Log("Audio Manager may have been created manually!");

                if (instance.transform.childCount > 0) Debug.Log("The AudioManager should not have any children but " + instance.transform.name + " has!");
                instance.gameObject.SetActive(true);
                DontDestroyOnLoad(instance.gameObject);

                instance.Init();
                return instance;
            }
        }

        #endregion Singleton

        #region Main

        private bool isInitilized = false;
        private static StudioListener listener = null;
        public static StudioListener _listener
        {
            get
            {
                if (listener == null && Application.isPlaying == true) Initilize();
                return listener;
            }
        }

        /// <summary>
        /// Sets the listener to use, if null it will try to find one in the scene. Returns true if listener is valid
        /// </summary>
        public bool SetListener(StudioListener newListener = null)
        {
            if (newListener == null)
            {
                if (listener != null) return true;

                newListener = GameObject.FindAnyObjectByType<StudioListener>(FindObjectsInactive.Include);
                if (newListener == null)
                {
                    Debug.LogError("No valid FMod StudioListener was found!");
                    return false;
                }
            }

            camPos = newListener.transform.position;
            listener = newListener;
            return true;
        }

        internal void Init()
        {
            if (isInitilized == true) return;

            SceneManager.sceneUnloaded += OnSceneUnLoaded;
            AudioSurface.Allocate();//Accessed in AudioSettings so must be initlized before it
            AudioSettings._instance.Init();//Called with true in AudioSurface.Allocate() 
            AudioBasics.Allocate();
            AudioReverb.Allocate();
            SetListener();
            AudioOcclusion.Init();

            isInitilized = true;
        }

        /// <summary>
        /// Makes sure the voxel audio system is initilized, accessing _instance will also initilize it
        /// </summary>
        public static void Initilize()
        {
            _instance.Init();
        }

        private void OnSceneUnLoaded(Scene scene)//We cant stop in OnSceneloaded since it runs after Awake()
        {
            if (AudioSettings._stopAudioOnSceneLoad == false) return;
            AudioSettings.StopAllAudio();
        }

        private void OnDestroy()
        {
            Destroy();
        }

        private void Destroy()
        {
            if (isInitilized == false) return;
            isInitilized = false;

            AudioReverb.Dispose();
            AudioBasics.Dispose();
            AudioSurface.Dispose();
            AudioOcclusion.Destroy();
            SceneManager.sceneUnloaded -= OnSceneUnLoaded;
            AudioInstance[] allAIs = GetAllAudioInstancesSafe();

            foreach (AudioInstance ai in allAIs)
            {
                ai.Destroy();
            }
        }

        /// <summary>
        /// Latest position of the listener
        /// </summary>
        internal static Vector3 camPos;
        /// <summary>
        /// Inverted, so 1.0f is no water
        /// </summary>
        private float underwaterAmount = 1.0f;
        internal static float currentUnderwaterFreq = AudioSettings._noLowPassFreq;

        private void Update()
        {
            if (isInitilized == false) return;
            if (SetListener() == false) return;

            camPos = listener.transform.position;

            //Tick underwater
            float deltaTime = Time.deltaTime;
            underwaterAmount = Mathf.Lerp(underwaterAmount, AudioSettings._isUnderwater == false ? 1.0f : 0.0f, AudioSettings._waterLerpSpeed * deltaTime);
            currentUnderwaterFreq = Mathf.Lerp(AudioSettings._underwaterLowPassFreq, AudioSettings._noLowPassFreq, underwaterAmount * underwaterAmount);

            //Tick sources
            int aiReverbDataI = 0;
            int aiBasicDataI = 0;

            AudioBasics.EndBasicsCompute();
            AudioReverb.EndReverbCompute();
            foreach (AudioInstance ai in GetAllAudioInstancesSafe())
            {
                ai.TickSource(deltaTime, ref aiReverbDataI, ref aiBasicDataI);
            }
            AudioReverb.StartReverbCompute(deltaTime, aiReverbDataI);
            AudioBasics.StartBasicsCompute(deltaTime, aiBasicDataI);
        }

        private void LateUpdate()
        {
            AudioReverb.CompleteReverbRays();
        }

        private void OnDisable()
        {
            if (enabled == true) return;
            Debug.LogError("The audio manager should never be disabled!");
        }

        #endregion Main

        #region Play/Stop sounds

        internal readonly object aiContainersLock = new();

        /// <summary>
        /// Must be locked with aiContainersLock
        /// </summary>
        private readonly Dictionary<IntPtr, AudioInstance> handleToAI = new(64);
        /// <summary>
        /// Must be locked with aiContainersLock
        /// </summary>
        private readonly List<AudioInstance> allAIs = new(64);

        /// <summary>
        /// Returns all active AudioInstances
        /// </summary>
        public AudioInstance[] GetAllAudioInstancesSafe()
        {
            AudioInstance[] ais;
            lock (aiContainersLock)
            {
                ais = allAIs.ToArray();
            }
            
            return ais;
        }

        public AudioInstanceWrap PlaySound(AudioReference aRef, AudioProps aProps = null)
        {
            //Get audio instance
            AudioConfigAsset aConfig = aRef.GetAudioConfig();
            AudioInstance ai = new()
            {
                callback = new EVENT_CALLBACK(EventCallback),
                clip = RuntimeManager.CreateInstance(aRef.clip),
                latestTimelineData = new(),
                isPersistent = aConfig.persistent,
                audioEffects = aConfig.audioEffects,
                reverbIgnoreSelfRays = aConfig.reverbIgnoreSelfRays,
            };

            if (aConfig.isSpatialized == true) ai.clip.getMinMaxDistance(out _, out ai.maxDistance);
            else ai.maxDistance = float.MaxValue;

            lock (aiContainersLock)
            {
                handleToAI.Add(ai.clip.handle, ai);
            }

            //Get effects inputs
            ai.ResetSource();
            ai.state = AudioInstance.State.pendingCreation;
            ai.hasReverb = aConfig.audioEffects.HasReverb();

            //Register audio instance
            ai.id = AudioInstance.nextId;
            AudioInstance.nextId++;

            lock (aiContainersLock)
            {
                allAIs.Add(ai);//Its worth adding/removing everytime to avoid unnecessary ticking
            }

            //Configure
            if (aProps != null) ai.fmod3D.position = aProps.pos.ToFMODVector();
            ai.clip.set3DAttributes(ai.fmod3D);//Must be called to not get warning
            ai.SetProps(aProps);

            //Callbacks
            EVENT_CALLBACK_TYPE callMask = EVENT_CALLBACK_TYPE.CREATED | EVENT_CALLBACK_TYPE.STOPPED | EVENT_CALLBACK_TYPE.DESTROYED
                | EVENT_CALLBACK_TYPE.TIMELINE_MARKER | EVENT_CALLBACK_TYPE.TIMELINE_BEAT//Wont affect performance since they are only recieved if the event has markers/beats/programmers
                | EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND | EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND;

            ai.clip.setCallback(ai.callback, callMask);
            return new(ai);
        }

        [AOT.MonoPInvokeCallback(typeof(FMOD.Studio.EVENT_CALLBACK))]
        internal static FMOD.RESULT EventCallback(FMOD.Studio.EVENT_CALLBACK_TYPE type, IntPtr clipPTR, IntPtr parameterPtr)
        {
            //Not invoked from main thread
            //FMod freezes if any exception is thrown from EvenCallback
            AudioManager am = instance;
            if (am == null) return FMOD.RESULT.OK;//Expected to happen on exit

            FMOD.RESULT result = FMOD.RESULT.OK;
            AudioInstance ai;
            lock (am.aiContainersLock)
            {
                ai = am.handleToAI[clipPTR];
            }

            switch (type)
            {
                case EVENT_CALLBACK_TYPE.CREATED:
                    OnCreated();
                    break;
                case EVENT_CALLBACK_TYPE.STOPPED:
                    OnStopped();
                    break;
                case EVENT_CALLBACK_TYPE.DESTROYED:
                    OnDestroy();
                    break;
                case EVENT_CALLBACK_TYPE.TIMELINE_BEAT:
                    OnTimeline(true);
                    break;
                case EVENT_CALLBACK_TYPE.TIMELINE_MARKER:
                    OnTimeline(false);
                    break;
                case EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND:
                    OnProgrammerSound(false);
                    break;
                case EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND:
                    OnProgrammerSound(true);
                    break;
                default:
                    result = FMOD.RESULT.ERR_BADCOMMAND;
                    Debug.LogError(type + " not expected " + clipPTR);
                    break;
            }

            return result;

            //https://www.fmod.com/docs/2.00/unity/examples-timeline-callbacks.html
            void OnTimeline(bool isBeat)
            {
                if (isBeat == true)
                {
                    ai.latestTimelineData.lastBeat = (TIMELINE_BEAT_PROPERTIES)Marshal.PtrToStructure(parameterPtr, typeof(TIMELINE_BEAT_PROPERTIES));
                }
                else
                {
                    ai.latestTimelineData.lastMarker = (TIMELINE_MARKER_PROPERTIES)Marshal.PtrToStructure(parameterPtr, typeof(TIMELINE_MARKER_PROPERTIES));
                }

                ai.InvokeAudioCallback(isBeat == true ? AudioCallback.beat : AudioCallback.marker);
            }

            //https://fmod.com/docs/2.02/unity/examples-programmer-sounds.html
            void OnProgrammerSound(bool finished)
            {
                var psProps = (PROGRAMMER_SOUND_PROPERTIES)Marshal.PtrToStructure(parameterPtr, typeof(PROGRAMMER_SOUND_PROPERTIES));
                var psInput = ai.InvokeProgrammerSound(psProps.name, finished);

                if (finished == false)
                {
                    psProps.sound = psInput.soundToPlay.handle;
                    psProps.subsoundIndex = psInput.subsoundIndex;
                    Marshal.StructureToPtr(psProps, parameterPtr, false);

                    return;
                }

                if (psInput != null) Debug.LogError("OnProgrammerSound should always return null if finished == true, sound wont be disposed!");

                var sound = new FMOD.Sound(psProps.sound);
                sound.release();
            }

            void OnCreated()
            {
                if (ai.clip.isValid() == false) return;
                if (ai.GetStateSafe() == AudioInstance.State.pendingCreation)
                    ai.SetStateSafe(AudioInstance.State.pendingPlay);//We always wanna setup filter stuff but not set state if stopped manually before OnCreated()

                if (ai.audioEffects.HasOcclusion() == true || ai.hasReverb == true)
                {
                    //https://bobthenameless.github.io/fmod-studio-docs/generated/FMOD_DSP_SFXREVERB.html
                    //https://bobthenameless.github.io/fmod-studio-docs/generated/FMOD_REVERB_PRESETS.html
                    result = ai.clip.getChannelGroup(out FMOD.ChannelGroup cg);
                    if (result != FMOD.RESULT.OK)
                    {
                        Debug.LogError("Error getting ChannelGroup for " + clipPTR);
                        return;
                    }

                    if (ai.hasReverb == true)
                    {
                        RuntimeManager.CoreSystem.createDSPByType(FMOD.DSP_TYPE.SFXREVERB, out ai.reverbFilter);

                        //Below is og for off
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DECAYTIME, 1000.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.EARLYDELAY, 7.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.LATEDELAY, 11.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HFREFERENCE, 5000.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HFDECAYRATIO, 100.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DIFFUSION, 100.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DENSITY, 100.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HIGHCUT, 20.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.EARLYLATEMIX, 96.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.WETLEVEL, 0.0f);//-80.0f for off
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DRYLEVEL, 0.0f);
                        //ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.LOWSHELFFREQUENCY, 250.0f);
                        //ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.LOWSHELFGAIN, 0.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.LOWSHELFFREQUENCY, 200f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.LOWSHELFGAIN, -5f);

                        cg.addDSP(0, ai.reverbFilter);
                    }

                    if (ai.audioEffects.HasOcclusion() == true)
                    {
                        RuntimeManager.CoreSystem.createDSPByType(FMOD.DSP_TYPE.LOWPASS_SIMPLE, out ai.lowpassFilter);
                        ai.lowpassFilter.setParameterFloat((int)FMOD.DSP_LOWPASS_SIMPLE.CUTOFF, 17000.0f);//Decrease based on underwater and behind wall, 400~ sounded good for water
                        cg.addDSP(1, ai.lowpassFilter);
                    }
                }
            }

            void InActivate()
            {
                lock (am.aiContainersLock)
                {
                    am.allAIs.Remove(ai);
                }

                ai.InvokeAudioCallback(AudioCallback.stopped);
                ai.id = -1;//I may be reading this from other thread, however I know this is the only thread writing to it.
                           //Even if race occures it SHOULD not be able to cause any problems. I would prefer not to require locking with ai.selfLock since its read a alot
            }

            void OnStopped()//Does not get called on exit playmode, called manually so may be called before OnCreated
            {
                if (ai.id <= 0) return;

                InActivate();
                ai.Dispose();
                ai.SetStateSafe(AudioInstance.State.inActive);
            }

            void OnDestroy()
            {
                if (ai.id > 0) InActivate();
                if (ai.GetStateSafe() == AudioInstance.State.destroyed) return;

                ai.SetStateSafe(AudioInstance.State.destroyed);//Must be called first to avoid potential stack overflow
                ai.Dispose();

                lock (am.aiContainersLock)
                {
                    am.handleToAI.Remove(clipPTR);
                }
            }
        }

        #endregion Play/Stop sounds


    }
}


