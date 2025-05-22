using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RaytracedAudio
{
    public class AudioZones
    {
        internal class ZoneInput
        {
            internal AudioInstance ai = null;

            internal bool resultIsReady = false;
        }

        internal static ZoneInput CreateZoneInput(AudioInstance ai)
        {
            return new()
            {
                ai = ai,
                resultIsReady = true,//Temp for now
            };
        }

        internal static void RemoveZoneInput(ZoneInput zoneInput)
        {

        }
    }
}
