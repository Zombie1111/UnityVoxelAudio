using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RaytracedAudio
{
    public class AudioSurface : MonoBehaviour
    {
        #region Surface Object
        [SerializeField] private SurfaceType surfaceType = SurfaceType.defualt;
        [SerializeField] private Surface surfaceOverrides = new(-1.0f);
        [Tooltip("Must be true to allow changing surface at runtime")]
        [SerializeField] private bool ownSurfaceInstance = false;
        private Collider[] colliders = null;
        private int surfaceIndex = -1;
        private bool hasOwnInstance = false;

        private void Start()
        {
            AudioManager._instance.Init();
            bool runtimeCreated = GetInstanceID() < 0;//Negative if created at runtime
            if (surfaceIndex < 0)
            {
                if (runtimeCreated == true) ownSurfaceInstance = true;
                InitSurfaceMaterial();
            }

            colliders = GetComponentsInChildren<Collider>();

            foreach (Collider col in colliders)
            {
                if (runtimeCreated == false) RegisterCollider(col);
                else ReRegisterCollider(col);
            }
        }

        private void InitSurfaceMaterial()
        {
            if (ownSurfaceInstance == true || surfaceOverrides.IsAnyValid() == true)
            {
                Surface surf = GetSurface(GetSurfaceI(surfaceType));
                Surface newSurf = surfaceOverrides;//We do not wanna modify surfaceOverrides
                newSurf.SetInvalidFrom(in surf);
                surfaceIndex = newSurf.Register(surfaceType, -1);
                hasOwnInstance = true;
            }
            else
            {
                hasOwnInstance = false;
                surfaceIndex = GetSurfaceI(surfaceType);
            }
        }

        /// <summary>
        /// Finds and registers new colliders. If you know what collider you have added/destroyed
        /// its faster to manually call RegisterCollider(col)/UnregisterCollider(col)
        /// </summary>
        public void DiscoverColliders()
        {
            colliders = GetComponentsInChildren<Collider>();

            foreach (Collider col in colliders)
            {
                RegisterCollider(col);
            }
        }

        private void OnDestroy()
        {
            surfaceIndex = -1;

            foreach (Collider col in colliders)
            {
                UnregisterCollider(col);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (surfaceIndex < 0) return;
            SetSurface(surfaceOverrides, surfaceType);
        }
#endif

        /// <summary>
        /// Sets the surface of all colliders using this AudioSurface
        /// </summary>
        public void SetSurface(ref Surface surface)
        {
            SetSurface(surface, surfaceType);
        }

        /// <summary>
        /// Same as other SetSurface() but also allows changing the surface type
        /// </summary>
        public void SetSurface(Surface surface, SurfaceType type)
        {
            surfaceOverrides = surface;
            surfaceType = type;
            if (surfaceIndex < 0) return;
            if (hasOwnInstance == false)
            {
                Debug.LogError("Unable to set AudioSurface at runtime if ownSurfaceInstance was false at creation: " + transform.name);
                return;
            }

            Surface surf = GetSurface(GetSurfaceI(type));
            surface.SetInvalidFrom(in surf);//Even if surface was surfaceOverrides it is set by copying so surfaceOverrides wont change
            surfaceIndex = surface.Register(type, surfaceIndex);
        }

        internal int GetSurfaceIndex()
        {
            if (surfaceIndex < 0) InitSurfaceMaterial();
            return surfaceIndex;
        }

        #endregion Surface Object

        #region Manage Surfaces
        [System.Serializable]
        internal class SurfaceConfig
        {
            [SerializeField] internal SurfaceType type;
            [SerializeField] internal string[] materialNames;
            [SerializeField] internal Surface surface;

            /// <summary>
            /// Registers this surface and links the surfaceType and materialNames to it, returns the surface index this surface got registered to.
            /// </summary>
            internal int Register()
            {
                int newSurfI = surface.Register(type, surfaceTypeToSurfaceI.TryGetValue(type, out int surfI) == true ? surfI : -1);
                surfaceTypeToSurfaceI[type] = newSurfI;

                if (materialNames == null) return newSurfI;

                foreach (string matName in materialNames)
                {
                    materialNamesToSurfaceI[matName] = newSurfI;
                }

                return newSurfI;
            }
        }

        private static int GetSurfaceI(string name)
        {
            foreach (var nameSurfaceI in materialNamesToSurfaceI)
            {
                if (name.Contains(nameSurfaceI.Key, StringComparison.InvariantCultureIgnoreCase) == false) continue;
                return nameSurfaceI.Value;
            }

            Debug.LogWarning(name + " does not exist in _materialNamesToSurfaceI, using _defualtSurfaceIndex instead");
            return AudioSettings._defualtSurfaceIndex;
        }

        public static int GetSurfaceI(SurfaceType type)
        {
            return surfaceTypeToSurfaceI[type];
            //if (_surfaceTypeToSurfaceI.TryGetValue(type, out int surfI) == true) return surfI;
            //Debug.LogError(type + " does not exist in _surfaceTypeToSurfaceI, using _defualtSurfaceIndex instead");
            //return _defualtSurfaceIndex;
        }

        #region Surface Types

        public enum SurfaceType
        {
            defualt = 0,
            steel = 1,
            metal = 2,
            porcelain = 3,
            concrete = 4,
            rock = 5,
            wood = 6,
            carpet = 7,
            glass = 8,
            plastic = 9,
            sky = 10,//If rays does not hit anything
        }

        internal static readonly SurfaceConfig[] defualtInitSurfaces = new SurfaceConfig[]
        {
            new()
            {
                type = SurfaceType.defualt,
                materialNames = new string[] { "defualt", "default" },
                surface = new Surface() { reflectness = 0.0f, metallicness = 0.0f, brightness = 0.5f, tail = 0.0f }
            },
            new()
            {
                type = SurfaceType.steel,
                materialNames = new string[] { "steel", "pipe", "container", "barrel" },
                surface = new Surface() { reflectness = 0.55f, metallicness = 0.2f, brightness = 0.6f, tail = 0.5f }
            },
            new()
            {
                type = SurfaceType.metal,
                materialNames = new string[] { "metal", "gold", "iron", "wire", "net" },
                surface = new Surface() { reflectness = 0.55f, metallicness = 0.3f, brightness = 0.6f, tail = 0.7f }
            },
            new()
            {
                type = SurfaceType.porcelain,
                materialNames = new string[] { "porcelain", "sink", "vase", "bath" },
                surface = new Surface() { reflectness = 0.4f, metallicness = 0.13f, brightness = 0.6f, tail = 1.0f }
            },
            new()
            {
                type = SurfaceType.concrete,
                materialNames = new string[] { "concrete", "wall", "floor", "gravel", "ceiling", "cemet", "paint", "stair" },
                surface = new Surface() { reflectness = 0.4f, metallicness = 0.05f, brightness = 0.3f, tail = 0.2f }
            },
            new()
            {
                type = SurfaceType.rock,
                materialNames = new string[] { "stone", "rock", "cobble", "brick" },
                surface = new Surface() { reflectness = 0.4f, metallicness = 0.1f, brightness = 0.3f, tail = 0.8f }
            },
            new()
            {
                type = SurfaceType.wood,
                materialNames = new string[] { "wood", "tree", "plank", "house", "roof", "boat", "desk", "speaker", "fence" },
                surface = new Surface() { reflectness = 0.1f, metallicness = 0.0f, brightness = 0.4f, tail = 0.5f }
            },
            new()
            {
                type = SurfaceType.carpet,
                materialNames = new string[] { "carpet", "wool", "blanket", "mattress", "curtain", "fiber", "cloth" },
                surface = new Surface() { reflectness = -0.5f, metallicness = 0.0f, brightness = 0.0f, tail = 0.0f }
            },
            new()
            {
                type = SurfaceType.glass,
                materialNames = new string[] { "glass", "window", "display", "monitor" },
                surface = new Surface() { reflectness = 0.5f, metallicness = 0.13f, brightness = 0.6f, tail = 0.4f }
            },
            new()
            {
                type = SurfaceType.plastic,
                materialNames = new string[] { "plastic", "bag", "keyboard", "mouse", "controller" },
                surface = new Surface() { reflectness = 0.5f, metallicness = 0.0f, brightness = 0.5f, tail = 0.2f }
            },
            new()
            {
                type = SurfaceType.sky,
                materialNames = new string[] { },
                surface = new Surface() { reflectness = 0.0f, metallicness = 0.0f, brightness = 0.5f, tail = 0.0f }
            },
        };

        #endregion Surface Types

        public delegate void Even_OnBeforeAudioSurfacesWrite();
        /// <summary>
        /// Invoked before any audio surfaces are changed to make sure no jobs are accessing the native surface containers
        /// </summary>
        public static event Even_OnBeforeAudioSurfacesWrite OnBeforeAudioSurfacesWrite;



        [System.Serializable]
        public struct Surface
        {
            public float reflectness;
            public float metallicness;
            public float brightness;
            public float tail;

            /// <summary>
            /// If startValue < -1.0f, will use defualt properties
            /// </summary>
            public Surface(float startValue = -2.0f)
            {
                if (startValue < -1.0f)
                {
                    reflectness = 0.0f;
                    metallicness = 0.0f;
                    brightness = 0.5f;
                    tail = 0.0f;

                    return;
                }

                reflectness = startValue;
                metallicness = startValue;
                brightness = startValue;
                tail = startValue;
            }

            /// <summary>
            /// Registers this with the given type and returns the surface index it got registered to.
            /// If surfI >= 0 and in range, overrides existing surface at that index.
            /// </summary>
            public readonly int Register(SurfaceType type, int surfI = -1)
            {
                OnBeforeAudioSurfacesWrite?.Invoke();//Make sure jobs aint running

                if (surfI > -1 && surfI < surfaces.Count)
                {
                    surfaces[surfI] = this;
                    surfaces_native[surfI] = this;
                    surfaceTypes[surfI] = type;
                    surfaceTypes_native[surfI] = type;
                    return surfI;
                }

                surfI = surfaces.Count;
                surfaces_native.Add(this);
                surfaces.Add(this);
                surfaceTypes.Add(type);
                surfaceTypes_native.Add(type);

                return surfI;
            }

            /// <summary>
            /// Returns true if any property is > -1.0f
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly bool IsAnyValid()
            {
                return reflectness > -1.0f || brightness > -1.0f || tail > -1.0f || metallicness > -1.0f;
            }

            /// <summary>
            /// Sets all negative properties from the given surface
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetInvalidFrom(in Surface from)
            {
                if (reflectness <= -1.0f) reflectness = from.reflectness;
                if (metallicness <= -1.0f) metallicness = from.metallicness;
                if (brightness <= -1.0f) brightness = from.brightness;
                if (tail <= -1.0f) tail = from.tail;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal float JoinWith(Surface surf)
            {
                //float weight = 1.0f + Math.Abs(surf.reflectness * 10.0f);//We want reflective stuff to have much higher weight since the last 0.3f besically goes from 0-100% reflectiveness
                float weight = 1.0f + Math.Abs(surf.reflectness);

                reflectness += surf.reflectness * weight;
                metallicness += surf.metallicness * weight;
                brightness += surf.brightness * weight;
                tail += surf.tail * weight;

                return weight;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Devide(float totalWeight)
            {
                reflectness /= totalWeight;
                metallicness /= totalWeight;
                brightness /= totalWeight;
                tail /= totalWeight;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Lerp(in Surface target, float t)
            {
                reflectness = Mathf.Lerp(reflectness, target.reflectness, t);
                metallicness = Mathf.Lerp(metallicness, target.metallicness, t);
                brightness = Mathf.Lerp(brightness, target.brightness, t);
                tail = Mathf.Lerp(tail, target.tail, t);
            }
        }

        private const int _defualtColAlloc = 1024;
        private const int _maxSurfaceTypesPerCol = 8;

        public struct TriRanges
        {
            private int surfaceCount;
            private unsafe fixed int mins[_maxSurfaceTypesPerCol];
            private unsafe fixed int maxs[_maxSurfaceTypesPerCol];
            private unsafe fixed int surfaceIs[_maxSurfaceTypesPerCol];

            internal static bool TryCreateTriRanges(Collider col, out TriRanges triRanges)
            {
                triRanges = new();
                if (col is MeshCollider mCol == false) return false;//Not a mesh collider, TriRanges is not needed

                Mesh mesh = mCol.sharedMesh;
                int surfCount = mesh.subMeshCount;
                if (surfCount <= 1) return false;//Only one submesh, TriRanges is overkill

                if (col.TryGetComponent(out Renderer rend) == false)
                {
                    Debug.LogWarning("Mesh collider " + col.transform.name + " has submeshes but no renderer attatched to it, unexpected!");
                    return false;//Expecting mesh colliders with submeshes to always have a renderer attatched to it
                }

                if (surfCount > _maxSurfaceTypesPerCol)
                {
                    Debug.Log((_maxSurfaceTypesPerCol - surfCount) + " materials will be exluded from " + col.transform.name
                        + "! Consider increasing _maxSurfaceTypesPerCol");

                    surfCount = _maxSurfaceTypesPerCol;
                }

                Material[] mats = rend.sharedMaterials;

                if (mats.Length < surfCount)
                {
                    Debug.LogWarning(col.transform.name + " has a renderer with less materials than submeshes, unexpected!");
                    surfCount = mats.Length;

                    if (surfCount <= 1) return false;//Only one surface, TriRanges is overkill
                }

                unsafe
                {
                    for (int i = 0; i < surfCount; i++)
                    {
                        if (mats[i] == null)
                        {
                            Debug.LogError(col.transform.name + " has a renderer with a null material, this is not allowed!");
                            return false;
                        }

                        SubMeshDescriptor subM = mesh.GetSubMesh(i);
                        triRanges.surfaceIs[i] = AudioSurface.GetSurfaceI(mats[i].name);
                        triRanges.mins[i] = subM.indexStart;
                        triRanges.maxs[i] = subM.indexStart + subM.indexCount;
                    }
                }

                triRanges.surfaceCount = surfCount;
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal unsafe int GetSurfaceI(int triI)
            {
                for (int i = 0; i < surfaceCount; i++)
                {
                    if (triI < mins[i] || triI >= maxs[i]) continue;
                    return surfaceIs[i];
                }

                return surfaceIs[0];
            }

            internal Reference ToReference()
            {
                Reference triRR = new()
                {
                    maxs = new int[surfaceCount],
                    mins = new int[surfaceCount],
                    surfaceIs = new int[surfaceCount],
                };

                unsafe
                {
                    for (int i = 0; i < surfaceCount; i++)
                    {
                        triRR.maxs[i] = maxs[i];
                        triRR.mins[i] = mins[i];
                        triRR.surfaceIs[i] = surfaceIs[i];
                    }
                }

                return triRR;
            }

            internal class Reference//Is this really worth having?
            {
                internal int[] mins = new int[_maxSurfaceTypesPerCol];
                internal int[] maxs = new int[_maxSurfaceTypesPerCol];
                internal int[] surfaceIs = new int[_maxSurfaceTypesPerCol];

                internal int GetSurfaceI(int triI)
                {
                    for (int i = 0; i < maxs.Length; i++)
                    {
                        if (triI < mins[i] || triI >= maxs[i]) continue;
                        return surfaceIs[i];
                    }

                    return surfaceIs[0];
                }
            }
        }

        private static NativeHashMap<int, int> colIdToSurfaceI_native;
        /// <summary>
        /// Stop all jobs accessing this on AudioSurace.event_OnBeforeAudioSurfacesWrite. Recommended to always get new readonly reference before starting job.
        /// </summary>
        public static NativeHashMap<int, int>.ReadOnly _readonlyColIdToSurfaceI => colIdToSurfaceI_native.AsReadOnly();
        private static readonly Dictionary<int, int> colIdToSurfaceI = new(_defualtColAlloc);

        private static NativeHashMap<int, TriRanges> colIdToTriRanges_native;
        /// <summary>
        /// Stop all jobs accessing this on AudioSurace.event_OnBeforeAudioSurfacesWrite. Recommended to always get new readonly reference before starting job.
        /// </summary>
        public static NativeHashMap<int, TriRanges>.ReadOnly _readonlyColIdToTriRanges => colIdToTriRanges_native.AsReadOnly();
        private static readonly Dictionary<int, TriRanges.Reference> colIdToTriRanges = new(_defualtColAlloc);

        private static NativeList<Surface> surfaces_native;
        /// <summary>
        /// Stop all jobs accessing this on AudioSurace.event_OnBeforeAudioSurfacesWrite. Recommended to always get new readonly reference before starting job.
        /// </summary>
        public static NativeArray<Surface>.ReadOnly _readonlySurfaces => surfaces_native.AsReadOnly();
        private static readonly List<Surface> surfaces = new(8);

        private static NativeList<SurfaceType> surfaceTypes_native;
        /// <summary>
        /// Stop all jobs accessing this on AudioSurace.event_OnBeforeAudioSurfacesWrite. Recommended to always get new readonly reference before starting job.
        /// </summary>
        public static NativeArray<SurfaceType>.ReadOnly _readonlySurfaceTypes => surfaceTypes_native.AsReadOnly();
        private static readonly List<SurfaceType> surfaceTypes = new(8);

        private static readonly HashSet<Collider> registeredColliders = new(_defualtColAlloc);
        private static readonly Dictionary<string, int> materialNamesToSurfaceI = new(16);
        private static readonly Dictionary<SurfaceType, int> surfaceTypeToSurfaceI = new(8);

        public static bool isInitilized = false;

        internal static void Allocate()//Called before AudioSettings.Init();
        {
            if (isInitilized == true) return;

            colIdToSurfaceI_native = new NativeHashMap<int, int>(_defualtColAlloc, Allocator.Persistent);
            colIdToTriRanges_native = new NativeHashMap<int, TriRanges>(_defualtColAlloc, Allocator.Persistent);
            surfaces_native = new NativeList<Surface>(8, Allocator.Persistent);
            surfaceTypes_native = new NativeList<SurfaceType>(8, Allocator.Persistent);

            isInitilized = true;
            SceneManager.sceneLoaded += OnSceneLoad;
            AudioSettings._instance.Init(true);
            OnSceneLoad(SceneManager.GetActiveScene(), LoadSceneMode.Additive);
        }

        internal static void Dispose()
        {
            if (isInitilized == false) return;
            isInitilized = false;
            SceneManager.sceneLoaded -= OnSceneLoad;
            OnBeforeAudioSurfacesWrite?.Invoke();//Make sure jobs aint running

            colIdToSurfaceI_native.Dispose();
            colIdToSurfaceI.Clear();
            colIdToTriRanges_native.Dispose();
            colIdToTriRanges.Clear();

            surfaces_native.Dispose();
            surfaces.Clear();
            surfaceTypes_native.Dispose();
            surfaceTypes.Clear();

            registeredColliders.Clear();
            materialNamesToSurfaceI.Clear();
            surfaceTypeToSurfaceI.Clear();
        }

        private static void OnSceneLoad(Scene scene, LoadSceneMode mode)
        {
            if (AudioSettings._registerAllCollidersOnSceneLoad == false) return;

            //Unregister all destroyed colliders
            if (mode != LoadSceneMode.Additive)
            {
                foreach (Collider col in registeredColliders.ToArray())
                {
                    if (col != null) continue;
                    UnregisterCollider(col);
                }
            }

            //Register all colliders in loaded scene
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
                {
                    RegisterCollider(col);
                }
            }
        }

        /// <summary>
        /// Unregisters the collider (If already registered) and then registers it
        /// </summary>
        public static void ReRegisterCollider(Collider col)
        {
            UnregisterCollider(col);
            RegisterCollider(col);
        }

        public static void RegisterCollider(Collider col)
        {
            if (registeredColliders.Add(col) == false) return;

            int colId = col.GetInstanceID();
            AudioSurface aSurf = col.GetComponentInParent<AudioSurface>(true);
            OnBeforeAudioSurfacesWrite?.Invoke();//Make sure jobs aint running

            if (aSurf == null && TriRanges.TryCreateTriRanges(col, out TriRanges triRanges) == true)
            {
                colIdToTriRanges_native.Add(colId, triRanges);
                colIdToTriRanges.Add(colId, triRanges.ToReference());
                return;
            }

            int surfI;
            if (aSurf != null)
            {
                surfI = aSurf.GetSurfaceIndex();
            }
            else
            {
                Renderer rend = col.GetComponentInParent<Renderer>(true);

                if (rend == null)
                {
                    if (col.sharedMaterial == null)
                    {
                        surfI = AudioSettings._defualtSurfaceIndex;
                    }
                    else surfI = GetSurfaceI(col.sharedMaterial.name);
                }
                else surfI = GetSurfaceI(rend.sharedMaterial.name);
            }

            colIdToSurfaceI_native.Add(colId, surfI);
            colIdToSurfaceI.Add(colId, surfI);
        }

        public static void UnregisterCollider(Collider col)
        {
            if (registeredColliders.Remove(col) == false) return;

            int colId = col.GetInstanceID();
            OnBeforeAudioSurfacesWrite?.Invoke();//Make sure jobs aint running

            if (colIdToTriRanges_native.ContainsKey(colId) == true)
            {
                colIdToTriRanges_native.Remove(colId);
                colIdToTriRanges.Remove(colId);
                return;
            }

            if (colIdToSurfaceI_native.ContainsKey(colId) == true)
            {
                colIdToSurfaceI_native.Remove(colId);
                colIdToSurfaceI.Remove(colId);
            }
        }

        /// <summary>
        /// Returns the surface index the given collider instanceID has at the provided triangle index
        /// </summary>
        public static int GetSurfaceI(int colId, int triI = -1)
        {
            if (triI < 0 || colIdToTriRanges.TryGetValue(colId, out TriRanges.Reference triRanges) == false)
            {
                if (colIdToSurfaceI.TryGetValue(colId, out int surfI) == true)
                    return surfI;

                return AudioSettings._defualtSurfaceIndex;
            }

            return triRanges.GetSurfaceI(triI);
        }

        /// <summary>
        /// Returns the surface index the given collider instanceID has at the provided triangle index (Make sure to stop this job on AudioSurace.event_OnBeforeAudioSurfacesWrite)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSurfaceI_native(int colId, int triI,
            ref NativeHashMap<int, TriRanges>.ReadOnly colIdToTriRanges, ref NativeHashMap<int, int>.ReadOnly colIdToSurfaceI)
        {
            if (triI < 0 || colIdToTriRanges.TryGetValue(colId, out TriRanges triRanges) == false)
            {
                if (colIdToSurfaceI.TryGetValue(colId, out int surfI) == true)
                    return surfI;

                //return AudioSettings._defualtSurfaceIndex;
                return 0;//Cant access static AudioSettings._defualtSurfaceIndex from jobs :(
            }

            return triRanges.GetSurfaceI(triI);
        }

        /// <summary>
        /// Returns the surface index at the hit point
        /// </summary>
        public static int GetSurfaceI(RaycastHit hit)
        {
            return GetSurfaceI(hit.colliderInstanceID, hit.triangleIndex);
        }

        public static Surface GetSurface(int surfI)
        {
            if (surfI < 0 || surfI >= surfaces.Count) return surfaces[AudioSettings._defualtSurfaceIndex];
            return surfaces[surfI];
        }

        /// <summary>
        /// (Make sure to stop this job on AudioSurace.event_OnBeforeAudioSurfacesWrite)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Surface GetSurface_native(int surfI, ref NativeArray<Surface>.ReadOnly surfaces)
        {
            //if (surfI < 0 || surfI >= surfaces.Length) return surfaces[AudioSettings._defualtSurfaceIndex];
            if (surfI < 0 || surfI >= surfaces.Length) return surfaces[0];//Cant access static AudioSettings._defualtSurfaceIndex from jobs :(
            return surfaces[surfI];
        }

        public static SurfaceType GetSurfaceType(int surfI)
        {
            if (surfI < 0 || surfI >= surfaceTypes.Count) return surfaceTypes[AudioSettings._defualtSurfaceIndex];
            return surfaceTypes[surfI];
        }

        /// <summary>
        /// (Make sure to stop this job on AudioSurace.event_OnBeforeAudioSurfacesWrite)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SurfaceType GetSurfaceType_native(int surfI, ref NativeArray<SurfaceType>.ReadOnly surfaceTypes)
        {
            //if (surfI < 0 || surfI >= surfaceTypes.Length) return surfaceTypes[AudioSettings._defualtSurfaceIndex];
            if (surfI < 0 || surfI >= surfaceTypes.Length) return surfaceTypes[0];//Cant access static AudioSettings._defualtSurfaceIndex from jobs :(
            return surfaceTypes[surfI];
        }

        #endregion Manage Surfaces
    }
}

