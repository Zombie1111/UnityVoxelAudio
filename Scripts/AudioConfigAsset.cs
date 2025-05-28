using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RaytracedAudio
{
    [CreateAssetMenu(menuName = "Audio/Audio Config Asset")]
    public class AudioConfigAsset : ScriptableObject
    {
        [SerializeField] internal AudioEffects audioEffects = AudioEffects.all;
        [Tooltip("If true audio wont be destroyed on scene load")]
        [SerializeField] internal bool persistent = false;
    }
}
