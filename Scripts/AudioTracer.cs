using RaytracedAudio;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.SceneManagement;
using zombVoxels;
using AudioSettings = RaytracedAudio.AudioSettings;
using Unity.Mathematics;

#if UNITY_EDITOR
using UnityEditor;
#endif

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

    internal static void DestroyTraceInput(TraceInput traceInput)
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

    #region Main
    private static bool isInitialized = false;

    internal static void Init()
    {
        if (isInitialized == true) return;

        SceneManager.sceneLoaded += OnSceneLoaded;
        SetVoxGlobalHandler(VoxGlobalHandler.TryGetValidGlobalHandler(false));
        isInitialized = true;
    }

    internal static void Destroy()
    {
        if (isInitialized == false) return;
        isInitialized = false;
        voxHandler = null;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SetVoxGlobalHandler(null);
    }

    [System.NonSerialized] private static VoxGlobalHandler voxHandler;

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (voxHandler != null) return;
        SetVoxGlobalHandler(VoxGlobalHandler.TryGetValidGlobalHandler(false));
    }

    /// <summary>
    /// Sets what globalVoxelHandler to use, pass null to clear 
    /// </summary>
    public static void SetVoxGlobalHandler(VoxGlobalHandler newHandler)
    {
        if (voxHandler == newHandler)
        {
            if (voxHandler == null) OnClearVoxelSystem();
            return;
        }

        if (voxHandler != null)
        {
            voxHandler.OnGlobalReadAccessStart -= OnGlobalReadAccessStart;
            voxHandler.OnGlobalReadAccessStop -= OnGlobalReadAccessStop;
            voxHandler.OnClearVoxelSystem -= OnClearVoxelSystem;
            voxHandler.OnSetupVoxelSystem -= OnSetupVoxelSystem;
            if (voxHandler.voxRuntimeIsValid == true) OnClearVoxelSystem();//Not been cleared, so call manually
        }

        voxHandler = newHandler;
        if (voxHandler == null) return;

        voxHandler.OnGlobalReadAccessStart += OnGlobalReadAccessStart;
        voxHandler.OnGlobalReadAccessStop += OnGlobalReadAccessStop;
        voxHandler.OnClearVoxelSystem += OnClearVoxelSystem;
        voxHandler.OnSetupVoxelSystem += OnSetupVoxelSystem;
        if (voxHandler.voxRuntimeIsValid == true) OnSetupVoxelSystem();//Already setup so call manually
    }


    private static bool voxelSystemIsValid = false;

    private static unsafe void OnClearVoxelSystem()
    {
        if (voxelSystemIsValid == false) return;
        voxelSystemIsValid = false;

        OnGlobalReadAccessStop();
        flipA.Dispose(); flipA = null;
        flipB.Dispose(); flipB = null;
        UnsafeUtility.Free(o_job.queueVoxI, Allocator.Persistent);
    }

    private static unsafe void OnSetupVoxelSystem()
    {
        if (voxelSystemIsValid == true) return;

        flipA = new(voxHandler._voxWorldReadonly.vCountXYZ);
        flipB = new(voxHandler._voxWorldReadonly.vCountXYZ);
        readFlip = GetNextFlip();
        writeFlip = GetNextFlip();

        o_job = new()
        {
            queueVoxI = (int*)UnsafeUtility.Malloc(_searchQueueSize * UnsafeUtility.SizeOf<int>(), UnsafeUtility.AlignOf<int>(), Allocator.Persistent),
            voxsType = voxHandler._voxGridReadonly,
            voxWorld = voxHandler._voxWorldNativeReadonly,
        };

        voxelSystemIsValid = true;
    }

    #endregion Main

    #region Compute voxels

    private const int _searchQueueSize = 524288;
    private static ComputeOcclusion_job o_job;
    private static JobHandle o_handle;
    private static bool o_jobIsActive = false;

    private static FlipFlop flipA = null;
    private static FlipFlop flipB = null;
    private static bool flipped = false;

    private static FlipFlop GetNextFlip()
    {
        flipped = !flipped;
        return flipped == false ? flipA : flipB;
    }

    private unsafe class FlipFlop
    {
        internal readonly ushort* voxsDis;
        internal readonly int* voxsDirectI;
        internal Vector3 camPos;

        internal FlipFlop(int voxCountXYZ)
        {
            int allocSize = voxCountXYZ * UnsafeUtility.SizeOf<ushort>();
            voxsDis = (ushort*)UnsafeUtility.Malloc(allocSize, UnsafeUtility.AlignOf<ushort>(), Allocator.Persistent);
            UnsafeUtility.MemSet(voxsDis, 0xFF, allocSize);

            allocSize = voxCountXYZ * UnsafeUtility.SizeOf<int>();
            voxsDirectI = (int*)UnsafeUtility.Malloc(allocSize, UnsafeUtility.AlignOf<int>(), Allocator.Persistent);
            UnsafeUtility.MemSet(voxsDirectI, 0xFF, allocSize);
        }

        internal void Dispose()
        {
            UnsafeUtility.Free(voxsDis, Allocator.Persistent);
            UnsafeUtility.Free(voxsDirectI, Allocator.Persistent);
        }
    }

    private static FlipFlop readFlip = null;
    private static FlipFlop writeFlip = null;

    private static unsafe void OnGlobalReadAccessStart()
    {
        if (o_jobIsActive == true || voxelSystemIsValid == false) return;

        //Get empty pos
        if (VoxHelpFunc.GetClosestVisibleAirVoxel(AudioManager.camPos, voxHandler._voxWorldReadonly, o_job.voxsType, out int voxI, out Vector3 voxPos,
            AudioSettings._voxSnapDistance, AudioSettings._mask, 2) == false)
        {
            Debug.LogError("Unable to find valid audio occlusion start voxel!");
            return;
        }

        writeFlip.camPos = voxPos;
        o_job.voxsDis = writeFlip.voxsDis;
        o_job.voxsDirectI = writeFlip.voxsDirectI;
        o_job.camVoxI = voxI;
        o_job.maxHearRadiusVox = (ushort)AudioSettings._voxComputeDistanceVox;
        o_job.indirectExtraDistance = AudioSettings._indirectExtraVoxDistance;

        o_jobIsActive = true;
        o_handle = o_job.Schedule();
        //VoxHelpFunc.Debug_toggleTimer();
        //OnGlobalReadAccessStop();
        //VoxHelpFunc.Debug_toggleTimer();
    }

    private static void OnGlobalReadAccessStop()
    {
        if (o_jobIsActive == false) return;

        readFlip = writeFlip;
        writeFlip = GetNextFlip();//We wanna flip at stop so other stuff can safety get read at OnGlobalReadAccessStart without worrying about execution order

        o_handle.Complete();
        o_jobIsActive = false;
    }

    //internal const float maxHearRadiusMeter = 35.0f;
    //private const ushort maxHearRadiusVox = 350;//maxHearRadiusVox = maxHearRadiusMeter * 5 / VoxGlobalSettings.voxelSizeWorld (350/35m takes 9ms)
    private const int voxMargin = 1;

    [BurstCompile]
    private unsafe struct ComputeOcclusion_job : IJob
    {
        [NativeDisableUnsafePtrRestriction] internal byte* voxsType;
        internal NativeReference<VoxWorld>.ReadOnly voxWorld;

        [NativeDisableUnsafePtrRestriction] internal ushort* voxsDis;
        [NativeDisableUnsafePtrRestriction] internal int* voxsDirectI;
        [NativeDisableUnsafePtrRestriction] internal int* queueVoxI;

        internal int camVoxI;
        internal ushort maxHearRadiusVox;
        internal ushort indirectExtraDistance;

        public void Execute()
        {
            VoxWorld vWorld = voxWorld.Value;
            UnsafeUtility.MemSet(voxsDis, 0xFF, vWorld.vCountXYZ * UnsafeUtility.SizeOf<ushort>());
            UnsafeUtility.MemSet(voxsDirectI, 0xFF, vWorld.vCountXYZ * UnsafeUtility.SizeOf<int>());//int 0xFF == -1
            int* voxDirs = vWorld.GetVoxAxisDirsUnsafe(out ushort* voxDirsDis);
            int queueHead = 0;
            int queueTail = 0;
            int queueCount = 0;

            int vCountX = vWorld.vCountX;
            int vCountY = vWorld.vCountY;
            int vCountYZ = vWorld.vCountYZ;
            int vCountZ = vWorld.vCountZ;


            int camX = camVoxI / vCountYZ;
            int remI = camVoxI % vCountYZ;
            int camY = remI / vCountZ;
            int camZ = remI % vCountZ;

            voxsDis[camVoxI] = 0;
            queueVoxI[queueTail] = camVoxI;
            queueTail = (queueTail + 1) % _searchQueueSize;
            queueCount++;

#if UNITY_EDITOR
            bool hasLogged = false;
#endif

            while (queueCount > 0)//Can probably be optimized further by using a bucket/priority order
            {
                //Dequeue
                int voxI = queueVoxI[queueHead];
                queueHead = (queueHead + 1) % _searchQueueSize;
                queueCount--;
                ushort activeDis = voxsDis[voxI];

                //Per axis voxel count and is voxI valid?
                int ix = voxI / vCountYZ;
                if (ix + voxMargin >= vCountX || ix < voxMargin) continue;

                remI = voxI % vCountYZ;
                int iy = remI / vCountZ;
                if (iy + voxMargin >= vCountY || iy < voxMargin) continue;

                int iz = remI % vCountZ;
                if (iz + voxMargin >= vCountZ || iz < voxMargin) continue;

                //Voxel distance
                int directVoxI = voxsDirectI[voxI];

                if (directVoxI < 0)
                {
                    ix -= camX; if (ix < 0) ix = -ix;
                    iy -= camY; if (iy < 0) iy = -iy;
                    iz -= camZ; if (iz < 0) iz = -iz;

                    if (ix < iy)
                    {
                        (iy, ix) = (ix, iy);
                    }
                    if (iy < iz)
                    {
                        (iz, iy) = (iy, iz);
                    }
                    if (ix < iy)
                    {
                        (iy, ix) = (ix, iy);
                    }

                    ushort airDis = (ushort)(5 * ix + 2 * (iy + iz));

                    if (airDis - activeDis < 0)
                    {
                        //Is indirect
                        activeDis += indirectExtraDistance;
                        directVoxI = voxI;
                        //activeDis = 10000;
                    }
                }

                //Spread
                for (int i = 0; i < 26; i++)
                {
                    int nextVoxI = voxI + voxDirs[i];
                    ushort nextDis = (ushort)(activeDis + voxDirsDis[i]);
                    //if (nextVoxI < 0 || nextVoxI >= vWorld.vCountXYZ) continue;
                    if (voxsDis[nextVoxI] <= nextDis) continue;//Old value is better

                    voxsDirectI[nextVoxI] = directVoxI;
                    voxsDis[nextVoxI] = nextDis;
                    if (nextDis > maxHearRadiusVox) continue;

                    byte nextVoxType = voxsType[nextVoxI];
                    if (nextVoxType > VoxGlobalSettings.solidTypeStart)
                    {
                        continue;
                    }

#if UNITY_EDITOR
                    if (queueCount == _searchQueueSize)//Off by 1 but who cares, only for logging, wont throw if full
                    {
                        if (hasLogged == false) Debug.LogError("Search queue is full!");
                        hasLogged = true;
                        break;
                    }
#endif
                    //Enqueue
                    queueVoxI[queueTail] = nextVoxI;
                    queueTail = (queueTail + 1) % _searchQueueSize;
                    queueCount++;
                }
            }

            UnsafeUtility.Free(voxDirs, Allocator.Temp);
            UnsafeUtility.Free(voxDirsDis, Allocator.Temp);
        }
    }
    #endregion Compute voxels

    private const int sampleStepSize = 6;
    private const int sampleStepCount = 1;

    public static unsafe float SampleOcclusionAtPos(Vector3 pos, out Vector3 resultDirection)
    {
        //Get source vox
        int voxI = VoxHelpFunc.PosToWVoxIndex_snapped(pos, voxHandler._voxWorldReadonly, out _, 1);
        float vDis = readFlip.voxsDis[voxI];
        int[] vOffsets = voxHandler._voxWorldReadonly.GetVoxDirs();

        if (vDis > AudioSettings._voxComputeDistanceVox)
        {
            for (int i = 1; i < vOffsets.Length; i++)
            {
                int vI = voxI + vOffsets[i];
                float vD = readFlip.voxsDis[vI];
                if (vD > AudioSettings._voxComputeDistanceVox) continue;

                vDis = vD;
                voxI = vI;
                break;
            }

            //Unhearable?
            if (vDis > AudioSettings._voxComputeDistanceVox)
            {
                resultDirection = (pos - AudioManager.camPos).normalized;
                return AudioSettings._voxComputeDistanceMeter;
            }
        }

        //Direct?
        int vDirectI = readFlip.voxsDirectI[voxI];
        //if (vDirectI < 0)
        //{
        //    resultDirection = (pos - AudioManager.camPos).normalized;
        //    return (vDis / 5) * VoxGlobalSettings.voxelSizeWorld;;
        //}

        //It is indirect, sample multiple points for smoother direction
        resultDirection = ((vDirectI < 0 ? pos : VoxHelpFunc.WVoxIndexToPos_snapped(vDirectI, voxHandler._voxWorldReadonly))
            - AudioManager.camPos).normalized * (100.0f / vDis);
        int maxStepValue = sampleStepCount * sampleStepSize;

        for (int step = sampleStepSize; step <= maxStepValue; step += sampleStepCount)
        {
            for (int i = 1; i < vOffsets.Length; i++)
            {
                int vI = voxI + (vOffsets[i] * step);
                if (VoxHelpBurst.IsWVoxIndexValidFast(vI, voxHandler._voxWorldReadonly) == false) continue;

                float vD = readFlip.voxsDis[vI];
                if (math.abs(vD - vDis) > step * 9  ) continue;

                int vDI = readFlip.voxsDirectI[vI];
                resultDirection += ((vDI < 0 ? pos : VoxHelpFunc.WVoxIndexToPos_snapped(vDI, voxHandler._voxWorldReadonly))
                    - AudioManager.camPos).normalized * (100.0f / vD);
            }
        }

        resultDirection.Normalize();
        return (vDis / 5) * VoxGlobalSettings.voxelSizeWorld;
    }

    #region Debug

