﻿using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.SceneManagement;
using zombVoxels;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VoxelAudio
{
    public class AudioOcclusion
    {
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

        [System.NonSerialized] internal static VoxGlobalHandler voxHandler;

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
        internal static bool hasComputedOcclusion = false;

        /// <summary>
        /// Returns the listener position at last occlusion update, returns current listener position if no occlusion has been computed
        /// </summary>
        public static Vector3 _lastCamPos => hasComputedOcclusion == true ? readFlip.camPos : AudioManager.camPos;

        private static unsafe void OnClearVoxelSystem()
        {
            if (voxelSystemIsValid == false) return;
            voxelSystemIsValid = false;
            hasComputedOcclusion = false;

            OnGlobalReadAccessStop();
            TryCompleteOcclusionJob(true);
            AudioBasics.EndBasicsCompute();
            flipA.Dispose(); flipA = null;
            flipB.Dispose(); flipB = null;
            o_job.isCompleted.Dispose();
            o_job.voxWorld.Dispose();
            UnsafeUtility.Free(o_job.queueVoxI, Allocator.Persistent);
            UnsafeUtility.Free(c_job.voxsTypeDestination, Allocator.Persistent);
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
                voxWorld = new NativeReference<VoxWorld>(voxHandler._voxWorldReadonly, Allocator.Persistent),
                isCompleted = new NativeReference<bool>(Allocator.Persistent)
            };

            c_job = new()
            {
                voxsTypeSource = voxHandler._voxGridReadonly,
                voxsTypeDestination = (byte*)UnsafeUtility.Malloc(voxHandler._voxWorldReadonly.vCountXYZ * UnsafeUtility.SizeOf<byte>(), UnsafeUtility.AlignOf<byte>(), Allocator.Persistent),
                voxWorld = voxHandler._voxWorldNativeReadonly,
            };

            hasComputedOcclusion = false;
            voxelSystemIsValid = true;
        }

        #endregion Main

        #region Compute voxels

        private const int _searchQueueSize = 786432;
        private static ComputeOcclusion_job o_job;
        private static CopyVoxsType_job c_job;
        private static JobHandle o_handle;
        private static JobHandle c_handle;
        private static bool o_jobIsActive = false;
        private static bool c_jobIsActive = false;

        private static FlipFlop flipA = null;
        private static FlipFlop flipB = null;
        private static bool flipped = false;

        private static FlipFlop GetNextFlip()
        {
            AudioBasics.EndBasicsCompute();
            flipped = !flipped;
            return flipped == false ? flipA : flipB;
        }

        internal unsafe class FlipFlop
        {
            internal readonly ushort* voxsDis;
            internal readonly int* voxsDirectI;
            /// <summary>
            /// Cam pos (snapped to voxel at compute time)
            /// </summary>
            internal Vector3 camPos;
            /// <summary>
            /// If > 0.0f, cam is outside global voxel grid and is distance to global voxel grid
            /// </summary>
            internal float camExtraDis;

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

        internal static FlipFlop readFlip = null;
        private static FlipFlop writeFlip = null;

        private static unsafe void OnGlobalReadAccessStart()
        {
            if (TryCompleteOcclusionJob(false) == false) return;
            if (o_jobIsActive == true || voxelSystemIsValid == false) return;
            if (AudioSettings._globalAudioEffects.HasOcclusion() == false)
            {
                hasComputedOcclusion = false;
                return;
            }

            //Get empty pos
            if (VoxHelpFunc.GetClosestVisibleAirVoxel(AudioManager.camPos, voxHandler._voxWorldReadonly, o_job.voxsType, out int voxI, out Vector3 voxPos,
                AudioSettings._voxSnapDistance, AudioSettings._mask, 2) == false)
            {
                Debug.LogError("Unable to find valid audio occlusion start voxel!");
                return;
            }

            writeFlip.camPos = voxPos;
            writeFlip.camExtraDis = Vector3.Distance(voxPos, AudioManager.camPos) - VoxGlobalSettings.voxelSizeWorld;
            o_job.voxsDis = writeFlip.voxsDis;
            o_job.voxsDirectI = writeFlip.voxsDirectI;
            o_job.camVoxI = voxI;
            o_job.maxHearRadiusVox = (ushort)AudioSettings._voxComputeDistanceVox;
            o_job.indirectExtraDistanceVox = AudioSettings._indirectExtraDistanceVox;
            o_job.isCompleted.Value = false;

            o_jobIsActive = true;
            c_jobIsActive = true;
            c_handle = c_job.Schedule();
            o_handle = o_job.Schedule(c_handle);
        }

        private static bool TryCompleteOcclusionJob(bool forceComplete)
        {
            if (c_jobIsActive == true) Debug.Log("Expecting voxsType copy job to always complete first!");
            if (o_jobIsActive == false) return true;
            if (forceComplete == false && o_job.isCompleted.Value == false) return false;

            readFlip = writeFlip;
            writeFlip = GetNextFlip();//We wanna flip at stop so other stuff can safety get read at OnGlobalReadAccessStart without worrying about execution order

            o_handle.Complete();
            o_jobIsActive = false;
            hasComputedOcclusion = true;
            return true;
        }

        private static void OnGlobalReadAccessStop()
        {
            if (c_jobIsActive == false) return;

            c_handle.Complete();
            c_jobIsActive = false;

        }

        private const int voxMargin = 1;
        private const byte passthroughVoxType = VoxGlobalSettings.doorVoxType;//Useful for doors, allows audio to go through but with a penalty of passthroughExtraVoxDis. Put 0 if unused
        private const byte passthroughVoxType2 = VoxGlobalSettings.rbVoxType;//Same as above, I use this for some rigidbodies
        private const ushort passthroughExtraVoxDis = 50;
        private const ushort passthroughExtraVoxDis2 = 0;

        [BurstCompile]
        private unsafe struct CopyVoxsType_job : IJob
        {
            [NativeDisableUnsafePtrRestriction] internal byte* voxsTypeSource;
            [NativeDisableUnsafePtrRestriction] internal byte* voxsTypeDestination;
            internal NativeReference<VoxWorld>.ReadOnly voxWorld;

            public void Execute()
            {
                UnsafeUtility.MemCpy(voxsTypeDestination, voxsTypeSource, voxWorld.Value.vCountXYZ * UnsafeUtility.SizeOf<byte>());
            }
        }

        [BurstCompile]
        private unsafe struct ComputeOcclusion_job : IJob
        {
            [NativeDisableUnsafePtrRestriction] internal byte* voxsType;
            internal NativeReference<VoxWorld> voxWorld;
            [NativeDisableContainerSafetyRestriction] internal NativeReference<bool> isCompleted;

            [NativeDisableUnsafePtrRestriction] internal ushort* voxsDis;
            [NativeDisableUnsafePtrRestriction] internal int* voxsDirectI;
            [NativeDisableUnsafePtrRestriction] internal int* queueVoxI;

            internal int camVoxI;
            internal ushort maxHearRadiusVox;
            internal ushort indirectExtraDistanceVox;

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
                int totLoopCount = 0;

#if UNITY_EDITOR
                bool hasLogged = false;
#endif

                while (queueCount > 0)//Can probably be optimized further by using a bucket Dial’s/priority order
                {
                    totLoopCount++;

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
                            activeDis += indirectExtraDistanceVox;
                            directVoxI = voxI;
                            //activeDis = 10000;
                        }
                    }

                    //Spread
                    for (int i = 0; i < 26; i++)
                    {
                        int nextVoxI = voxI + voxDirs[i];
                        ushort nextDis = (ushort)(activeDis + voxDirsDis[i]);
                        //if (voxsDis[nextVoxI] < 65000) continue;//Old value is better, why does comparing against constant value give similar result as nextDis, comparing with constant should be wrong
                        //if (nextVoxI < 0 || nextVoxI >= vWorld.vCountXYZ) continue;
                        if (voxsDis[nextVoxI] <= nextDis) continue;//Old value is better

                        voxsDirectI[nextVoxI] = directVoxI;

                        byte nextVoxType = voxsType[nextVoxI];

                        if (nextVoxType > 0)
                        {
                            if (nextVoxType > VoxGlobalSettings.solidTypeStart)
                            {
                                                        voxsDis[nextVoxI] = nextDis;
                                continue;
                            }
                            if (nextVoxType == passthroughVoxType) nextDis += passthroughExtraVoxDis;
                            else if (nextVoxType == passthroughVoxType2) nextDis += passthroughExtraVoxDis2;
                            //else if (nextVoxType > VoxGlobalSettings.solidTypeStart && nextVoxType != ) continue;
                        }

                        voxsDis[nextVoxI] = nextDis;
                        if (nextDis > maxHearRadiusVox) continue;

#if UNITY_EDITOR
                        if (queueCount == _searchQueueSize)//Off by 1 but who cares, only for logging, wont throw if full
                        {
                            if (hasLogged == false) Debug.LogWarning("Search queue is full, occlusion may be wrong!");//No risk for out of bounds if full, just wrong occlusion
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
                isCompleted.Value = true;//Not 100% safe
            }
        }
        #endregion Compute voxels

        #region Help Methods

        /// <summary>
        /// Returns the occluded distance between pos and listener, isOccluded is < 0 if not occluded
        /// </summary>
        /// <param name="isOccluded">If >= 0 the position is occluded and isDirectlyVisible is the nearest directly visible voxel index</param>
        public static unsafe float SampleOcclusionAtPos(Vector3 pos, out int isOccluded)
        {
            if (hasComputedOcclusion == false)
            {
                isOccluded = -1;
                return Vector3.Distance(AudioManager.camPos, pos);
            }

            int voxI = VoxHelpFunc.PosToWVoxIndex_snapped(pos, voxHandler._voxWorldReadonly, out Vector3 snappedPos, 2);
            ushort vDis = readFlip.voxsDis[voxI];

            if ((readFlip.camExtraDis > 0.0f && snappedPos != pos) || vDis < 1)
            {
                //Both sample pos and cam is outside grid or we are inside audio source
                isOccluded = -1;
                return Vector3.Distance(pos, readFlip.camPos);
            }

            isOccluded = readFlip.voxsDirectI[voxI];
            return ((vDis / 5.0f) * VoxGlobalSettings.voxelSizeWorld) + Vector3.Distance(snappedPos, pos) + readFlip.camExtraDis;
        }

        #endregion Help Methods

        #region Debug

#if UNITY_EDITOR
        private static readonly List<VoxHelpBurst.CustomVoxelDataB> voxsToDraw = new();
        private static Vector3 sceneCamPos;

        internal static void DebugDrawGizmos()
        {
            if (voxelSystemIsValid == false) return;
            if (AudioSettings._debugMode == DebugMode.drawAudioOcclusion && Application.isPlaying == true && voxHandler != null)
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
            if (Application.isPlaying == false || voxHandler == null)
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

}
