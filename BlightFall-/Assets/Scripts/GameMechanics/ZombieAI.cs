using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(UnityEngine.CharacterController))]
public class ZombieAI : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float gravity = -9.81f;

    private Transform targetPlayer;
    private UnityEngine.CharacterController controller;
    private Vector3 velocity;

    private void Awake()
    {
        controller = GetComponent<UnityEngine.CharacterController>();
    }

    private void Update()
    {
        // AI tracking and movement run exclusively on the SERVER
        if (!IsServer) return;

        if (targetPlayer == null)
        {
            FindClosestPlayer();
            return;
        }

        // 1. Move towards player along the X-Z plane
        Vector3 direction = (targetPlayer.position - transform.position);
        direction.y = 0f;

        Vector3 moveDir = direction.normalized;

        // Use CharacterController.Move so it actually collides with trees and obstacles!
        controller.Move(moveDir * moveSpeed * Time.deltaTime);

        // 2. Handle gravity to keep them grounded
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

    private void FindClosestPlayer()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        float shortestDistance = Mathf.Infinity;
        Transform closest = null;

        foreach (GameObject player in players)
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance < shortestDistance)
            {
                shortestDistance = distance;
                closest = player.transform;
            }
        }

        targetPlayer = closest;
    }
}