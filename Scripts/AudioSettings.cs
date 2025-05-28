using System.Collections.Generic;
using UnityEngine;


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

        private static bool isSetup = false;

        private void Setup()
        {
            isSetup = true;
            if (defaultAudioConfigAsset == null && Application.isPlaying == true)
            {
                defaultAudioConfigAsset = ScriptableObject.CreateInstance<AudioConfigAsset>();
                Debug.LogError("defaultAudioConfigAsset is not allow to be null! (Creating temp)");
            }

            _defaultAudioConfigAsset = defaultAudioConfigAsset;
            _buses = buses.ToArray();
            _minMaxDistanceFactor = minMaxDistanceFactor;
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
        }

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
            if (isSetup == false) _instance.Init();
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
        [Tooltip("Global factor on how far away spatilized sounds can be heard")]
        [SerializeField] private float minMaxDistanceFactor = 3.0f;
        internal static float _minMaxDistanceFactor = 3.0f;

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
        public static void SetAudioPaused(bool pause, bool forceSet = false)
        {
            if (audioIsPaused == pause) return;
            if (isSetup == false) _instance.Init();
            audioIsPaused = pause;

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
            if (isSetup == false) _instance.Init();
            AudioInstance[] allAIs = AudioManager._instance.GetAllAIsSafe();

            foreach (AudioInstance ai in allAIs)
            {
                if (ai.isPersistent == true && forceStop == false) continue;
                ai.Stop(stopImmediately);
            }
        }
    }
}

