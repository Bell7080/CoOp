using UnityEngine;

namespace CoOpHorror.Networking
{
    // Where approved players appear in this scene: slots in rows of three around this transform, facing its yaw.
    public sealed class SpawnArea : MonoBehaviour
    {
        public float Spacing = 2f;

        private void Awake() => RoomController.SetSpawnArea(transform.position, transform.eulerAngles.y, Spacing);

        private void OnDestroy() => RoomController.ResetSpawnArea();
    }
}
