using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using zombVoxels;

public static class AudioTracing
{
    private static NativeArray<RaycastCommand> rayCommands;
    private static NativeArray<RaycastHit> rayHits;
    private static NativeArray<Vector3> rayDirs;

    internal static void Allocate()
    {
        //rayCommands = new NativeArray<>
    }

    internal static void Dispose()
    {

    }

    private struct PrepareRays_job : IJob
    {
        public void Execute()
        {

        }
    }
}
