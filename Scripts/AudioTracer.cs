using RaytracedAudio;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

internal class AudioTracer
{
    internal class TraceInput
    {
        internal AudioInstance ai = null;

        internal bool resultIsReady = false;
        internal Vector3 resDirection;
        internal float resDistance;

        internal Surface resSurface;
        internal float resRoomSize;
    }

    internal static TraceInput CreateTraceInput(AudioInstance ai)
    {
        return new()
        {
            ai = ai,
            resultIsReady = true,//Temp for now
        };
    }

    internal static void RemoveTraceInput(TraceInput traceInput)
    {

    }

    [System.Serializable]
    internal struct Surface
    {
        public float reflectness;
        public float metallicness;
        public float brightness;
        public float tail;

        [Tooltip("How affected the audio is by going through a surface (This should be 0.0f for all surfaces except a few like doors)")]
        public float passthrough;
    }
}
