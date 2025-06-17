using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using zombVoxels;

namespace VoxelAudio
{
    internal static class AudioReverb
    {
        /// <summary>
        /// Last assigned one is always player cam
        /// </summary>
        internal static NativeArray<AudioInstance.ReverbData> aisReverbData_native;
        private static NativeArray<RaycastHit> rayHits;
        private static NativeArray<RaycastCommand> rayCommands;
        private static bool isInitialized = false;
        private const int _axisDirCount = 6;

        internal static void Allocate()
        {
            if (isInitialized == true) return;

            AudioSurface.OnBeforeAudioSurfacesWrite += OnBeforeAudioSurfacesWrite;
            playerSurface = new(-2.0f);

            rayHits = new NativeArray<RaycastHit>(AudioSettings._maxReverbVoices * _axisDirCount, Allocator.Persistent);
            rayCommands = new NativeArray<RaycastCommand>(AudioSettings._maxReverbVoices * _axisDirCount, Allocator.Persistent);
            aisReverbData_native = new NativeArray<AudioInstance.ReverbData>(AudioSettings._maxReverbVoices, Allocator.Persistent);

            pr_job = new()
            {
                aisReverbData_native = aisReverbData_native.AsReadOnly(),
                rayCommands = rayCommands,
            };

            cr_job = new()
            {
                aisReverbData_native = aisReverbData_native,
                rayHits = rayHits.AsReadOnly(),                
            };

            unsafe
            {
                RaycastCommand baseCommand = new()
                {
                    distance = AudioSettings._rayMaxDistance,
                    queryParameters = new QueryParameters
                    {
                        layerMask = AudioSettings._mask,
                        hitBackfaces = false,
                        hitMultipleFaces = false,
                        hitTriggers = QueryTriggerInteraction.Ignore,
                    }
                };

                UnsafeUtility.MemCpyReplicate(NativeArrayUnsafeUtility.GetUnsafePtr(rayCommands), UnsafeUtility.AddressOf(ref baseCommand),
                    UnsafeUtility.SizeOf<RaycastCommand>(), AudioSettings._maxReverbVoices * _axisDirCount);
            }

            isInitialized = true;
        }

        internal static void Dispose()
        {
            if (isInitialized == false) return;
            isInitialized = false;

            AudioSurface.OnBeforeAudioSurfacesWrite -= OnBeforeAudioSurfacesWrite;
            EndReverbCompute();

            rayCommands.Dispose();
            rayHits.Dispose();
            aisReverbData_native.Dispose();
        }

        private static void OnBeforeAudioSurfacesWrite()
        {
            EndReverbCompute();
        }

        private const int _rotIterationCount = 8;//Hardcoded 8
        private const int _raysPerThread = 100;
        private static AudioSurface.Surface playerSurface = new(-2.0f);
        private static int lastRealSourceCount = -1;

        internal static void StartReverbCompute(float deltaTime, int realSourceCount)
        {
            if (cr_jobIsActive == true) return;
            if (realSourceCount == 0 && AudioSettings._globalAudioEffects.HasReverb() == false) return;
            ray_jobIsActive = true;
            cr_jobIsActive = true;

            //Set listener rays
            var plData = aisReverbData_native[realSourceCount];
            plData.pos = AudioManager.camPos;
            plData.reverbFilter = new(System.IntPtr.Zero);
            plData.surf = playerSurface;
            plData.isNew = false;
            aisReverbData_native[realSourceCount++] = plData;
            lastRealSourceCount = realSourceCount;

            //Set prepare rays job
            int rotIteration = Time.frameCount % _rotIterationCount;
            switch (rotIteration)
            {
                case 0:
                    pr_job.rayRot = Quaternion.identity;
                    break;
                case 1:
                    pr_job.rayRot = Quaternion.Euler(45, 45, 45);
                    break;
                case 2:
                    pr_job.rayRot = Quaternion.Euler(-45, 45, 45);
                    break;
                case 3:
                    pr_job.rayRot = Quaternion.Euler(45, -45, 45);
                    break;
                case 4:
                    pr_job.rayRot = Quaternion.Euler(45, 45, -45);
                    break;
                case 5:
                    pr_job.rayRot = Quaternion.Euler(-45, -45, -45);
                    break;
                case 6:
                    pr_job.rayRot = Quaternion.Euler(-45, 45, 0);
                    break;
                case 7:
                    pr_job.rayRot = Quaternion.Euler(45, -45, 0);
                    break;
            }

            pr_job.realSourceCount = realSourceCount;

            //Set collect rays job
            cr_job.realSourceCount = realSourceCount;
            cr_job.surfLerpDelta = deltaTime * AudioSettings._reverbLerpSpeed;
            cr_job.surfaces = AudioSurface._readonlySurfaces;
            cr_job.colIdToSurfaceI = AudioSurface._readonlyColIdToSurfaceI;
            cr_job.colIdToTriRanges = AudioSurface._readonlyColIdToTriRanges;
            cr_job.skySurfaceI = AudioSurface.GetSurfaceI(AudioSurface.SurfaceType.sky);

#if UNITY_EDITOR
            cr_job.debugDraw = AudioSettings._debugMode == DebugMode.drawAudioReverb;
#endif

            //Run
            var prHandle = pr_job.Schedule();
            ray_handle = RaycastCommand.ScheduleBatch(rayCommands, rayHits, _raysPerThread, 1, prHandle);
            cr_handle = cr_job.Schedule(ray_handle);
        }

        internal static void EndReverbCompute()
        {
            if (cr_jobIsActive == false) return;

            CompleteReverbRays();
            cr_handle.Complete();
            cr_jobIsActive = false;

            playerSurface = aisReverbData_native[lastRealSourceCount - 1].surf;
        }

