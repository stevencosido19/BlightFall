using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Explicitly defined roles
public enum CharacterRole
{
    Knight,
    Arbalist,
    Rogue
}

[RequireComponent(typeof(UnityEngine.CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Character Role")]
    [Tooltip("Select which character this prefab represents")]
    [SerializeField] private CharacterRole characterRole;
    public CharacterRole Role => characterRole;

    // Role-specific abilities
    public bool CanPushRocks => characterRole == CharacterRole.Knight;
    public bool CanPickLocks => characterRole == CharacterRole.Rogue;
    public bool CanUseCrossbow => characterRole == CharacterRole.Arbalist;

    // NonSerialized prevents Unity from permanently saving 'true' into the prefab
    [System.NonSerialized] public bool isMovementLocked = false;

    // Remembers the last direction you walked without rotating the 2D transform
    public Vector3 LastFacingDirection { get; private set; } = Vector3.forward;

    [Header("Inventory (Key)")]
    // 0 = No key, 1 = Key #1, 2 = Key #2, etc. Synced across the network.
    public NetworkVariable<int> CurrentKeyId = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

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
            Debug.Log($"Local player spawned as [{characterRole}] with ClientId: {OwnerClientId}");

            // Automatically toggle the on-screen Key UI when you pick up or use a key
            CurrentKeyId.OnValueChanged += (previous, current) =>
            {
                if (KeyHUD.Instance != null)
                {
                    KeyHUD.Instance.SetKeyVisible(current > 0);
                }
            };

            // Check initial state on spawn
            if (KeyHUD.Instance != null)
            {
                KeyHUD.Instance.SetKeyVisible(CurrentKeyId.Value > 0);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        // 1. Disables movement if not the owner OR if movement is locked (while aiming with Scope)
        if (!IsOwner || isMovementLocked) return;

        // 2. READ COMBINED INPUT
        Vector2 input = ReadInput();

        // 3. CONVERT 2D INPUT TO 3D MOVEMENT
        moveDirection = new Vector3(input.x, 0f, input.y).normalized;

        // 4. REMEMBER LAST MOVED DIRECTION (No transform rotation!)
        if (moveDirection.sqrMagnitude > 0.01f)
        {
            LastFacingDirection = moveDirection;
        }

        // 5. APPLY MOVEMENT
        controller.Move(moveDirection * moveSpeed * Time.deltaTime);

        // 6. APPLY GRAVITY
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

    [Rpc(SendTo.Owner)]
    public void TeleportToSpawnRpc(Vector3 spawnPosition)
    {
        if (controller == null) controller = GetComponent<UnityEngine.CharacterController>();

        controller.enabled = false;
        transform.position = spawnPosition;
        controller.enabled = true;
    }
}