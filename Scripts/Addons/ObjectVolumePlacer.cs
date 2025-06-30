using UnityEngine;
using VoxelAudio;
using UnityEditor;
using System.Collections.Generic;
using VoxelAudioPlugins;
using System.Linq;
using AudioSettings = VoxelAudio.AudioSettings;

namespace VoxelAudioAddons
{
    public class ObjectVolumePlacer : MonoBehaviour
    {
        [Tooltip("If true, sets this transform (Or top child if custom shapes) position to the closest point on the cube relative to closestTo transform." +
            "Use full to make it sound like audio is coming from a area rather than a point")]
        [SerializeField] private bool doPlaceTransform = true;
        [Tooltip("If true, uses raycast to make sure the ClosestPoint is not inside a collider (Uses AudioSettings reverb rayMaxDistance and mask)")]
        [SerializeField] private bool snapToGeometry = true;
        [Tooltip("If null uses FMod studio listener")]
        [SerializeField] private Transform closestTo = null;
        [SerializeField] internal List<MeshCollider> createdShapes = new();

#if UNITY_EDITOR

        internal bool hasHadCreatedShapes = false;

        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying == false)
            {
                wToL = transform.worldToLocalMatrix;
                lToW = transform.localToWorldMatrix;
                center = transform.position;
            }

            //Draw shape
            Gizmos.color = Color.gray;

            if (createdShapes.Count == 0)
            {
                Gizmos.matrix = lToW;
                Gizmos.DrawCube(Vector3.zero, Vector3.one);
                Gizmos.matrix = Matrix4x4.identity;
            }
            else
            {
                for (int i = 0; i < createdShapes.Count; i++)
                {
                    if (ObjectVolumePlacerEditor.colEditing != null && i == createdShapes.Count - 1) Gizmos.color = Color.white;

                    MeshCollider col = createdShapes[i];
                    if (col == null || col.sharedMesh == null) continue;
                    Gizmos.DrawMesh(col.sharedMesh, 0, transform.position, transform.rotation, transform.lossyScale);
                }
            }

            if (doPlaceTransform == false) return;
            var view = SceneView.currentDrawingSceneView;

            Vector3 pos;
            if (closestTo != null) pos = closestTo.position;
            else if (AudioManager._listener != null) pos = AudioManager._listener.transform.position;
            else if (view != null) pos = view.camera.ViewportToWorldPoint(view.cameraViewport.center);
            else return;

            Gizmos.color = Color.white;
            Gizmos.DrawSphere(ClosestPoint(pos), 0.1f);
        }
#endif

        private const float rayNorOffset = 0.1f;

        public Vector3 ClosestPoint(Vector3 pos)
        {
            Vector3 bPos;
            Vector3 bCenter;

            if (createdShapes.Count > 0)
            {
                float bestD = float.MaxValue;
                bPos = Vector3.zero;
                bCenter = Vector3.zero;

                foreach (MeshCollider mcol in createdShapes)
                {
                    Vector3 cPos = mcol.ClosestPoint(pos);
                    float dis = (cPos - pos).sqrMagnitude;

                    if (dis > bestD) continue;
                    bPos = cPos;
                    bestD = dis;
                    bCenter = mcol.bounds.center;
                }
            }
            else
            {
                Vector3 posL = wToL.MultiplyPoint3x4(pos);

                posL.x = Mathf.Clamp(posL.x, -0.5f, 0.5f);
                posL.y = Mathf.Clamp(posL.y, -0.5f, 0.5f);
                posL.z = Mathf.Clamp(posL.z, -0.5f, 0.5f);

                bPos = lToW.MultiplyPoint3x4(posL);
                bCenter = center;
            }

            //Collision
            if (snapToGeometry == true)
            {
                
                if ((pos - bPos).sqrMagnitude < rayNorOffset) return bPos;

                Vector3 dirDis = (bCenter - bPos).normalized;
                if (Physics.Raycast(bPos, dirDis, out RaycastHit hit, AudioSettings._rayMaxDistance, AudioSettings._mask, QueryTriggerInteraction.Ignore) == true) bCenter = hit.point + (hit.normal * rayNorOffset);
                else bCenter = bPos + (dirDis * (AudioSettings._rayMaxDistance - rayNorOffset));

                if (Physics.Linecast(bCenter, bPos, out hit, AudioSettings._mask, QueryTriggerInteraction.Ignore) == true) bPos = hit.point;
            }

            return bPos;
        }

        private Matrix4x4 wToL;
        private Matrix4x4 lToW;
        private Vector3 center;
        private Transform transToMove;
        private bool useListener = false;

        private void Awake()
        {
            center = transform.position;
            wToL = transform.worldToLocalMatrix;
            lToW = transform.localToWorldMatrix;

            transToMove = createdShapes.Count == 0 ? transform : transform.GetChild(0);
            if (closestTo == null)
            {
                useListener = true;
                closestTo = AudioManager._listener.transform;
            }
        }

        private void Update()
        {
            if (doPlaceTransform == false)
            {
                wToL = transform.worldToLocalMatrix;
                lToW = transform.localToWorldMatrix;
                return;
            }

            transToMove.position = ClosestPoint(useListener == true ? AudioOcclusion._lastCamPos : closestTo.position);
        }
    }

