using FMODUnity;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using zombVoxels;

namespace VoxelAudio
{
    internal static class AudioBasics
    {
        internal static NativeArray<AudioInstance.BasicData> aisBasicData_native;
        private static NativeReference<VoxWorld> voxWorldNative;
        private static bool isInitilized = false;

        internal static void Allocate()
        {
            if (isInitilized == true) return;

            aisBasicData_native = new NativeArray<AudioInstance.BasicData>(AudioSettings._maxActiveVoices, Allocator.Persistent);
            voxWorldNative = new NativeReference<VoxWorld>(Allocator.Persistent)
            {
                Value = new()
            };

            bd_job = new()
            {
                aisBasicData = aisBasicData_native,
                voxWorldReadonly = voxWorldNative.AsReadOnly(),
            };

            isInitilized = true;
        }

        internal static void Dispose()
        {
            if (isInitilized == false) return;
            isInitilized = false;

            EndBasicsCompute();
            voxWorldNative.Dispose();
            aisBasicData_native.Dispose();
        }

        internal static unsafe void StartBasicsCompute(float deltaTime, int voiceCount)
        {
            if (bd_jobIsActive == true) return;
            bd_jobIsActive = true;

            //Prepare
            voxWorldNative.Value = AudioOcclusion.voxHandler == null || AudioOcclusion.hasComputedOcclusion == false
                || AudioSettings._globalAudioEffects.HasOcclusion() == false ? new() : AudioOcclusion.voxHandler._voxWorldReadonly;

            if (AudioOcclusion.readFlip != null)
            {
                bd_job.voxsDis = AudioOcclusion.readFlip.voxsDis;
                bd_job.voxsDirectI = AudioOcclusion.readFlip.voxsDirectI;
                bd_job.camExtraDis = AudioOcclusion.readFlip.camExtraDis;
            }

            bd_job.occlusionLerpDelta = AudioSettings._occlusionLerpSpeed * deltaTime;
            bd_job.currentUnderwaterFreq = AudioManager.currentUnderwaterFreq;
            bd_job.voiceCount = voiceCount;
            bd_job.camPos = AudioManager.camPos;
            bd_job.camPosRead = AudioOcclusion._lastCamPos;
            bd_job.voxComputeDistanceMeter = AudioSettings._voxComputeDistanceMeter;
            bd_job.voxComputeDistanceVox = (ushort)AudioSettings._voxComputeDistanceVox;
            bd_job.occludedFilterStart = AudioSettings._occludedFilterStart;
            bd_job.occludedFilterEnd = AudioSettings._occludedFilterEnd;
            bd_job.fullyOccludedLowPassFreq = AudioSettings._fullyOccludedLowPassFreq;
            bd_job.minMaxDistanceFactor = AudioSettings._minMaxDistanceFactor;

            //Run
            bd_handle = bd_job.Schedule();
        }

        internal static void EndBasicsCompute()
        {
            if (bd_jobIsActive == false) return;

            bd_handle.Complete();
            bd_jobIsActive = false;
        }

        private static ComputeBasics_job bd_job;
        private static JobHandle bd_handle;
        private static bool bd_jobIsActive = false;

        [BurstCompile]
        private unsafe struct ComputeBasics_job : IJob
        {
            private const int sampleStepSize = 6;
            private const int sampleStepCount = 1;

            [NativeDisableUnsafePtrRestriction] internal ushort* voxsDis;
            [NativeDisableUnsafePtrRestriction] internal int* voxsDirectI;
            internal NativeArray<AudioInstance.BasicData> aisBasicData;
            internal NativeReference<VoxWorld>.ReadOnly voxWorldReadonly;
            internal float occlusionLerpDelta;

            internal float camExtraDis;
            internal float currentUnderwaterFreq;
            internal int voiceCount;
            internal Vector3 camPos;
            internal Vector3 camPosRead;
            internal float voxComputeDistanceMeter;
            internal ushort voxComputeDistanceVox;
            internal float occludedFilterStart;
            internal float occludedFilterEnd;
            internal float fullyOccludedLowPassFreq;
            internal float minMaxDistanceFactor;

