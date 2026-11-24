using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CoOpHorror.Networking
{
    // Deliberately small movement sandbox, not the final first-person controller.
    public sealed class SandboxAvatarMotor : NetworkBehaviour
    {
        public const float Speed = 4f;

        public override void OnNetworkSpawn()
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                var tint = new MaterialPropertyBlock();
                tint.SetColor("_BaseColor", Color.HSVToRGB((OwnerClientId * 0.19f + 0.42f) % 1f, 0.6f, 0.9f));
                renderer.SetPropertyBlock(tint);
            }
        }

        private void Update()
        {
            if (!IsOwner || !IsSpawned || Keyboard.current == null) return;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.GetComponent<UnityEngine.UI.InputField>() != null) return;
            var k = Keyboard.current;
            Move(new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0),
                (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0)), Time.deltaTime);
            if (k.eKey.wasPressedThisFrame) GetComponent<ConnectionAvatar>().SendSignal();
        }

        public void Move(Vector2 direction, float deltaTime)
        {
            if (!IsOwner || !IsSpawned) return;
            direction = Vector2.ClampMagnitude(direction, 1f);
            var next = transform.position + new Vector3(direction.x, 0, direction.y) * (Speed * Mathf.Clamp(deltaTime, 0, 0.05f));
            next.x = Mathf.Clamp(next.x, -9, 9);
            next.z = Mathf.Clamp(next.z, -9, 9);
            next.y = 1.05f;
            transform.position = next;
        }
    }
}
