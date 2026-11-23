using UnityEngine;
using UnityEngine.Rendering;
using VRPortalToolkit.Data;
using VRPortalToolkit.Rendering;

namespace VRPortalToolkit.Presence
{
    public enum OverlayTechnique { NoOverlay, Contours, Blended, Absolute, Stencil }

    public sealed class PresenceOverlay : PortalRendererBase
    {
        public const float MoveThreshold = 0.03f;
        public const float TurnThreshold = 5f;
        public const float IdleSeconds = 2f;
        public const float TransitionSeconds = 0.5f;
        private static readonly int TechniqueId = Shader.PropertyToID("_Technique");
        private static readonly int RevealId = Shader.PropertyToID("_Reveal");
        private static readonly int ApertureId = Shader.PropertyToID("_Aperture");

        private Material composite;
        private PortalMeshRenderer aperture;
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private float remaining;
        private float progress;

        public OverlayTechnique Technique { get; set; }
        public bool IsActive { get; private set; }
        public float Progress => progress;
        public bool Clipped { get; private set; }
        public int WallMask { get; set; }

        public override IPortal Portal => aperture ? aperture.portal : null;
        public override PortalRendererSettings Overrides => new PortalRendererSettings
        {
            portalStereo = composite,
            depthNormalTexture = Technique == OverlayTechnique.Contours,
        };

        public void Attach(PortalMeshRenderer window, Material material)
        {
            aperture = window;
            composite = material;
            ResetState();
        }

        public void ResetState()
        {
            remaining = progress = 0f;
            IsActive = Clipped = false;
            Transform exit = aperture.portal.connected.transform;
            previousPosition = exit.position;
            previousRotation = exit.rotation;
        }

        // Levelness adjustments are supplied separately from intentional exit motion.
        public void Tick(bool held, bool intent, bool exitManipulated, float delta)
        {
            Transform exit = aperture.portal.connected.transform;
            Vector3 shift = exit.position - previousPosition;
            shift.y = 0f;
            bool moved = exitManipulated &&
                (shift.sqrMagnitude >= MoveThreshold * MoveThreshold ||
                 Quaternion.Angle(previousRotation, exit.rotation) >= TurnThreshold);
            if (moved || !held)
            {
                previousPosition = exit.position;
                previousRotation = exit.rotation;
            }

            if (!held || Technique == OverlayTechnique.NoOverlay)
                remaining = 0f;
            else
                remaining = intent || moved ? IdleSeconds : Mathf.Max(0f, remaining - delta);

            IsActive = remaining > 0f;
            progress = Mathf.MoveTowards(progress, IsActive ? 1f : 0f, delta / TransitionSeconds);
            if (progress <= 0f)
                Clipped = false;
        }

        public override bool TryGetWindow(PortalRenderNode node, Vector3 position, Matrix4x4 view,
            Matrix4x4 projection, out ViewWindow window)
        {
            window = new ViewWindow(1f, 1f, 0f);
            return aperture && Technique != OverlayTechnique.NoOverlay && progress > 0f && node.depth == 0;
        }

        public override bool TryGetClippingPlane(PortalRenderNode parent, out Vector3 centre, out Vector3 normal)
        {
            Matrix4x4 through = aperture.portal.teleportMatrix;
            Vector3 start = through.MultiplyPoint3x4(aperture.transform.position);
            Vector3 end = through.MultiplyPoint3x4(parent.localToWorldMatrix.GetColumn(3));
            Vector3 direction = end - start;
            Clipped = UnityEngine.Physics.Raycast(start, direction.normalized, out RaycastHit hit,
                direction.magnitude, WallMask, QueryTriggerInteraction.Ignore);
            if (Clipped)
            {
                Matrix4x4 back = through.inverse;
                centre = back.MultiplyPoint3x4(hit.point + hit.normal * 0.012f);
                normal = back.MultiplyVector(-hit.normal).normalized;
                return true;
            }

            centre = normal = Vector3.zero;
            return false;
        }

        public override void Render(PortalRenderNode node, CommandBuffer cmd, Material material,
            MaterialPropertyBlock properties = null)
        {
            if (!aperture || properties == null || material != composite)
                return;

            Camera camera = node.camera;
            Transform eye = camera.transform;
            float distance = camera.nearClipPlane + 0.12f;
            float extent = Mathf.Max(1.2f, distance * 2f * camera.aspect);
            Matrix4x4 screenQuad = Matrix4x4.TRS(eye.position + eye.forward * distance, eye.rotation,
                new Vector3(extent * 2f, extent * 2f, 1f));
            properties.SetFloat(TechniqueId, (int)Technique);
            properties.SetFloat(RevealId, progress);
            properties.SetMatrix(ApertureId, aperture.transform.localToWorldMatrix);

            Mesh quad = aperture.filter.sharedMesh;
            if (Technique == OverlayTechnique.Stencil)
            {
                cmd.DrawMesh(quad, screenQuad, composite, 0, 1, properties);
                cmd.DrawMesh(quad, screenQuad, composite, 0, 2, properties);
            }
            else
                cmd.DrawMesh(quad, screenQuad, composite, 0, 0, properties);
        }

        public override void RenderDefault(PortalRenderNode node, CommandBuffer cmd) { }
    }
}
