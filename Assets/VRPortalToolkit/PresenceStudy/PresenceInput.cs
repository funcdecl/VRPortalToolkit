using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VRPortalToolkit.Presence
{
    internal sealed class PresenceInput
    {
        private float yaw;
        private float pitch = 15f;
        private bool desktopHeld = true;
        private bool previousAim;
        private bool previousSelect;
        private bool previousMenu;
        private bool xrConfigured;
        private float trackingHeight = 1.62f;

        public bool UsingHeadset => XRSettings.isDeviceActive;
        public bool Held { get; private set; } = true;
        public bool Aim { get; private set; }
        public bool ReleasedAim { get; private set; }
        public bool Select { get; private set; }
        public bool Advance { get; private set; }
        public float Turn { get; private set; }
        public Ray Ray { get; private set; }
        public Vector3 PortalPosition { get; private set; }
        public Quaternion PortalRotation { get; private set; } = Quaternion.Euler(0, 180f, 0);

        public void Recenter(Transform eye)
        {
            yaw = 0f;
            pitch = 15f;
            if (!UsingHeadset)
                eye.SetPositionAndRotation(new Vector3(0f, 1.62f, 0f), Quaternion.Euler(pitch, yaw, 0f));
        }

        public void Read(Transform eye, bool allowLook, bool leftHanded)
        {
            if (UsingHeadset)
                ReadHeadset(eye, leftHanded);
            else
                ReadDesktop(eye, allowLook);
        }

        private void ReadDesktop(Transform eye, bool allowLook)
        {
            if (allowLook)
            {
                float directionX = Axis(KeyCode.LeftArrow, KeyCode.RightArrow);
                float directionY = Axis(KeyCode.UpArrow, KeyCode.DownArrow);
                yaw += 95f * Time.unscaledDeltaTime * directionX;
                pitch += 78f * Time.unscaledDeltaTime * directionY;
                if (Input.GetMouseButton(1))
                {
                    yaw += 2.25f * Input.GetAxisRaw("Mouse X");
                    pitch -= 2.25f * Input.GetAxisRaw("Mouse Y");
                }
                pitch = Mathf.Clamp(pitch, -65f, 75f);
                eye.rotation = Quaternion.Euler(pitch, yaw, 0f);
                if (Input.GetKeyDown(KeyCode.Home))
                    Recenter(eye);
            }

            if (Input.GetKeyDown(KeyCode.F))
                desktopHeld = !desktopHeld;
            Held = desktopHeld;
            Aim = Held && Input.GetKey(KeyCode.Space);
            ReleasedAim = previousAim && !Aim && Held;
            previousAim = Aim;
            Select = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.G);
            Advance = Input.GetKeyDown(KeyCode.Return);
            Turn = Axis(KeyCode.Q, KeyCode.E) * Time.unscaledDeltaTime * 100f;
            Ray = new Ray(eye.position, eye.forward);
            PortalPosition = new Vector3(0f, 1.41f, 0.57f);
            PortalRotation = Quaternion.Euler(0, 180f, 0);
        }

        private void ReadHeadset(Transform eye, bool leftHanded)
        {
            if (!xrConfigured)
            {
                var subsystems = new List<XRInputSubsystem>();
                SubsystemManager.GetInstances(subsystems);
                foreach (XRInputSubsystem subsystem in subsystems)
                    if (subsystem.running && subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
                        trackingHeight = 0f;
                xrConfigured = true;
            }

            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 headPosition))
                eye.position = headPosition + Vector3.up * trackingHeight;
            if (head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion headRotation))
                eye.rotation = headRotation;

            InputDevice holding = InputDevices.GetDeviceAtXRNode(leftHanded ? XRNode.LeftHand : XRNode.RightHand);
            InputDevice other = InputDevices.GetDeviceAtXRNode(leftHanded ? XRNode.RightHand : XRNode.LeftHand);
            Held = holding.TryGetFeatureValue(CommonUsages.gripButton, out bool grip) && grip;
            holding.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
            Aim = Held && stick.y > 0.5f;
            ReleasedAim = previousAim && !Aim && Held;
            previousAim = Aim;
            Turn = Held ? Mathf.Clamp(stick.x * 1.5f, -1f, 1f) * Time.unscaledDeltaTime * 100f : 0f;

            if (Held && holding.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 hand) &&
                holding.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion handRotation))
            {
                Vector3 forward = Vector3.ProjectOnPlane(handRotation * Vector3.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.01f)
                    forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
                forward.Normalize();
                PortalPosition = hand + Vector3.up * (trackingHeight + 0.13f) + forward * 0.10f;
                if (Vector3.Dot(PortalPosition - eye.position, forward) < 0.22f)
                    PortalPosition += forward * (0.22f - Vector3.Dot(PortalPosition - eye.position, forward));
                PortalRotation = Quaternion.LookRotation(-forward, Vector3.up);
            }

            if (other.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 otherPosition) &&
                other.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion otherRotation))
                Ray = new Ray(otherPosition + Vector3.up * trackingHeight, otherRotation * Vector3.forward);
            else
                Ray = new Ray(eye.position, eye.forward);

            bool select = other.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger) && trigger;
            Select = select && !previousSelect;
            previousSelect = select;
            bool menu = other.TryGetFeatureValue(CommonUsages.primaryButton, out bool button) && button;
            Advance = menu && !previousMenu;
            previousMenu = menu;
        }

        private static float Axis(KeyCode negative, KeyCode positive)
        {
            return (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
        }
    }
}
