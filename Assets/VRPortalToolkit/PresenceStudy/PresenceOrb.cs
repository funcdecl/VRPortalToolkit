using UnityEngine;

namespace VRPortalToolkit.Presence
{
    public sealed class PresenceOrb : MonoBehaviour
    {
        public int Number { get; set; }
        public Vector3 Home { get; set; }
        public Transform HomeParent { get; set; }
        public int HomeLayer { get; set; }

        public void ReturnHome()
        {
            transform.SetParent(HomeParent, true);
            transform.position = Home;
            gameObject.layer = HomeLayer;
            GetComponent<Collider>().enabled = true;
        }
    }
}
