using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Key = UnityEngine.InputSystem.Key;
using Keyboard = UnityEngine.InputSystem.Keyboard;
using Mouse = UnityEngine.InputSystem.Mouse;

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
                float directionX = Axis(Key.LeftArrow, Key.RightArrow);
                float directionY = Axis(Key.UpArrow, Key.DownArrow);
                yaw += 95f * Time.unscaledDeltaTime * directionX;
                pitch += 78f * Time.unscaledDeltaTime * directionY;
                Mouse mouse = Mouse.current;
                if (mouse != null && mouse.rightButton.isPressed)
                {
                    Vector2 drag = mouse.delta.ReadValue();
                    yaw += 0.15f * drag.x;
                    pitch -= 0.15f * drag.y;
                }
                pitch = Mathf.Clamp(pitch, -65f, 75f);
                eye.rotation = Quaternion.Euler(pitch, yaw, 0f);
                if (Pressed(Key.Home))
                    Recenter(eye);
            }

            if (Pressed(Key.F))
                desktopHeld = !desktopHeld;
            Held = desktopHeld;
            Aim = Held && HeldKey(Key.Space);
            ReleasedAim = previousAim && !Aim && Held;
            previousAim = Aim;
            Select = Mouse.current?.leftButton.wasPressedThisFrame == true || Pressed(Key.G);
            Advance = Pressed(Key.Enter);
            Turn = Axis(Key.Q, Key.E) * Time.unscaledDeltaTime * 100f;
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

        public static bool Pressed(Key key) => Keyboard.current?[key].wasPressedThisFrame == true;

        private static bool HeldKey(Key key) => Keyboard.current?[key].isPressed == true;

        private static float Axis(Key negative, Key positive)
        {
            return (HeldKey(positive) ? 1f : 0f) - (HeldKey(negative) ? 1f : 0f);
        }
    }
}
