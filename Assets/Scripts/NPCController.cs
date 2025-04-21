using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(PhotonView))]
public class NPCController : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float patrolRadius = 20f;
    [SerializeField] private float minWaitTime = 2f;
    [SerializeField] private float maxWaitTime = 5f;

    [Header("Combat Settings")]
    [SerializeField] private float detectionRange = 30f;
    [SerializeField] private float attackRange = 10f;
    [SerializeField] private float attackCooldown = 2f;
    [SerializeField] private int damageAmount = 20;

    // Components
    private NavMeshAgent agent;
    private Animator animator;
    private PhotonView photonView;
    private NPCHealth npcHealth;
    private Vector3 startPosition;
    private bool isDead = false;
    private float nextAttackTime;
    private PlayerHealth playerHealth;
    private Transform gunTransform;
    private Rigidbody rb;
    private Vector3 networkPosition;
    private Quaternion networkRotation;

    // Animation parameter names - match the player's animator parameters
    private readonly string ANIM_HORIZONTAL = "Horizontal";
    private readonly string ANIM_VERTICAL = "Vertical";
    private readonly string ANIM_IS_RUNNING = "Running";
    private readonly string ANIM_IS_JUMPING = "IsJumping";
    private readonly string ANIM_IS_HURT = "IsHurt";
    private readonly string ANIM_IS_DEAD = "IsDead";

    private void Awake()
    {
        // Get components
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            Debug.Log("Found animator in children");
        }
        
        if (animator != null)
        {
            Debug.Log("NPC Animator found. Checking parameters:");
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                Debug.Log($"Parameter: {param.name} of type {param.type}");
            }
        }
        else
        {
            Debug.LogError("No animator found on NPC or its children!");
        }
        
        photonView = GetComponent<PhotonView>();
        npcHealth = GetComponent<NPCHealth>();
        rb = GetComponent<Rigidbody>();
        playerHealth = GetComponent<PlayerHealth>();
        
        // Store starting position
        startPosition = transform.position;

        // Configure NavMeshAgent
        if (agent != null)
        {
            agent.speed = moveSpeed;
            agent.stoppingDistance = 1f;
            agent.autoBraking = true;
        }

        // Configure Rigidbody for NPC
        if (rb != null)
        {
            rb.isKinematic = true; // Let NavMeshAgent handle movement
            rb.useGravity = true;
        }

        networkPosition = transform.position;
        networkRotation = transform.rotation;

        // Subscribe to health events
        if (npcHealth != null)
        {
            npcHealth.OnDamageReceived += HandleDamage;
            npcHealth.OnDeath += HandleDeath;
        }

        // Subscribe to player health events
        if (playerHealth != null)
        {
            playerHealth.RespawnEvent += OnNPCDeath;
        }
    }

    private void Start()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            VerifyAnimatorSetup();
            StartCoroutine(AIRoutine());
        }
    }

    private void Update()
    {
        // Only update animations and movement on MasterClient
        if (!PhotonNetwork.IsMasterClient || isDead) return;

        // Update animation based on movement
        if (animator != null && agent != null)
        {
            // Calculate movement direction and speed
            Vector3 velocity = agent.velocity;
            float speed = velocity.magnitude;
            
            // Convert world space velocity to local space direction
            Vector3 localVelocity = transform.InverseTransformDirection(velocity);
            float forward = localVelocity.z;
            float sideways = localVelocity.x;

            // Set animator parameters to match player's animation system
            photonView.RPC("SyncAnimationParameters", RpcTarget.All, 
                sideways / agent.speed,
                forward / agent.speed,
                speed > agent.speed * 0.5f);
        }

        // Look for nearby players
        FindAndAttackPlayer();
    }

    private IEnumerator AIRoutine()
    {
        while (!isDead)
        {
            if (!agent.pathStatus.Equals(NavMeshPathStatus.PathInvalid))
            {
                // Generate random position within patrol radius
                Vector3 randomPos = startPosition + Random.insideUnitSphere * patrolRadius;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(randomPos, out hit, patrolRadius, NavMesh.AllAreas))
                {
                    // Set destination
                    agent.SetDestination(hit.position);

                    // Wait until we reach the destination or get close enough
                    while (agent.pathStatus == NavMeshPathStatus.PathPartial)
                    {
                        yield return new WaitForSeconds(0.1f);
                    }
                }
            }

            // Random wait time between movements
            float waitTime = Random.Range(minWaitTime, maxWaitTime);
            yield return new WaitForSeconds(waitTime);
        }
    }

    private void FindAndAttackPlayer()
    {
        if (Time.time < nextAttackTime) return;

        Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRange);
        foreach (Collider col in colliders)
        {
            if (col.CompareTag("Player"))
            {
                float distance = Vector3.Distance(transform.position, col.transform.position);
                
                // If player is within attack range
                if (distance <= attackRange)
                {
                    // Face the player
                    Vector3 directionToPlayer = (col.transform.position - transform.position).normalized;
                    transform.rotation = Quaternion.LookRotation(directionToPlayer);

                    // Attack
                    Attack(col.gameObject);
                    nextAttackTime = Time.time + attackCooldown;
                    break;
                }
                // If player is detected but not in attack range, move towards them
                else if (distance <= detectionRange)
                {
                    agent.SetDestination(col.transform.position);
                }
            }
        }
    }

    private void Attack(GameObject player)
    {
        // Play attack animation
        if (animator != null)
        {
            animator.SetTrigger(ANIM_IS_HURT);
        }

        // Apply damage to player
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        if (playerHealth != null && playerHealth.photonView != null)
        {
            playerHealth.photonView.RPC("TakeDamage", RpcTarget.All, damageAmount, "NPC");
        }
    }

    private void HandleDamage()
    {
        if (isDead) return;

        if (animator != null)
        {
            animator.SetTrigger(ANIM_IS_HURT);
        }
    }

    private void HandleDeath()
    {
        if (isDead) return;
        isDead = true;

        Debug.Log("NPC Death - Playing death animation");

        // Stop movement
        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        // Disable NPC controller behaviors
        enabled = false;

        // Play death animation
        if (animator != null)
        {
            // Reset other animation parameters
            animator.SetFloat(ANIM_HORIZONTAL, 0f);
            animator.SetFloat(ANIM_VERTICAL, 0f);
            animator.SetBool(ANIM_IS_RUNNING, false);
            
            // Trigger death animation
            animator.SetBool(ANIM_IS_DEAD, true);
            animator.SetTrigger("Die");
            
            Debug.Log("Death animation triggered");
        }

        // Disable colliders
        foreach (Collider col in GetComponents<Collider>())
        {
            col.enabled = false;
        }

        // Make rigidbody kinematic to prevent physics interactions
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
        }

        // Start sinking after a delay
        StartCoroutine(StartSinking());

        // Start destruction sequence
        if (PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(DestroyAfterDelay());
        }
    }

    private IEnumerator StartSinking()
    {
        yield return new WaitForSeconds(2f); // Wait for death animation to play
        
        float sinkDuration = 2f;
        float elapsedTime = 0f;
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.down * 2f; // Sink 2 units down

        while (elapsedTime < sinkDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / sinkDuration;
            transform.position = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }
    }

    private IEnumerator DestroyAfterDelay()
    {
        // Wait for death animation and sinking to complete
        yield return new WaitForSeconds(5f);
        
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.Destroy(gameObject);
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Send position, rotation, and velocity
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(agent != null ? agent.velocity : Vector3.zero);
            stream.SendNext(agent != null ? agent.destination : transform.position);
        }
        else
        {
            // Receive position, rotation, and velocity
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            Vector3 receivedVelocity = (Vector3)stream.ReceiveNext();
            Vector3 receivedDestination = (Vector3)stream.ReceiveNext();

            // Apply received values to non-MasterClient NPCs
            if (!PhotonNetwork.IsMasterClient)
            {
                // Smoothly move to the network position
                transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * 10f);
                transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation, Time.deltaTime * 10f);

                // Update NavMeshAgent if available
                if (agent != null && agent.enabled)
                {
                    agent.velocity = receivedVelocity;
                    if (Vector3.Distance(agent.destination, receivedDestination) > 0.1f)
                    {
                        agent.destination = receivedDestination;
                    }
                }
            }
        }
    }

    private void VerifyAnimatorSetup()
    {
        if (animator == null)
        {
            Debug.LogError("No Animator component found!");
            return;
        }

        Debug.Log("Checking NPC animator parameters:");
        bool hasHorizontal = false;
        bool hasVertical = false;
        bool hasRunning = false;
        bool hasJumping = false;
        bool hasHurt = false;
        bool hasDead = false;

        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            Debug.Log($"Found parameter: {param.name} ({param.type})");
            if (param.name == ANIM_HORIZONTAL) hasHorizontal = true;
            if (param.name == ANIM_VERTICAL) hasVertical = true;
            if (param.name == ANIM_IS_RUNNING) hasRunning = true;
            if (param.name == ANIM_IS_JUMPING) hasJumping = true;
            if (param.name == ANIM_IS_HURT) hasHurt = true;
            if (param.name == ANIM_IS_DEAD) hasDead = true;
        }

        if (!hasHorizontal) Debug.LogError($"Missing {ANIM_HORIZONTAL} parameter");
        if (!hasVertical) Debug.LogError($"Missing {ANIM_VERTICAL} parameter");
        if (!hasRunning) Debug.LogError($"Missing {ANIM_IS_RUNNING} parameter");
        if (!hasJumping) Debug.LogError($"Missing {ANIM_IS_JUMPING} parameter");
        if (!hasHurt) Debug.LogError($"Missing {ANIM_IS_HURT} parameter");
        if (!hasDead) Debug.LogError($"Missing {ANIM_IS_DEAD} parameter");
    }

    void OnNPCDeath(float respawnTime)
    {
        if (isDead) return;
        isDead = true;

        // Stop movement
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        // Play death animation
        if (animator != null)
        {
            animator.SetTrigger("Die");
        }

        // Disable colliders
        foreach (Collider col in GetComponents<Collider>())
        {
            col.enabled = false;
        }

        // Start destruction sequence
        if (PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(DestroyAfterDelay());
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Draw detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Draw attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Draw patrol radius
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(startPosition, patrolRadius);
    }

    [PunRPC]
    private void SyncAnimationParameters(float horizontal, float vertical, bool isRunning)
    {
        if (animator != null)
        {
            animator.SetFloat(ANIM_HORIZONTAL, horizontal);
            animator.SetFloat(ANIM_VERTICAL, vertical);
            animator.SetBool(ANIM_IS_RUNNING, isRunning);
        }
    }

    [PunRPC]
    private void PlayAttackAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(ANIM_IS_HURT);
        }
    }

    [PunRPC]
    private void PlayDeathAnimation()
    {
        if (animator != null)
        {
            animator.SetBool(ANIM_IS_DEAD, true);
            animator.SetTrigger("Die");
        }
    }
}
