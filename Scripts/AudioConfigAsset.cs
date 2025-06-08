using UnityEngine;

namespace RaytracedAudio
{
    [CreateAssetMenu(menuName = "Audio/Audio Config Asset")]
    public class AudioConfigAsset : ScriptableObject
    {
        [SerializeField] internal AudioEffects audioEffects = AudioEffects.all;
        [Tooltip("If true audio wont be destroyed on scene load")]
        [SerializeField] internal bool persistent = false;
        [Tooltip("Should be true if the EventInstance has a Spatializer, used to know if certain effects can stop being updated if too far away")]
        [SerializeField] internal bool isSpatialized = true;
        [Tooltip("If has reverb and true, will pretend rays from source always hits default surface type")]
        [SerializeField] internal bool reverbIgnoreSelfRays = false;
    }
}
