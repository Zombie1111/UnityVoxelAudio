using VoxelAudio;
using UnityEngine;

namespace VoxelAudio_demo
{
    public class RADemo_simplestPlayer : MonoBehaviour
    {
        [SerializeField] private AudioReference audioRef = new();

        private void Start()
        {
            audioRef.Play(transform);
        }
    }
}