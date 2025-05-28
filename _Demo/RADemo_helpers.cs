using Codice.Client.BaseCommands;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RaytracedAudio;
using AudioSettings = RaytracedAudio.AudioSettings;

namespace RaytracedAudio_demo
{
    public class RADemo_helpers : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                AudioSettings.SetAudioPaused(!AudioSettings._isAudioPaused);
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                AudioSettings.StopAllAudio();
            }
        }
    }
}
