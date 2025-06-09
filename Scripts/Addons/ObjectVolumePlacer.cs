using UnityEngine;
using VoxelAudio;
using UnityEditor;

namespace VoxelAudioAddons
{
    public class ObjectVolumePlacer : MonoBehaviour
    {
        [Tooltip("If true, sets this transform position to the closest point on the cube relative to closestTo transform." +
            "Use full to make it sound like audio is coming from a area rather than a point")]
        [SerializeField] private bool doPlaceTransform = true;
        [Tooltip("If null uses FMod studio listener")]
        [SerializeField] private Transform closestTo = null;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying == false)
            {
                wToL = transform.worldToLocalMatrix;
                lToW = transform.localToWorldMatrix;
            }

            Gizmos.matrix = lToW;
            Gizmos.DrawCube(Vector3.zero, Vector3.one);

            if (doPlaceTransform == false) return;
            var view = SceneView.currentDrawingSceneView;

            Vector3 pos;
            if (closestTo != null) pos = closestTo.position;
            else if (AudioManager.listener != null) pos = AudioManager.listener.transform.position;
            else if (view != null) pos = view.camera.ViewportToWorldPoint(view.cameraViewport.center);
            else return;

            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.red;
            Gizmos.DrawCube(ClosestPoint(pos), Vector3.one * 0.1f);
        }
#endif

        public Vector3 ClosestPoint(Vector3 pos)
        {
            Vector3 posL = wToL.MultiplyPoint3x4(pos);

            posL.x = Mathf.Clamp(posL.x, -0.5f, 0.5f);
            posL.y = Mathf.Clamp(posL.y, -0.5f, 0.5f);
            posL.z = Mathf.Clamp(posL.z, -0.5f, 0.5f);

            return lToW.MultiplyPoint3x4(posL);
        }

        Matrix4x4 wToL;
        Matrix4x4 lToW;

        private void Awake()
        {
            wToL = transform.worldToLocalMatrix;
            lToW = transform.localToWorldMatrix;
        }

        private void Update()
        {
            if (doPlaceTransform == false)
            {
                wToL = transform.worldToLocalMatrix;
                lToW = transform.localToWorldMatrix;
                return;
            }

            Transform trans;
            if (closestTo != null) trans = closestTo;
            else if (AudioManager.listener != null) trans = AudioManager.listener.transform;
            else return;

            transform.position = ClosestPoint(trans.position);
        }
    }
}
