using Codice.Client.BaseCommands;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
            AudioSettings.StopAudio();
        }
    }
}
