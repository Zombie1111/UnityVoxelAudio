using FMODUnity;
using Mono.Cecil.Cil;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using zombVoxels;

namespace RaytracedAudio
{
    public static class AudioBasicsJob
    {
        [BurstCompile]
        private unsafe struct ApplyBasics_job : IJob
        {
            internal NativeArray<AudioInstance.Data> aisData;
            internal float deltaTime;
            internal Vector3 camPos;
            internal float _minMaxDistanceFactor;
            internal float _fullyOccludedLowPassFreq;
            internal float _occlusionLerpSpeed;
            internal float _voxComputeDistanceMeter;
            internal float _voxComputeDistanceVox;
            internal float _occludedFilterDisM;
            internal ushort* voxsDis;
            internal int* voxsDirectI;
            internal NativeReference<VoxWorld>.ReadOnly _voxWorldReadonly;
            internal float camExtraDis;

            public void Execute()
            {
                int aiCount = aisData.Length;

                for (int i = 0; i < aiCount; i++)
                {
                    AudioInstance.Data aiD = aisData[i];
                    if (aiD.clip.isValid() == false) return;

                    if (aiD.lowpassFilter.hasHandle() == true)
                    {
                        float dis = SampleOcclusionAtPos(aiD.pos, out Vector3 dir, out float ocAmount);
                        ocAmount = 1.0f - ocAmount;
                        float ocSpeed = _occlusionLerpSpeed / (1.0f + (dis / _voxComputeDistanceMeter));

                        if (aiD.dis < 0.0f)
                        {
                            aiD.dis = dis;
                            aiD.dir = dir;
                        }
                        else
                        {
                            aiD.dis = Mathf.Lerp(aiD.dis, dis, _occlusionLerpSpeed * deltaTime);
                            aiD.dir = Vector3.Slerp(aiD.dir, dir, _occlusionLerpSpeed * deltaTime);
                        }

                        ////Bounce brightness (0.0 == di0, de100: 0.5 == di50, de50, 1.0, di100, de0)
                        //reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DIFFUSION, Mathf.Lerp(0.0f, 100.0f, traceInput.resSurface.brightness));
                        //reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DENSITY, Mathf.Lerp(100.0f, 0.0f, traceInput.resSurface.brightness));
                        //
                        //reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HIGHCUT, Mathf.Lerp(20.0f, 4000.0f, traceInput.resSurface.metallicness));
                        //
                        //reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.EARLYLATEMIX, Mathf.Lerp(0.0f, 66.0f, traceInput.resSurface.tail * 2.0f));
                        //reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HFDECAYRATIO, Mathf.Lerp(0.0f, 100.0f, (traceInput.resSurface.tail - 0.5f) * 2.0f));
                        //
                        ////float wetL = Mathf.Lerp(-80.0f, 20.0f, traceData.resSurface.reflectness);
                        ////float wetL = Mathf.Lerp(-80.0f, 20.0f, traceData.resSurface.reflectness);
                        //float wetL = 10.377f * Mathf.Log(Mathf.Clamp01(traceInput.resSurface.reflectness)) + 95.029f;
                        //reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DECAYTIME, Mathf.Exp(0.0981f * wetL));
                        ////reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DECAYTIME, 2.0f * Mathf.Exp(0.0906f * (wetL + 80.00001f)));

                        //lowpassFilter.setParameterFloat((int)FMOD.DSP_LOWPASS_SIMPLE.CUTOFF, -2409 * Mathf.Log(traceInput.resSurface.passthrough) - 217.15f);
                        aiD.lowpassFilter.setParameterFloat((int)FMOD.DSP_LOWPASS_SIMPLE.CUTOFF, Mathf.Lerp(_fullyOccludedLowPassFreq, 17000.0f, ocAmount * ocAmount));
                    }
                    else
                    {
                        aiD.dir = (aiD.pos - camPos).normalized;
                        aiD.dis = (aiD.pos - camPos).magnitude;
                    }

                    //Apply 3D
                    aiD.clip.set3DAttributes(new()
                    {
                        position = (camPos + (aiD.dir * (aiD.dis / _minMaxDistanceFactor))).ToFMODVector(),
                        
                    });
                }
            }

            private const int sampleStepSize = 6;
            private const int sampleStepCount = 1;

            private float SampleOcclusionAtPos(Vector3 pos, out Vector3 resultDirection, out float occludedAmount)
            {
                //Get source vox
                var vWorld = _voxWorldReadonly.Value;
                int voxI = VoxHelpFunc.PosToWVoxIndex_snapped(pos, vWorld, out Vector3 snappedPos, 1);
                if (camExtraDis > 0.0f && snappedPos != pos)
                {
                    //Both sample pos and cam is outside grid
                    resultDirection = (pos - camPos).normalized;
                    occludedAmount = 0.0f;
                    return Vector3.Distance(pos, camPos);
                }

                float vDis = voxsDis[voxI];
                int* vOffsets = vWorld.GetVoxDirsUnsafe();

                if (vDis > _voxComputeDistanceVox)
                {
                    for (int i = 1; i < 15; i++)
                    {
                        int vI = voxI + vOffsets[i];
                        float vD = voxsDis[vI];
                        if (vD > _voxComputeDistanceVox) continue;

                        vDis = vD;
                        voxI = vI;
                        break;
                    }

                    //Unhearable?
                    if (vDis > _voxComputeDistanceVox)
                    {
                        resultDirection = (pos - camPos).normalized;
                        occludedAmount = 0.0f;
                        return _voxComputeDistanceMeter;
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

                        float vD = voxsDis[vI];
                        if (math.abs(vD - vDis) > step * 9) continue;

                        int vDI = voxsDirectI[vI];
                        resultDirection += ((vDI < 0 ? pos : VoxHelpFunc.WVoxIndexToPos_snapped(vDI, vWorld))
                            - camPos).normalized * (100.0f / vD);
                    }
                }

                resultDirection.Normalize();
                float airDis = Vector3.Distance(pos, camPos);
                float oDis = ((vDis / 5) * VoxGlobalSettings.voxelSizeWorld) + Vector3.Distance(snappedPos, pos) + camExtraDis;
                occludedAmount = Mathf.Clamp01((oDis - (airDis + _occludedFilterDisM)) / _occludedFilterDisM);

                UnsafeUtility.Free(vOffsets, Allocator.Temp);
                return oDis;
            }
        }
    }
}
