using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PlayerHealth : NetworkBehaviour, IDamageable
{
    public static PlayerHealth LocalPlayerInstance { get; private set; }
    public static event Action<PlayerHealth> OnLocalPlayerHealthReady;

    // Tracks all active players on the network for fast revive & defeat checks
    public static readonly List<PlayerHealth> AllPlayers = new List<PlayerHealth>();
    private static bool defeatTriggeredThisMatch = false;
    private static bool isRestartingStage = false;

    [Header("Max Health per Role")]
    [SerializeField] private int knightHealth = 150;
    [SerializeField] private int arbalistHealth = 100;
    [SerializeField] private int rogueHealth = 80;

    [Header("Character & Soul Sprite References")]
    [Tooltip("Drag the child object that has the character's SpriteRenderer inside")]
    [SerializeField] private SpriteRenderer characterSpriteRenderer;
    [Tooltip("Optional: parent visual object (e.g., KnightSprite / RougeSprite / ArbalistSprite). If empty, uses Character Sprite Renderer's GameObject.")]
    [SerializeField] private GameObject characterVisual;
    [Tooltip("Drag the Soul child object that replaces the character when dead")]
    [SerializeField] private GameObject soulVisual;

    [Header("Hit Feedback (Red Flash & Shake)")]
    [Tooltip("The visual child/sprite to shake (if empty, uses Character Sprite Renderer)")]
    [SerializeField] private Transform visualToShake;
    [SerializeField] private float shakeDuration = 0.15f;
    [SerializeField] private float shakeIntensity = 0.12f;
    [SerializeField] private Color hitColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Header("Soul Float Animation")]
    [SerializeField] private float soulFloatSpeed = 2.5f;
    [SerializeField] private float soulFloatHeight = 0.15f;

    [Header("Revive Settings")]
    [SerializeField] private float reviveRange = 2.5f;
    [SerializeField] private float reviveDuration = 2.5f;
    [Range(0.1f, 1f)]
    [SerializeField] private float reviveHealthPercent = 0.5f;
    [Tooltip("If true, teammate must press/hold the Revive button or [E]. If false, standing near revives automatically.")]
    [SerializeField] private bool requireButtonToRevive = true;
    [Tooltip("If true, hold the Revive button / [E] for Revive Duration. If false, 1 tap revives immediately.")]
    [SerializeField] private bool holdToRevive = true;

    [Header("HUD Buttons (Revive & Attack Swap)")]
    [SerializeField] private string reviveButtonTag = "ReviveButton";
    [SerializeField] private string attackButtonTag = "AttackButton";
    [Tooltip("If true, hides the Revive button when no dead teammate is nearby.")]
    [SerializeField] private bool hideButtonWhenOutOfRange = true;
    [Tooltip("If true, hides AttackButton when dead or when ReviveButton is shown.")]
    [SerializeField] private bool hideAttackButtonWhenDeadOrReviving = true;

    [Header("Revive Progress Slider (World Space)")]
    [Tooltip("Optional parent Canvas/container to hide/show. If empty, hides/shows the Slider itself.")]
    [SerializeField] private GameObject reviveUIContainer;
    [SerializeField] private Slider reviveProgressSlider;

    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> ReviveProgress = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> isHoldingReviveInput = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public int MaxHealth { get; private set; }
    public CharacterRole PlayerRole { get; private set; }

    private PlayerController playerController;
    private SpriteRenderer[] spriteRenderers;
    private Color[] originalSpriteColors;
    private Coroutine hitFeedbackRoutine;
    private Vector3 initialLocalPos;
    private Vector3 initialSoulLocalPos;

    private Button mobileReviveButton;
    private Button mobileAttackButton;
    private bool mobileReviveHeld = false;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();

        if (characterSpriteRenderer == null)
        {
            if (characterVisual != null)
            {
                characterSpriteRenderer = characterVisual.GetComponentInChildren<SpriteRenderer>(true);
            }
            else
            {
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (soulVisual != null && (sr.gameObject == soulVisual || sr.transform.IsChildOf(soulVisual.transform)))
                        continue;

                    characterSpriteRenderer = sr;
                    break;
                }
            }
        }

        if (characterVisual != null)
        {
            spriteRenderers = characterVisual.GetComponentsInChildren<SpriteRenderer>(true);
        }
        else if (characterSpriteRenderer != null)
        {
            spriteRenderers = characterSpriteRenderer.GetComponentsInChildren<SpriteRenderer>(true);
        }
        else
        {
            spriteRenderers = Array.Empty<SpriteRenderer>();
        }

        originalSpriteColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            originalSpriteColors[i] = spriteRenderers[i].color;
        }

        if (visualToShake == null)
        {
            if (characterSpriteRenderer != null && characterSpriteRenderer.transform != transform)
            {
                visualToShake = characterSpriteRenderer.transform;
            }
            else if (characterVisual != null && characterVisual.transform != transform)
            {
                visualToShake = characterVisual.transform;
            }
        }

        if (visualToShake != null)
        {
            initialLocalPos = visualToShake.localPosition;
        }

        if (soulVisual != null)
        {
            initialSoulLocalPos = soulVisual.transform.localPosition;
        }

        if (reviveProgressSlider != null)
        {
            reviveProgressSlider.minValue = 0f;
            reviveProgressSlider.maxValue = 1f;
            reviveProgressSlider.value = 0f;
            reviveProgressSlider.interactable = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!AllPlayers.Contains(this))
        {
            AllPlayers.Add(this);
        }

        PlayerController controller = GetComponent<PlayerController>();
        if (controller != null)
        {
            PlayerRole = controller.Role;
            MaxHealth = PlayerRole switch
            {
                CharacterRole.Knight => knightHealth,
                CharacterRole.Arbalist => arbalistHealth,
                CharacterRole.Rogue => rogueHealth,
                _ => 100
            };
        }

        if (IsServer)
        {
            isRestartingStage = false;
            defeatTriggeredThisMatch = false;
            CurrentHealth.Value = MaxHealth;
            IsDead.Value = false;
            ReviveProgress.Value = 0f;
        }

        IsDead.OnValueChanged += OnDeadStateChanged;
        ApplyDeadState(IsDead.Value);

        if (IsOwner)
        {
            LocalPlayerInstance = this;
            OnLocalPlayerHealthReady?.Invoke(this);

            CurrentHealth.OnValueChanged += (previous, current) =>
            {
                if (current < previous)
                {
                    TriggerLocalFlash();
                }
            };

            StartCoroutine(FindAndBindButtonsRoutine());
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        AllPlayers.Remove(this);
        IsDead.OnValueChanged -= OnDeadStateChanged;

        if (IsOwner && LocalPlayerInstance == this)
        {
            LocalPlayerInstance = null;
        }

        // Only check if NOT restarting/shutting down
        if (IsServer && !isRestartingStage && NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.ShutdownInProgress)
        {
            CheckAllPlayersDeadServer();
        }
    }

    private IEnumerator FindAndBindButtonsRoutine()
    {
        int attempts = 0;
        while ((mobileReviveButton == null || mobileAttackButton == null) && attempts < 25)
        {
            if (mobileAttackButton == null)
            {
                mobileAttackButton = FindSceneButtonByTag(attackButtonTag);
            }

            if (mobileReviveButton == null)
            {
                mobileReviveButton = FindSceneButtonByTag(reviveButtonTag);
                if (mobileReviveButton != null)
                {
                    EventTrigger trigger = mobileReviveButton.gameObject.GetComponent<EventTrigger>();
                    if (trigger == null)
                    {
                        trigger = mobileReviveButton.gameObject.AddComponent<EventTrigger>();
                    }
                    trigger.triggers.Clear();

                    EventTrigger.Entry downEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                    downEntry.callback.AddListener((_) => OnReviveButtonDown());
                    trigger.triggers.Add(downEntry);

                    EventTrigger.Entry upEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
                    upEntry.callback.AddListener((_) => OnReviveButtonUp());
                    trigger.triggers.Add(upEntry);

                    EventTrigger.Entry exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener((_) => OnReviveButtonUp());
                    trigger.triggers.Add(exitEntry);

                    UpdateHUDButtons(false);
                    Debug.Log($"<color=green>[PlayerHealth]</color> ReviveButton bound successfully for Client {OwnerClientId}!");
                }
            }

            if (mobileReviveButton != null && mobileAttackButton != null)
            {
                yield break;
            }

            attempts++;
            yield return new WaitForSeconds(0.1f);
        }

        if (mobileReviveButton == null)
        {
            Debug.LogError($"[PlayerHealth] Could NOT find any Button with tag '{reviveButtonTag}'! Make sure the tag exists and is assigned to your Revive button.");
        }
    }

    private Button FindSceneButtonByTag(string targetTag)
    {
        if (string.IsNullOrEmpty(targetTag)) return null;

        try
        {
            GameObject activeObj = GameObject.FindWithTag(targetTag);
            if (activeObj != null)
            {
                Button btn = activeObj.GetComponent<Button>();
                if (btn != null) return btn;
            }
        }
        catch (UnityException)
        {
            Debug.LogError($"[PlayerHealth] Tag '{targetTag}' does not exist in Unity! Go to Tag -> Add Tag... and create '{targetTag}'.");
            return null;
        }

        Button[] allButtons = Resources.FindObjectsOfTypeAll<Button>();
        foreach (Button btn in allButtons)
        {
            if (btn == null || !btn.gameObject.scene.IsValid()) continue;
            if (btn.CompareTag(targetTag))
            {
                return btn;
            }
        }

        return null;
    }

    private void OnReviveButtonDown()
    {
        if (!IsOwner || IsDead.Value) return;

        if (holdToRevive)
        {
            mobileReviveHeld = true;
        }
        else
        {
            RequestInstantReviveServerRpc();
        }
    }

    private void OnReviveButtonUp()
    {
        mobileReviveHeld = false;
    }

    private void Update()
    {
        GameObject reviveUIToToggle = reviveUIContainer != null
            ? reviveUIContainer
            : (reviveProgressSlider != null ? reviveProgressSlider.gameObject : null);

        if (IsDead.Value)
        {
            if (soulVisual != null && soulVisual.activeSelf)
            {
                float bobOffset = Mathf.Sin(Time.time * soulFloatSpeed) * soulFloatHeight;
                soulVisual.transform.localPosition = initialSoulLocalPos + new Vector3(0f, bobOffset, 0f);
            }

            if (reviveUIToToggle != null)
            {
                reviveUIToToggle.SetActive(!defeatTriggeredThisMatch && ReviveProgress.Value > 0.01f);
            }

            if (reviveProgressSlider != null)
            {
                reviveProgressSlider.value = ReviveProgress.Value;
            }
        }
        else
        {
            if (reviveUIToToggle != null && reviveUIToToggle.activeSelf)
            {
                reviveUIToToggle.SetActive(false);
            }
        }

        if (IsOwner)
        {
            bool canReviveSomeone = !IsDead.Value && HasDeadTeammateInRange();
            UpdateHUDButtons(canReviveSomeone);

            if (!canReviveSomeone)
            {
                mobileReviveHeld = false;
            }

            if (requireButtonToRevive)
            {
                if (holdToRevive)
                {
                    bool holdingKey = Keyboard.current != null && Keyboard.current.eKey.isPressed;
                    bool wantsRevive = canReviveSomeone && (holdingKey || mobileReviveHeld);

                    if (isHoldingReviveInput.Value != wantsRevive)
                    {
                        isHoldingReviveInput.Value = wantsRevive;
                    }
                }
                else
                {
                    if (canReviveSomeone && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                    {
                        RequestInstantReviveServerRpc();
                    }
                }
            }
        }

        if (IsServer && IsDead.Value && !defeatTriggeredThisMatch && (!requireButtonToRevive || holdToRevive))
        {
            UpdateServerRevive();
        }
    }

    private bool HasDeadTeammateInRange()
    {
        foreach (PlayerHealth other in AllPlayers)
        {
            if (other == null || !other.IsSpawned || other == this) continue;
            if (!other.IsDead.Value) continue;

            if (Vector3.Distance(transform.position, other.transform.position) <= reviveRange)
            {
                return true;
            }
        }
        return false;
    }

    private void UpdateHUDButtons(bool canRevive)
    {
        if (mobileReviveButton != null)
        {
            if (hideButtonWhenOutOfRange)
            {
                if (mobileReviveButton.gameObject.activeSelf != canRevive)
                {
                    mobileReviveButton.gameObject.SetActive(canRevive);
                }
            }
            else
            {
                mobileReviveButton.interactable = canRevive;
            }
        }

        if (mobileAttackButton != null && hideAttackButtonWhenDeadOrReviving)
        {
            bool showAttack = !IsDead.Value && !canRevive;
            if (mobileAttackButton.gameObject.activeSelf != showAttack)
            {
                mobileAttackButton.gameObject.SetActive(showAttack);
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestInstantReviveServerRpc()
    {
        if (IsDead.Value || defeatTriggeredThisMatch) return;

        foreach (PlayerHealth other in AllPlayers)
        {
            if (other == null || !other.IsSpawned || other == this) continue;
            if (!other.IsDead.Value) continue;

            if (Vector3.Distance(transform.position, other.transform.position) <= reviveRange)
            {
                other.ReviveServer();
                break;
            }
        }
    }

    private void UpdateServerRevive()
    {
        bool teammateReviving = false;

        foreach (PlayerHealth other in AllPlayers)
        {
            if (other == null || !other.IsSpawned || other == this) continue;
            if (other.IsDead.Value) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist <= reviveRange)
            {
                if (!requireButtonToRevive || other.isHoldingReviveInput.Value)
                {
                    teammateReviving = true;
                    break;
                }
            }
        }

        if (teammateReviving)
        {
            ReviveProgress.Value = Mathf.Min(1f, ReviveProgress.Value + (Time.deltaTime / reviveDuration));

            if (ReviveProgress.Value >= 1f)
            {
                ReviveServer();
            }
        }
        else if (ReviveProgress.Value > 0f)
        {
            ReviveProgress.Value = Mathf.Max(0f, ReviveProgress.Value - (Time.deltaTime * 1.5f));
        }
    }

    public void ReviveServer()
    {
        if (!IsServer || defeatTriggeredThisMatch) return;

        IsDead.Value = false;
        ReviveProgress.Value = 0f;
        CurrentHealth.Value = Mathf.Max(1, Mathf.RoundToInt(MaxHealth * reviveHealthPercent));

        Debug.Log($"<color=green>[PlayerHealth]</color> Player {OwnerClientId} was REVIVED with {CurrentHealth.Value}/{MaxHealth} HP!");
    }

    private void OnDeadStateChanged(bool previous, bool current)
    {
        ApplyDeadState(current);
    }

    private void ApplyDeadState(bool dead)
    {
        if (dead)
        {
            if (hitFeedbackRoutine != null)
            {
                StopCoroutine(hitFeedbackRoutine);
                hitFeedbackRoutine = null;
            }
            ResetVisualState();

            if (characterVisual != null && characterVisual != gameObject)
            {
                characterVisual.SetActive(false);
            }

            if (characterSpriteRenderer != null)
            {
                if (characterSpriteRenderer.gameObject != gameObject)
                {
                    characterSpriteRenderer.gameObject.SetActive(false);
                }
                else
                {
                    characterSpriteRenderer.enabled = false;
                }
            }

            if (soulVisual != null)
            {
                soulVisual.SetActive(true);
            }
        }
        else
        {
            if (soulVisual != null)
            {
                soulVisual.transform.localPosition = initialSoulLocalPos;
                soulVisual.SetActive(false);
            }

            if (characterVisual != null && characterVisual != gameObject)
            {
                characterVisual.SetActive(true);
            }

            if (characterSpriteRenderer != null)
            {
                ActivateHierarchyUpToRoot(characterSpriteRenderer.transform);
                characterSpriteRenderer.enabled = true;
            }

            ResetVisualState();

            if (reviveProgressSlider != null)
            {
                reviveProgressSlider.value = 0f;
            }
        }

        if (IsOwner && playerController != null)
        {
            playerController.isMovementLocked = dead;
        }
    }

    private void ActivateHierarchyUpToRoot(Transform target)
    {
        Transform current = target;
        while (current != null && current != transform)
        {
            if (!current.gameObject.activeSelf)
            {
                current.gameObject.SetActive(true);
            }
            current = current.parent;
        }
    }

    public void TakeDamage(int damage, Vector3 knockbackForce)
    {
        if (!IsServer || IsDead.Value) return;

        CurrentHealth.Value = Mathf.Max(0, CurrentHealth.Value - damage);
        Debug.Log($"<color=red>[PlayerHealth]</color> Client {OwnerClientId} took {damage} dmg. HP: {CurrentHealth.Value}/{MaxHealth}");

        TriggerDamageFlashRpc();

        if (CurrentHealth.Value <= 0)
        {
            IsDead.Value = true;
            ReviveProgress.Value = 0f;
            Debug.Log($"[PlayerHealth] Player {OwnerClientId} died and became a Soul!");

            CheckAllPlayersDeadServer();
        }
        else
        {
            PlayHitVisualClientRpc();
        }
    }

    private void CheckAllPlayersDeadServer()
    {
        if (!IsServer || isRestartingStage || defeatTriggeredThisMatch || AllPlayers.Count == 0) return;

        PlayerHealth spawnedSender = null;

        foreach (PlayerHealth player in AllPlayers)
        {
            if (player == null || !player.IsSpawned) continue;

            if (spawnedSender == null)
                spawnedSender = player;

            if (!player.IsDead.Value)
            {
                return; // At least one teammate is still alive
            }
        }

        if (spawnedSender == null) return;

        defeatTriggeredThisMatch = true;
        Debug.Log("<color=red>[PlayerHealth]</color> ALL PLAYERS DIED! Showing Defeat Screen.");
        spawnedSender.ShowDefeatScreenClientRpc();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ShowDefeatScreenClientRpc()
    {
        if (DefeatScreenUI.Instance != null)
        {
            DefeatScreenUI.Instance.ShowDefeatScreen();
        }
        else
        {
            Debug.LogError("[PlayerHealth] DefeatScreenUI.Instance is NULL! Add DefeatScreenUI to your scene.");
        }
    }

    [Rpc(SendTo.Server)]
    public void RequestRepeatStageServerRpc()
    {
        if (!IsServer || NetworkManager.Singleton == null || isRestartingStage) return;

        isRestartingStage = true;
        defeatTriggeredThisMatch = false;
        string currentScene = SceneManager.GetActiveScene().name;

        // Clear AllPlayers BEFORE despawning so OnNetworkDespawn won't trigger Defeat again
        List<PlayerHealth> snapshot = new List<PlayerHealth>(AllPlayers);
        AllPlayers.Clear();

        foreach (PlayerHealth player in snapshot)
        {
            if (player != null && player.NetworkObject != null && player.NetworkObject.IsSpawned)
            {
                player.NetworkObject.Despawn(true);
            }
        }

        NetworkManager.Singleton.SceneManager.LoadScene(currentScene, LoadSceneMode.Single);
    }

    [Rpc(SendTo.Owner)]
    public void TriggerDamageFlashRpc()
    {
        TriggerLocalFlash();
    }

    private void TriggerLocalFlash()
    {
        if (DamageFlashUI.Instance != null)
        {
            DamageFlashUI.Instance.Flash();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayHitVisualClientRpc()
    {
        if (IsDead.Value) return;

        if (hitFeedbackRoutine != null)
        {
            StopCoroutine(hitFeedbackRoutine);
            ResetVisualState();
        }

        hitFeedbackRoutine = StartCoroutine(HitFeedbackRoutine());
    }

    private IEnumerator HitFeedbackRoutine()
    {
        float timer = 0f;

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
                spriteRenderers[i].color = hitColor;
        }

        while (timer < shakeDuration)
        {
            timer += Time.deltaTime;

            if (visualToShake != null)
            {
                float offsetX = UnityEngine.Random.Range(-1f, 1f) * shakeIntensity;
                float offsetY = UnityEngine.Random.Range(-1f, 1f) * shakeIntensity;
                visualToShake.localPosition = initialLocalPos + new Vector3(offsetX, offsetY, 0f);
            }

            yield return null;
        }

        ResetVisualState();
        hitFeedbackRoutine = null;
    }

    private void ResetVisualState()
    {
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
                spriteRenderers[i].color = originalSpriteColors[i];
        }

        if (visualToShake != null)
        {
            visualToShake.localPosition = initialLocalPos;
        }
    }
}