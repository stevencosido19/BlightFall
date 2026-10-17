using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(UnityEngine.CharacterController))]
public class CharacterController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 6f;

    [Header("Gravity Settings")]
    [SerializeField] private float gravity = -9.81f;

    [Header("Collider Adjustments")]
    [SerializeField] private float controllerHeight = 2.0f;
    [SerializeField] private float controllerRadius = 0.5f;
    [SerializeField] private Vector3 controllerCenter = new Vector3(0f, 1f, 0f);

    private UnityEngine.CharacterController controller;
    private Vector3 moveDirection;
    private Vector3 velocity;

    private void Awake()
    {
        controller = GetComponent<UnityEngine.CharacterController>();
        ApplyColliderSettings();
    }

    private void OnValidate()
    {
        // Automatically applies changes in the Inspector while editing or testing
        if (controller == null)
        {
            controller = GetComponent<UnityEngine.CharacterController>();
        }

        if (controller != null)
        {
            ApplyColliderSettings();
        }
    }

    private void ApplyColliderSettings()
    {
        controller.height = controllerHeight;
        controller.radius = controllerRadius;
        controller.center = controllerCenter;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            Debug.Log($"Local player spawned with NetworkClientId: {OwnerClientId}");
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        // 1. READ COMBINED INPUT
        Vector2 input = ReadInput();

        // 2. CONVERT 2D INPUT TO 3D MOVEMENT
        moveDirection = new Vector3(input.x, 0f, input.y).normalized;

        // 3. APPLY MOVEMENT
        controller.Move(moveDirection * moveSpeed * Time.deltaTime);

        // 4. APPLY GRAVITY
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }

        controller.Move(velocity * Time.deltaTime);
    }

    private Vector2 ReadInput()
    {
        Vector2 input = Vector2.zero;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
        }

        if (Gamepad.current != null)
        {
            Vector2 stickInput = Gamepad.current.leftStick.ReadValue();
            if (stickInput.sqrMagnitude > 0.01f)
            {
                input = stickInput;
            }
        }

        return Vector2.ClampMagnitude(input, 1f);
    }

    // Tells Netcode to only run this code on the specific client that owns this character
    [Rpc(SendTo.Owner)]
    public void TeleportToSpawnRpc(Vector3 spawnPosition)
    {
        if (controller == null) controller = GetComponent<UnityEngine.CharacterController>();

        // We must turn off the CharacterController, move the transform, and turn it back on.
        controller.enabled = false;
        transform.position = spawnPosition;
        controller.enabled = true;
    }
}