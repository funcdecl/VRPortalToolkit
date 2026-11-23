using UnityEngine;

namespace VRPortalToolkit.Presence
{
    public sealed class PresenceLabel : MonoBehaviour
    {
        public float Offset { get; set; }
        public Camera Eye { get; set; }
        public Portal Entrance { get; set; }
        private Vector3 anchor;

        private void Start()
        {
            anchor = transform.localPosition;
        }

        private void LateUpdate()
        {
            if (!Eye) return;
            Vector3 position = transform.parent.TransformPoint(anchor);
            Vector3 eyePosition = Eye.transform.position;
            if (position.x > PresenceLayout.Origin.x * 0.5f)
                eyePosition = Entrance.teleportMatrix.MultiplyPoint3x4(eyePosition);
            Vector3 towardEye = (eyePosition - position).normalized;
            transform.SetPositionAndRotation(position + towardEye * Offset, Quaternion.LookRotation(-towardEye));
        }
    }
}
