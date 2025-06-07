using RaytracedAudio;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using zombVoxels;
using AudioSettings = RaytracedAudio.AudioSettings;

public static class AudioReverb
{
    internal static NativeArray<AudioInstance.Data> aisData_native;
    private static NativeArray<RaycastCommand> rayCommands;
    private static NativeArray<RaycastHit> rayHits;
    private static NativeArray<Vector3> rayDirs;

    internal static void Allocate()
    {
        aisData_native = new NativeArray<AudioInstance.Data>(AudioSettings._maxConcurrentAudioSources, Allocator.Persistent);
    }

    internal static void Dispose()
    {
        aisData_native.Dispose();
    }

    private struct PrepareRays_job : IJob
    {
        public void Execute()
        {

        }
    }
}