        internal static void CompleteReverbRays()//This is seperate because we wanna complete it early due to the way Raycastcommands work
        {
            if (ray_jobIsActive == false) return;

            ray_handle.Complete();
            ray_jobIsActive = false;
        }

        private static PrepareRays_job pr_job;
        private static JobHandle ray_handle;
        private static bool ray_jobIsActive = false;

        [BurstCompile]
        private struct PrepareRays_job : IJob
        {
            internal NativeArray<AudioInstance.ReverbData>.ReadOnly aisReverbData_native;
            internal NativeArray<RaycastCommand> rayCommands;
            internal Quaternion rayRot;
            internal int realSourceCount;

            public void Execute()
            {
                //Transform ray dirs
                NativeArray<Vector3> rayDirs = AudioHelpMethods.GetAxisDirsNative();

                for (int i = 0; i < _axisDirCount; i++)
                {
                    rayDirs[i] = rayRot * rayDirs[i];
                }

                //Set ray commands
                for (int i = 0; i < realSourceCount; i++)
                {
                    AudioInstance.ReverbData aiD = aisReverbData_native[i];

                    for (int rayI = 0; rayI < _axisDirCount; rayI++)
                    {
                        Vector3 rDir = rayDirs[rayI];
                        RaycastCommand rCommand = rayCommands[(i * _axisDirCount) + rayI];
                        rCommand.from = aiD.pos - (rDir * VoxGlobalSettings.voxelSizeWorld);
                        rCommand.direction = rDir;
                        rayCommands[(i * _axisDirCount) + rayI] = rCommand;
                    }
                }
            }
        }

        private static CollectRays_job cr_job;
        private static JobHandle cr_handle;
        private static bool cr_jobIsActive = false;

        [BurstCompile]
        private struct CollectRays_job : IJob
        {
            internal NativeArray<AudioInstance.ReverbData> aisReverbData_native;
            internal NativeArray<RaycastHit>.ReadOnly rayHits;
            internal int realSourceCount;
            internal float surfLerpDelta;
            internal int skySurfaceI;

            internal NativeArray<AudioSurface.Surface>.ReadOnly surfaces;
            internal NativeHashMap<int, AudioSurface.TriRanges>.ReadOnly colIdToTriRanges;
            internal NativeHashMap<int, int>.ReadOnly colIdToSurfaceI;

#if UNITY_EDITOR
            internal bool debugDraw;
#endif

            public void Execute()
            {
                AudioSurface.Surface plSurf = aisReverbData_native[realSourceCount - 1].surf;
                int lastSourceI = realSourceCount - 1;//Last is player
                AudioSurface.Surface skySurf = AudioSurface.GetSurface_native(skySurfaceI, ref surfaces);

                for (int i = 0; i < realSourceCount; i++)
                {
                    AudioInstance.ReverbData aiD = aisReverbData_native[i];
                    AudioSurface.Surface surf = new(-2.0f);

                    if (aiD.ignoreSelfRays == false)
                    {
                        float totWeight = 0.0f;

                        for (int rayI = 0; rayI < _axisDirCount; rayI++)
                        {
                            RaycastHit hit = rayHits[(i * _axisDirCount) + rayI];
                            if (hit.colliderInstanceID == 0)
                            {
                                totWeight += surf.JoinWith(skySurf);
                                continue;
                            }

                            totWeight += surf.JoinWith(AudioSurface.GetSurface_native(
                                AudioSurface.GetSurfaceI_native(hit.colliderInstanceID, hit.triangleIndex, ref colIdToTriRanges, ref colIdToSurfaceI), ref surfaces));

#if UNITY_EDITOR
                            if (debugDraw == false) continue;
                            Debug.DrawLine(aiD.pos, hit.point, Color.blue, 0.1f);
#endif
                        }

                        if (totWeight > 0.0f) surf.Devide(totWeight);
                    }
                    else surf = surfaces[0];

                    if (lastSourceI != i) surf.Lerp(plSurf, 0.5f);//Blend 50% between player surroundings and source surroundings (Except for player one)
                    if (aiD.isNew == false) aiD.surf.Lerp(surf, surfLerpDelta);
                    else
                    {
                        aiD.isNew = false;
                        aiD.surf = surf;
                    }

                    aisReverbData_native[i] = aiD;

                    //Apply to reverb filter
                    if (aiD.reverbFilter.hasHandle() == false) continue;//Expecting this to be false for player

                    aiD.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DIFFUSION, Mathf.Lerp(0.0f, 100.0f, aiD.surf.brightness));
                    aiD.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DENSITY, Mathf.Lerp(100.0f, 0.0f, aiD.surf.brightness));

                    aiD.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HIGHCUT, Mathf.Lerp(20.0f, 4000.0f, aiD.surf.metallicness));

                    aiD.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.EARLYLATEMIX, Mathf.Lerp(0.0f, 66.0f, aiD.surf.tail * 2.0f));
                    aiD.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.HFDECAYRATIO, Mathf.Lerp(0.0f, 100.0f, (aiD.surf.tail - 0.5f) * 2.0f));

                    float wetL = 10.377f * Mathf.Log(Mathf.Clamp01(aiD.surf.reflectness) * Mathf.Clamp01(aiD.surf.reflectness)) + 95.029f;
                    aiD.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.DECAYTIME, Mathf.Exp(0.0981f * wetL));
                    aiD.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.WETLEVEL, (wetL - 80.0f) * 0.5f);

                    //float mag = aiD.surf.Magnitude();
                    //aiD.reverbFilter.setParameterFloat((int)FMOD.DSP_SFXREVERB.WETLEVEL, Mathf.Lerp(-80.0f, 0.0f, mag));
                }
            }
        }
    }

}
