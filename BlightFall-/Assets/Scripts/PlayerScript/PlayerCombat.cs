using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PlayerCombat : NetworkBehaviour
{
    [Header("Knight - Heavy Slash")]
    [SerializeField] private int knightSlashDamage = 35;
    [SerializeField] private float knightKnockbackForce = 22f;
    [SerializeField] private float knightSlashRadius = 2.2f;
    [SerializeField] private float knightSlashDistance = 1.8f;
    [SerializeField] private float knightCooldown = 1.0f;

    [Header("Rogue - Fast Dagger Stab")]
    [SerializeField] private int rogueStabDamage = 25;
    [SerializeField] private float rogueKnockbackForce = 4f;
    [SerializeField] private float rogueStabRadius = 1.2f;
    [SerializeField] private float rogueStabDistance = 1.4f;
    [SerializeField] private float rogueCooldown = 0.35f;
    [SerializeField] private bool enableBackstabCrit = true;

    [Header("Arbalist - Standard Crossbow Bolt")]
    [SerializeField] private int arbalistBoltDamage = 30;
    [SerializeField] private float arbalistBoltSpeed = 18f;
    [SerializeField] private float arbalistBoltRange = 14f;
    [SerializeField] private float arbalistCooldown = 0.7f;
    [SerializeField] private float arbalistKnockbackForce = 8f;

    private PlayerController playerController;
    private float lastAttackTime = -99f;
    private Button mobileAttackButton;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            StartCoroutine(FindAndBindAttackButtonRoutine());
        }
    }

    // Coroutine ensures the client finds the button even if UI takes a frame to initialize
    private IEnumerator FindAndBindAttackButtonRoutine()
    {
        int attempts = 0;
        while (mobileAttackButton == null && attempts < 20)
        {
            GameObject btnObj = GameObject.FindWithTag("AttackButton");
            if (btnObj != null)
            {
                mobileAttackButton = btnObj.GetComponent<Button>();
                if (mobileAttackButton != null)
                {
                    mobileAttackButton.onClick.RemoveAllListeners();
                    mobileAttackButton.onClick.AddListener(TriggerAttack);
                    Debug.Log($"<color=green>[PlayerCombat]</color> AttackButton bound successfully for Client {OwnerClientId}!");
                    yield break;
                }
            }

            attempts++;
            yield return new WaitForSeconds(0.1f);
        }

        if (mobileAttackButton == null)
        {
            Debug.LogError("[PlayerCombat] Could NOT find GameObject with tag 'AttackButton' on Client!");
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

#if UNITY_EDITOR
        // Test in editor with [J] key
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.jKey.wasPressedThisFrame)
        {
            TriggerAttack();
        }
#endif
    }

    public void TriggerAttack()
    {
        if (!IsOwner) return;
        if (playerController == null || playerController.isMovementLocked) return;

        Vector3 attackDir = playerController.LastFacingDirection;
        attackDir.y = 0f;
        if (attackDir.sqrMagnitude < 0.001f) attackDir = Vector3.forward;
        attackDir = attackDir.normalized;

        Vector3 myCurrentPosition = transform.position;

        Debug.Log($"<color=cyan>[PlayerCombat]</color> Client {OwnerClientId} attacking at {myCurrentPosition} as {playerController.Role}");

        // 1. KNIGHT
        if (playerController.Role == CharacterRole.Knight)
        {
            if (Time.time < lastAttackTime + knightCooldown) return;
            lastAttackTime = Time.time;

            // Pass current position across the network!
            ExecuteKnightSlashServerRpc(myCurrentPosition, attackDir);
        }
        // 2. ROGUE
        else if (playerController.Role == CharacterRole.Rogue)
        {
            if (Time.time < lastAttackTime + rogueCooldown) return;
            lastAttackTime = Time.time;

            ExecuteRogueStabServerRpc(myCurrentPosition, attackDir);
        }
        // 3. ARBALIST
        else if (playerController.Role == CharacterRole.Arbalist)
        {
            if (Time.time < lastAttackTime + arbalistCooldown) return;
            lastAttackTime = Time.time;

            ExecuteArbalistShotServerRpc(myCurrentPosition, attackDir);
        }
    }

    // ==========================================
    //            KNIGHT HEAVY SLASH
    // ==========================================
    [Rpc(SendTo.Server)]
    public void ExecuteKnightSlashServerRpc(Vector3 clientPos, Vector3 direction)
    {
        // Use clientPos so it happens where the client is actually standing
        Vector3 slashCenter = clientPos + (direction * knightSlashDistance) + Vector3.up;
        SpawnKnightSlashVisualClientRpc(slashCenter, direction);

        Collider[] hits = Physics.OverlapSphere(slashCenter, knightSlashRadius);

        foreach (Collider hit in hits)
        {
            if (hit.transform.root == transform.root) continue; // Don't hit yourself

            IDamageable target = hit.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                Vector3 knockbackDir = (hit.transform.position - clientPos).normalized;
                knockbackDir.y = 0;
                knockbackDir = knockbackDir.normalized * knightKnockbackForce;

                target.TakeDamage(knightSlashDamage, knockbackDir);
            }
        }
    }

    // ==========================================
    //            ROGUE DAGGER STAB
    // ==========================================
    [Rpc(SendTo.Server)]
    public void ExecuteRogueStabServerRpc(Vector3 clientPos, Vector3 direction)
    {
        Vector3 stabCenter = clientPos + (direction * rogueStabDistance) + Vector3.up;
        SpawnRogueStabVisualClientRpc(clientPos + Vector3.up, direction);

        Collider[] hits = Physics.OverlapSphere(stabCenter, rogueStabRadius);

        foreach (Collider hit in hits)
        {
            if (hit.transform.root == transform.root) continue;

            IDamageable target = hit.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                int finalDamage = rogueStabDamage;

                ZombieAI zombie = hit.GetComponentInParent<ZombieAI>();
                if (enableBackstabCrit && zombie != null)
                {
                    float dotProduct = Vector3.Dot(direction, zombie.FacingDirection);
                    if (dotProduct > 0.35f)
                    {
                        finalDamage *= 2;
                        Debug.Log("<color=magenta>[Rogue]</color> BACKSTAB CRITICAL HIT! 50 Damage!");
                    }
                }

                Vector3 knockbackDir = direction * rogueKnockbackForce;
                knockbackDir.y = 0;

                target.TakeDamage(finalDamage, knockbackDir);
            }
        }
    }

    // ==========================================
    //         ARBALIST CROSSBOW BOLT
    // ==========================================
    [Rpc(SendTo.Server)]
    public void ExecuteArbalistShotServerRpc(Vector3 clientPos, Vector3 direction)
    {
        Vector3 spawnPos = clientPos + Vector3.up + (direction * 1.0f);
        SpawnBoltVisualClientRpc(spawnPos, direction);

        RaycastHit[] hits = Physics.RaycastAll(spawnPos, direction, arbalistBoltRange);

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.root == transform.root) continue;

            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                Vector3 knockback = direction * arbalistKnockbackForce;
                knockback.y = 0;
                target.TakeDamage(arbalistBoltDamage, knockback);
                break;
            }
        }
    }

    // ==========================================
    //            PROTOTYPE VISUALS
    // ==========================================
    [Rpc(SendTo.ClientsAndHost)]
    public void SpawnKnightSlashVisualClientRpc(Vector3 pos, Vector3 dir)
    {
        StartCoroutine(SlashVisualRoutine(pos, dir));
    }

    [Rpc(SendTo.ClientsAndHost)]
    public void SpawnRogueStabVisualClientRpc(Vector3 origin, Vector3 dir)
    {
        StartCoroutine(StabVisualRoutine(origin, dir));
    }

    [Rpc(SendTo.ClientsAndHost)]
    public void SpawnBoltVisualClientRpc(Vector3 startPos, Vector3 dir)
    {
        StartCoroutine(BoltVisualRoutine(startPos, dir));
    }

    private IEnumerator SlashVisualRoutine(Vector3 pos, Vector3 dir)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "SlashArc";
        Destroy(visual.GetComponent<Collider>());

        visual.transform.position = pos;
        visual.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        visual.transform.localScale = new Vector3(3f, 0.15f, 0.8f);

        var renderer = visual.GetComponent<Renderer>();
        renderer.material.color = Color.white;

        float timer = 0f;
        while (timer < 0.15f)
        {
            timer += Time.deltaTime;
            visual.transform.localScale += new Vector3(2f, 0f, 1f) * Time.deltaTime * 10f;
            yield return null;
        }

        Destroy(visual);
    }

    private IEnumerator StabVisualRoutine(Vector3 origin, Vector3 dir)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "DaggerThrust";
        Destroy(visual.GetComponent<Collider>());

        visual.transform.localScale = new Vector3(0.2f, 0.1f, 1.6f);
        visual.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        visual.transform.position = origin + (dir * 0.8f);

        var renderer = visual.GetComponent<Renderer>();
        renderer.material.color = new Color(0.85f, 0.3f, 1f);

        float timer = 0f;
        while (timer < 0.08f)
        {
            timer += Time.deltaTime;
            visual.transform.position += dir * Time.deltaTime * 8f;
            yield return null;
        }

        Destroy(visual);
    }

    private IEnumerator BoltVisualRoutine(Vector3 startPos, Vector3 dir)
    {
        GameObject bolt = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bolt.name = "CrossbowBolt";
        Destroy(bolt.GetComponent<Collider>());

        bolt.transform.position = startPos;
        bolt.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        bolt.transform.localScale = new Vector3(0.12f, 0.12f, 1.2f);

        var renderer = bolt.GetComponent<Renderer>();
        renderer.material.color = new Color(1f, 0.8f, 0.2f);

        float distanceTraveled = 0f;
        while (distanceTraveled < arbalistBoltRange)
        {
            float step = arbalistBoltSpeed * Time.deltaTime;
            bolt.transform.position += dir * step;
            distanceTraveled += step;
            yield return null;
        }

        Destroy(bolt);
    }
}