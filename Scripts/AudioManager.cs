using FMOD.Studio;
using FMODUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace RaytracedAudio
{
    public class AudioManager : MonoBehaviour
    {
        #region Singleton

        private static AudioManager instance;
        internal static AudioManager _instance
        {
            get
            {
                if (instance != null) return instance;

                instance = GameObject.FindAnyObjectByType<AudioManager>(FindObjectsInactive.Exclude);
                if (instance == null)
                {
                    GameObject newObj = new("Audio Manager")
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };

                    instance = newObj.AddComponent<AudioManager>();
                }

                if (instance.transform.childCount > 0) Debug.Log("The AudioManager should not have any children but " + instance.transform.name + " has!");
                instance.gameObject.SetActive(true);
                DontDestroyOnLoad(instance.gameObject);

                instance.Init();
                return instance;
            }
        }

        #endregion Singleton

        #region Main

        private void Awake()
        {
            Init();
        }

        private bool isInitilized = false;
        private StudioListener listener = null;

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

        private void Init()
        {
            if (isInitilized == true) return;

            SetListener();
            isInitilized = true;
        }

        private void OnDestroy()
        {
            Destroy();
        }

        private void Destroy()
        {
            if (isInitilized == false) return;
            isInitilized = false;
        }

        /// <summary>
        /// Last position of the listener
        /// </summary>
        internal static Vector3 camPos;

        private void Update()
        {
            if (isInitilized == false) return;
            if (SetListener() == false) return;

            camPos = listener.transform.position;

            //Tick sources
            float deltaTime = Time.deltaTime;

            foreach (AudioInstance ai in allAIs)
            {
                ai.TickSource(deltaTime);
            }
        }

        #endregion Main

        #region Play/Stop sounds

        private readonly Dictionary<IntPtr, AudioInstance> handleToAI = new(32);
        private readonly List<AudioInstance> allAIs = new(32);

        public AudioInstance PlaySound(AudioConfig config, AudioProps props = null)
        {
            //Singletone
            if (config.singletone == true && handleToAI.TryGetValue(config.lastPlayedEventHandle, out AudioInstance ai) == true)
            {
                ai.SetParent(config.attatchTo);
                ai.SetProps(props);
                return ai;
            }

            //Get source
            ai = new AudioInstance
            {
                clip = RuntimeManager.CreateInstance(config.clip),
                state = AudioInstance.State.pendingCreation,
            };

            ai.traceInput = config.audioEffects.HasTracing() == true ? AudioTracer.CreateTraceInput(ai) : null;
            ai.zoneInput = config.audioEffects.HasZones() == true ? AudioZones.CreateZoneInput(ai) : null;

            config.lastPlayedEventHandle = ai.clip.handle;
            handleToAI.Add(ai.clip.handle, ai);
            allAIs.Add(ai);

            //Configure
            if (props != null) ai.fmod3D.position = props.pos.ToFMODVector();
            ai.clip.set3DAttributes(ai.fmod3D);//Must be called to not get warning
            ai.SetParent(config.attatchTo);
            ai.SetProps(props);

            //Callbacks
            ai.clip.setCallback(EventCallback, FMOD.Studio.EVENT_CALLBACK_TYPE.CREATED | EVENT_CALLBACK_TYPE.STOPPED | FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROYED);
            return ai;
        }

        private static FMOD.RESULT EventCallback(FMOD.Studio.EVENT_CALLBACK_TYPE type, IntPtr instance, IntPtr parameters)
        {
            AudioManager am = _instance;

            FMOD.RESULT result = FMOD.RESULT.OK;
            AudioInstance ai = am.handleToAI[instance];

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
                default:
                    result = FMOD.RESULT.ERR_BADCOMMAND;
                    Debug.LogError(type + " not expected " + instance);
                    break;
            }

            return result;

            void OnCreated()
            {
                if (ai.state != AudioInstance.State.pendingCreation)
                {
                    Debug.Log(instance + " was not pending creation??");
                    return;
                }

                if (ai.audioEffects.HasAny() == true)
                {
                    //https://bobthenameless.github.io/fmod-studio-docs/generated/FMOD_DSP_SFXREVERB.html
                    //https://bobthenameless.github.io/fmod-studio-docs/generated/FMOD_REVERB_PRESETS.html
                    result = ai.clip.getChannelGroup(out FMOD.ChannelGroup cg);
                    RuntimeManager.CoreSystem.createDSPByType(FMOD.DSP_TYPE.LOWPASS_SIMPLE, out ai.lowpassFilter);

                    if (result != FMOD.RESULT.OK)
                    {
                        Debug.LogError("Error getting ChannelGroup for " + instance);
                        return;
                    }

                    ai.lowpassFilter.setParameterFloat((int)FMOD.DSP_LOWPASS_SIMPLE.CUTOFF, 17000.0f);//Decrease based on underwater and behind wall, 400~ sounded good for water
                    cg.addDSP(1, ai.lowpassFilter);

                    if (ai.audioEffects.HasZones() == true)
                    {

                    }

                    if (ai.audioEffects.HasTracing() == true)
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
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.LOWSHELFFREQUENCY, 250.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.LOWSHELFGAIN, 0.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HIGHCUT, 20.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.EARLYLATEMIX, 96.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.WETLEVEL, -80.0f);
                        ai.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DRYLEVEL, 0.0f);

                        //decayTime, hfDecayRatio, diffusion, density, highcut, earlyLateMix, wetLevel


                        ////Metal pipe
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DECAYTIME, 2800.0f);//Increase this based on how many times we can bounce.
                        //                                                                              //Increase based on surface prop, specific prop for this to allow for sound proofing.
                        //                                                                              //In theory, increasing this also based on direct visible bounce count would be more correct.
                        //                                                                              //fuck that for now tho.
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.EARLYDELAY, 14.0f);//Delay before first reflection, increase based on room size.
                        //                                                                             //Avg distance to all directly visible points?
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.LATEDELAY, 21.0f);//Cant tell the difference when increasing this alone, only if increased alongside EARLYDELAY,
                        //                                                                            //So I guess always increase both together?
                        //
                        ////These should be controlled by surface type
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HFREFERENCE, 5000.0f);//Always 5000
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HFDECAYRATIO, 79.0f);
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DIFFUSION, 100.0f);
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DENSITY, 100.0f);//Reflection brightness, lower = brighter.
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.LOWSHELFFREQUENCY, 250.0f);//Always 250
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.LOWSHELFGAIN, 0.0f);//Always 0
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HIGHCUT, 3400.0f);
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.EARLYLATEMIX, 66.0f);
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.WETLEVEL, 1.2f);
                        //ss.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DRYLEVEL, 0.0f);

                        cg.addDSP(0, ai.reverbFilter);
                    }
                }

                ai.state = AudioInstance.State.pendingPlay;
                Debug.Log("Created");
            }

            void OnStopped()//Does not get called on exit playmode (But release() aint needed then neither)
            {
                ai.clip.release();//Dispose
                ai.state = AudioInstance.State.pendingDestroy;
                Debug.Log("Stopped");
            }

            void OnDestroy()
            {
                AudioTracer.RemoveTraceInput(ai.traceInput);
                am.allAIs.Remove(ai);
                am.handleToAI.Remove(instance);

                ai.state = AudioInstance.State.destroyed;
                Debug.Log("Destoyed");
            }
        }

        #endregion Play/Stop sounds

        private static bool audioIsPaused = false;
        private static readonly List<string> pausableBusPaths = new()
        {
            "bus:/Player",
            "bus:/Enviroment"
        };

        /// <summary>
        /// Pauses or unpauses all pausable audio sources (Wont unpause sources paused separately)
        /// </summary>
        public static void SetAudioPaused(bool toPaused)
        {
            if (audioIsPaused == toPaused) return;
            audioIsPaused = toPaused;

            foreach (string path in pausableBusPaths)
            {
                RuntimeManager.GetBus(path).setPaused(toPaused);
            }
        }

        /// <summary>
        /// Stops all pausable audio sources. if immediately == false, FMOD fadeout is allowed
        /// </summary>
        public static void StopAudio(bool immediately = false)
        {
            foreach (string path in pausableBusPaths)
            {
                RuntimeManager.GetBus(path).stopAllEvents(immediately == false ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT
                    : FMOD.Studio.STOP_MODE.IMMEDIATE);
            }
        }
    }
}


