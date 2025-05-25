using FMOD.Studio;
using FMODUnity;
using RaytracedAudio;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

public class AudioSettings : ScriptableObject
{
    #region Singleton

    private static AudioSettings instance = null;
    public static AudioSettings _instance
    {
        get
        {
            if (instance != null) return instance;
            instance = Resources.Load<AudioSettings>("AudioGlobalSettings");
            if (instance == null) Debug.LogError("Expected AudioGlobalSettings asset to be found at `UnityRaytracedAudio/Resources/AudioGlobalSettings`, has it been deleted?");
            else instance.Setup();

            return instance;
        }
    }

    #endregion Singletone

#if UNITY_EDITOR
    private void OnValidate()
    {
        Setup();
    }
#endif

    private void Setup()
    {
        _buses = buses.ToArray();
        busPathToBus.Clear();

        foreach (BusConfig bus in buses)
        {
            bus.bus.clearHandle();//To make sure their path is correct
            if (busPathToBus.TryAdd(bus.path, bus) == true || Application.isPlaying == false) continue;
            Debug.Log("Bus path " + bus.path + " exists more than ones!");
        }
    }

    [Tooltip("Should contain all your buses")]
    [SerializeField] private List<BusConfig> buses = new(1) { new() };
    private static BusConfig[] _buses = new BusConfig[0];
    private static readonly Dictionary<string, BusConfig> busPathToBus = new(6);

    /// <summary>
    /// Returns master bus if string is empty, null if path is invalid, otherwise returns bus at path.
    /// </summary>
    public static BusConfig GetBus(string busPath = "")
    {
        if (busPathToBus.TryGetValue(busPath, out BusConfig bus))
        {
            return bus;
        }

        Debug.LogError($"Bus with path {busPath} not found");
        return null;
    }

    [Tooltip("Disable effects globally for all sounds (Only affects sounds played after this was set)")]
    [SerializeField] private AudioEffects globalAudioEffects = AudioEffects.all;
    public static AudioEffects _globalAudioEffects
    {
        get => _instance.globalAudioEffects;
        set
        {
            _instance.globalAudioEffects = value;
        }
    }

    private static bool audioIsPaused = false;
    public static bool _isAudioPaused => audioIsPaused;

    /// <summary>
    /// Pauses or unpauses all pausable (or all if forceSet is true) audio sources (Wont unpause sources paused separately)
    /// </summary>
    public static void SetAudioPaused(bool toPaused, bool forceSet = false)
    {
        if (audioIsPaused == toPaused) return;
        audioIsPaused = toPaused;

        foreach (BusConfig bus in _buses)
        {
            bus.SetPaused(toPaused, forceSet);
        }
    }

    /// <summary>
    /// Stops all pausable (or all if forceStop is true) audio sources. if immediately == false, FMOD fadeout is allowed
    /// </summary>
    public static void StopAudio(bool immediately = false, bool forceStop = false)
    {
        STOP_MODE mode = immediately == false ? STOP_MODE.ALLOWFADEOUT : STOP_MODE.IMMEDIATE;

        foreach (BusConfig bus in _buses)
        {
            bus.StopAll(mode, forceStop);
        }
    }
}
