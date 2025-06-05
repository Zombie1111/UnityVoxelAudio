using System.Collections.Generic;
using UnityEngine;
using zombVoxels;


namespace RaytracedAudio
{
    public class AudioSettings : ScriptableObject
    {
        #region Singleton

        private static AudioSettings instance = null;
        internal static AudioSettings _instance
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
            if (defaultAudioConfigAsset == null && Application.isPlaying == true)
            {
                defaultAudioConfigAsset = ScriptableObject.CreateInstance<AudioConfigAsset>();
                Debug.LogError("defaultAudioConfigAsset is not allow to be null! (Creating temp)");
            }

            _defaultAudioConfigAsset = defaultAudioConfigAsset;
            _buses = buses.ToArray();
            _minMaxDistanceFactor = minMaxDistanceFactor;
            _stopAudioOnSceneLoad = stopAudioOnSceneLoad;
            _mask = mask;
            _voxSnapDistance = voxSnapDistance;
            _occlusionLerpSpeed = occlusionLerpSpeed;
            __globalAudioEffects = globalAudioEffects;
            _voxComputeDistanceMeter = voxComputeDistance;
            _voxComputeDistanceVox = Mathf.RoundToInt((voxComputeDistance * 5) / VoxGlobalSettings.voxelSizeWorld);
            _indirectExtraDistanceVox = (ushort)Mathf.RoundToInt((indirectExtraDistanceMeter * 5) / VoxGlobalSettings.voxelSizeWorld);
            busPathToBus.Clear();

            foreach (BusConfig bus in buses)
            {
                bus.bus.clearHandle();//To make sure their path is correct
                if (busPathToBus.TryAdd(bus.path, bus) == true || Application.isPlaying == false)
                {
                    busPathToBus.TryAdd(bus.GetFullPath(), bus);
                    continue;
                }
                Debug.Log("Bus path " + bus.path + " exists more than ones!");
            }

#if UNITY_EDITOR
            _debugMode = debugMode;
            if (Application.isPlaying == true) AudioManager._instance.gameObject.hideFlags = debugMode != DebugMode.none ? HideFlags.None : HideFlags.HideInHierarchy;
#endif
        }

        /// <summary>
        /// Initilizes the AudioSettings if its not already initlized
        /// </summary>
        public void Init()//Just used to bus Setup() through _instance
        {

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
            _instance.Init();
            if (busPathToBus.TryGetValue(busPath, out BusConfig bus))
            {
                return bus;
            }

            Debug.LogError($"Bus with path {busPath} not found");
            return null;
        }

        [Tooltip("AudioConfigAsset to use if AudioReference.audioConfigOverride is null")]
        [SerializeField] private AudioConfigAsset defaultAudioConfigAsset = null;
        internal static AudioConfigAsset _defaultAudioConfigAsset;
        [Tooltip("Disable effects globally for all sounds (Only affects sounds played after this was set)")]
        [SerializeField] private AudioEffects globalAudioEffects = AudioEffects.all;
        internal static AudioEffects __globalAudioEffects = AudioEffects.all;
        [Tooltip("Global factor on how far away spatilized sounds can be heard")]
        [SerializeField] private float minMaxDistanceFactor = 3.0f;
        internal static float _minMaxDistanceFactor = 3.0f;
        [Tooltip("If true non persistent audio will be stopped on SceneManager.OnSceneUnloaded")]
        [SerializeField] private bool stopAudioOnSceneLoad = true;
        internal static bool _stopAudioOnSceneLoad = true;

        [Header("Audio Occlusion")]
        [Tooltip("Layers that has solid voxels, usually also player collidable")]
        [SerializeField] private LayerMask mask = Physics.AllLayers;
        internal static LayerMask _mask = Physics.AllLayers;
        [SerializeField] private int voxSnapDistance = 3;
        internal static int _voxSnapDistance = 3;
        [SerializeField] private float occlusionLerpSpeed = 5.0f;
        internal static float _occlusionLerpSpeed = 5.0f;
        [SerializeField] private float voxComputeDistance = 35.0f;
        internal static float _voxComputeDistanceMeter = 35.0f;
        internal static int _voxComputeDistanceVox = 350;
        [SerializeField] private ushort indirectExtraDistanceMeter = 20;
        internal static ushort _indirectExtraDistanceVox = 20;

#if UNITY_EDITOR
        [Header("Debug")]
        [SerializeField] private DebugMode debugMode = DebugMode.none;
        internal static DebugMode _debugMode = DebugMode.none;
#endif

        public static AudioEffects _globalAudioEffects
        {
            get => _instance.globalAudioEffects;
            set
            {
                _instance.globalAudioEffects = value;
                __globalAudioEffects = value;
            }
        }

        private static bool audioIsPaused = false;
        public static bool _isAudioPaused => audioIsPaused;

        /// <summary>
        /// Pauses or unpauses all pausable (or all if forceSet is true) audio sources (Wont unpause sources paused separately)
        /// </summary>
        public static void SetAudioPaused(bool pause, bool forceSet = false)
        {
            if (audioIsPaused == pause) return;
            audioIsPaused = pause;
            _instance.Init();

            foreach (BusConfig bus in _buses)
            {
                if (bus.isPausable == false && forceSet == false) continue;
                bus.SetPaused(pause);
            }
        }

        /// <summary>
        /// Stops all non persistent (or all if forceStop is true) audio sources. if stopImmediately == false, allows FMod fadeout
        /// </summary>
        public static void StopAllAudio(bool stopImmediately = false, bool forceStop = false)
        {
            _instance.Init();
            AudioInstance[] allAIs = AudioManager._instance.GetAllAudioInstancesSafe();

            foreach (AudioInstance ai in allAIs)
            {
                if (ai.isPersistent == true && forceStop == false) continue;
                ai.Stop(stopImmediately);
            }
        }
    }
}