            public void Execute()
            {
                VoxWorld vWorld = voxWorldReadonly.Value;
                int* vOffsets = vWorld.GetVoxDirsUnsafe();

                for (int i = 0; i < voiceCount; i++)
                {
                    AudioInstance.BasicData bd = aisBasicData[i];
                    if (bd.clip.isValid() == false) continue;

                    //Update effects
                    if (bd.lowpassFilter.hasHandle() == true)
                    {
                        float dis = SampleOcclusionAtPos(bd.pos, vOffsets, vWorld, out Vector3 dir, out float ocAmount);
                        ocAmount = 1.0f - ocAmount;

                        if (bd.distance < 0.0f)
                        {
                            bd.distance = dis;
                            bd.direction = dir;
                            bd.occlusion = ocAmount;
                        }
                        else
                        {
                            bd.occlusion = Mathf.Lerp(bd.occlusion, ocAmount, occlusionLerpDelta);
                            bd.distance = Mathf.Lerp(bd.distance, dis, occlusionLerpDelta);//Distance calc is wrong somewhere
                            //bd.direction = (bd.pos - camPos).normalized;
                            bd.direction = Vector3.Slerp(bd.direction, dir, occlusionLerpDelta / (1.0f + (dis / voxComputeDistanceMeter)));
                        }

                        bd.lowpassFilter.setParameterFloat((int)FMOD.DSP_LOWPASS_SIMPLE.CUTOFF, Mathf.Min(currentUnderwaterFreq,
                             Mathf.Lerp(fullyOccludedLowPassFreq, AudioSettings._noLowPassFreq, bd.occlusion * bd.occlusion)));
                    }
                    else
                    {
                        bd.direction = (bd.pos - camPos).normalized;
                        bd.distance = (bd.pos - camPos).magnitude;
                        bd.occlusion = 1.0f;
                    }

                    //Apply 3D
                    float fmodDis = bd.distance / minMaxDistanceFactor;
                    bd.clip.set3DAttributes(new()
                    {
                        position = (camPos + (bd.direction * fmodDis)).ToFMODVector(),
                        forward = new() { x = 0, y = 0, z = 1 },
                        up = new() { x = 0, y = 1, z = 0 },
                        velocity = new() { x = 0, y = 0, z = 0 },
                    });

                    aisBasicData[i] = bd;
                }

                UnsafeUtility.Free(vOffsets, Allocator.Temp);
            }

            private float SampleOcclusionAtPos(Vector3 pos, int* vOffsets, in VoxWorld vWorld, out Vector3 resultDirection, out float occludedAmount)
            {
                if (vWorld.vCountXYZ == 0)
                {
                    resultDirection = (pos - camPos).normalized;
                    occludedAmount = 0.0f;
                    return Vector3.Distance(pos, camPos);
                }

                //Get source vox
                int voxI = VoxHelpFunc.PosToWVoxIndex_snapped(pos, vWorld, out Vector3 snappedPos, 2);
                ushort vDis = voxsDis[voxI];

                if ((camExtraDis > 0.0f && snappedPos != pos) || vDis < 1)
                {
                    //Both sample pos and cam is outside grid or we are inside audio source
                    resultDirection = (pos - camPosRead).normalized;
                    occludedAmount = 0.0f;
                    return Vector3.Distance(pos, camPosRead);
                }

                if (vDis > voxComputeDistanceVox)
                {
                    for (int i = 1; i < 15; i++)
                    {
                        int vI = voxI + vOffsets[i];
                        ushort vD = voxsDis[vI];
                        if (vD > voxComputeDistanceVox) continue;

                        vDis = vD;
                        voxI = vI;
                        break;
                    }

                    //Unhearable?
                    if (vDis > voxComputeDistanceVox)
                    {
                        resultDirection = (pos - camPos).normalized;
                        occludedAmount = 0.0f;
                        return voxComputeDistanceMeter;
                    }
                }

                //Direct?
                int vDirectI = voxsDirectI[voxI];

                //Sample multiple points for smoother direction
                resultDirection = ((vDirectI < 0 ? pos : VoxHelpFunc.WVoxIndexToPos_snapped(vDirectI, vWorld))
                    - camPos).normalized * (100.0f / vDis);
                int maxStepValue = sampleStepCount * sampleStepSize;

                for (int step = sampleStepSize; step <= maxStepValue; step += sampleStepCount)
                {
                    for (int i = 1; i < 15; i++)
                    {
                        int vI = voxI + (vOffsets[i] * step);
                        if (VoxHelpBurst.IsWVoxIndexValidFast(vI, vWorld) == false) continue;

                        ushort vD = voxsDis[vI];
                        if (vD < 1 || math.abs(vD - vDis) > step * 9) continue;

                        int vDI = voxsDirectI[vI];
                        resultDirection += ((vDI < 0 ? pos : VoxHelpFunc.WVoxIndexToPos_snapped(vDI, vWorld))
                            - camPos).normalized * (100.0f / vD);
                    }
                }

                resultDirection.Normalize();
                float airDis = Vector3.Distance(pos, camPos);
                float oDis = ((vDis / 5.0f) * VoxGlobalSettings.voxelSizeWorld) + Vector3.Distance(snappedPos, pos) + camExtraDis;
                occludedAmount = Mathf.Clamp01((oDis - (airDis + occludedFilterStart)) / occludedFilterEnd);

                return oDis;
            }
        }
    }

}