#if UNITY_EDITOR
    private static readonly List<VoxHelpBurst.CustomVoxelDataB> voxsToDraw = new();
    private static Vector3 sceneCamPos;

    internal static void DebugDrawGizmos()
    {
        if (voxelSystemIsValid == false) return;
        if (AudioSettings._debugMode == DebugMode.drawAudioOcclusion && Application.isPlaying == true)
        {
            if (voxHandler._globalHasReadAccess == true)
            {
                Debug_updateVisualVoxels();
            }

            Vector3 voxSize = Vector3.one * VoxGlobalSettings.voxelSizeWorld;

            foreach (var draw in voxsToDraw)
            {
                Gizmos.color = draw.col;
                Gizmos.DrawCube(draw.pos, voxSize);
            }
        }
    }

    private static unsafe void Debug_updateVisualVoxels()
    {
        voxsToDraw.Clear();
        if (Application.isPlaying == false)
        {
            Debug.LogError("Cannot update visual voxels in editmode");
            return;
        }

        float maxDis = VoxGlobalSettings.voxelSizeWorld * 25.0f;//This is how far voxels will be rendered
        var view = SceneView.currentDrawingSceneView;
        if (view == null) return;
        Vector3 sceneCam = view.camera.ViewportToWorldPoint(view.cameraViewport.center);
        sceneCamPos = sceneCam;

        NativeList<VoxHelpBurst.CustomVoxelDataB> solidVoxsI = new(1080, Allocator.Temp);
        VoxHelpBurst.GetSolidVoxColorsInRadius(ref sceneCam, maxDis, AudioSettings._voxComputeDistanceVox, voxHandler._voxGridReadonly
            , readFlip.voxsDis, readFlip.voxsDirectI, voxHandler._voxWorldReadonly, ref solidVoxsI);

        foreach (var vData in solidVoxsI)
        {
            voxsToDraw.Add(vData);
        }

        solidVoxsI.Dispose();
    }
#endif

    #endregion Debug
}