#region Editor
#if UNITY_EDITOR

    [CustomEditor(typeof(ObjectVolumePlacer))]
    public class ObjectVolumePlacerEditor : Editor
    {
        internal static MeshCollider colEditing = null;
        private readonly List<Vector3> createdPoints = new();

        public override void OnInspectorGUI()
        {
            ObjectVolumePlacer placer = (ObjectVolumePlacer)target;
            Collider[] cols = placer.GetComponents<Collider>();

            if (placer.hasHadCreatedShapes == false && placer.createdShapes.Count == 0 && cols.Length > 0)
            {
                EditorGUILayout.HelpBox("This object should not have any colliders, child colliders are fine", MessageType.Warning);
                return;
            }

            foreach (Collider col in cols)
            {
                if (col is MeshCollider mCol == false) continue;
                if (placer.createdShapes.Contains(mCol) == true) continue;
                DestroyImmediate(col);
            }

            for (int i = placer.createdShapes.Count - 1; i >= 0; i--)
            {
                if (placer.createdShapes[i] != null && placer.createdShapes[i].convex == true) continue;
                placer.createdShapes.RemoveAt(i);
                Debug.Log("A collider has been changed or deleted unexpectedly from " + placer.transform.name);
            }

            DrawDefaultInspector();
            GUILayout.Space(10);

            if (colEditing != null)
            {
                if (GUILayout.Button("Stop Creating (ESCAPE)") == true)
                {
                    ExitEditMode();
                }

                if (validHull == false) EditorGUILayout.HelpBox("Invalid shape points!", MessageType.Warning);

                return;
            };

            if (GUILayout.Button("Create Custom Shape"))
            {
                createdPoints.Clear();
                colEditing = placer.gameObject.AddComponent<MeshCollider>();
                colEditing.convex = true;
                colEditing.isTrigger = true;
                colEditing.excludeLayers = Physics.AllLayers;
                colEditing.sharedMesh = null;
                //colEditing.hideFlags = HideFlags.HideInInspector;
                placer.createdShapes.Add(colEditing);
            }

            if (placer.createdShapes.Count > 0)
            {
                placer.hasHadCreatedShapes = true;
                if (placer.transform.childCount > 0) EditorGUILayout.HelpBox("Custom shapes active, top child will be moved instead!", MessageType.Info);
                else EditorGUILayout.HelpBox("Custom shapes active, top child will be moved instead! (No child found)", MessageType.Error);
            }
        }

        private static bool validHull = false;
        private static Vector3 lastNor = Vector3.up;

        private void OnSceneGUI()
        {
            if (colEditing == null) return;

            Event e = Event.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            // Raycast to the collider we're editing
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            {
                Handles.color = Color.green;
                Handles.SphereHandleCap(0, hit.point, Quaternion.identity, 0.2f, EventType.Repaint);

                // On left-click: add point to list
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                {
                    lastNor = hit.normal;
                    createdPoints.Add(hit.point);
                    e.Use(); // Consume the event
                    SceneView.RepaintAll();
                }
            }

            //Scroll to move last placed point along normal
            if (e.type == EventType.ScrollWheel && createdPoints.Count > 0)
            {
                float scrollDir = -e.delta.y; // Scroll up = positive
                createdPoints[^1] += 0.05f * scrollDir * lastNor;

                e.Use(); // Consume the scroll event
                SceneView.RepaintAll();
            }

            // Draw already created points
            for (int i = 0; i < createdPoints.Count; i++)
            {
                Handles.color = i == createdPoints.Count - 1 ? Color.yellow : Color.red;
                Handles.SphereHandleCap(0, createdPoints[i], Quaternion.identity, 0.15f, EventType.Repaint);
            }

            QuickHull_convex hull = new();
            List<int> tris = new(8);
            List<Vector3> vers = new(24);
            List<Vector3> nors = new(24);

            ObjectVolumePlacer placer = (ObjectVolumePlacer)target;
            List<Vector3> pointsL = createdPoints.Select(cp => placer.transform.InverseTransformPoint(cp)).ToList();

            validHull = hull.GenerateHull(pointsL, true, ref vers, ref tris, ref nors);
            if (validHull == true)
            {
                Mesh mesh = colEditing.sharedMesh;
                if (mesh == null) mesh = new();
                mesh.SetVertices(vers);
                mesh.SetNormals(nors);
                mesh.SetTriangles(tris, 0);
                colEditing.sharedMesh = mesh;
            }
            else colEditing.sharedMesh = null;

            // Escape to exit editing mode
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                ExitEditMode();
                e.Use();
            }
        }

        private void ExitEditMode()
        {
            ObjectVolumePlacer placer = (ObjectVolumePlacer)target;
            if (colEditing.sharedMesh == null || validHull == false) placer.createdShapes.RemoveAt(placer.createdShapes.Count - 1);

            colEditing = null;
            EditorUtility.SetDirty(target);
            SceneView.RepaintAll();
        }
    }

#endif
#endregion Editor
}
