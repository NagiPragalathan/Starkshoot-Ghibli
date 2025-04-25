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
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float patrolRadius = 20f;
    [SerializeField] private float minWaitTime = 1f;
    [SerializeField] private float maxWaitTime = 3f;

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
    private bool isMoving = false;
    private bool isInitialized = false;
    private int npcViewID;

    // Animation parameter hashes (faster than strings)
    private int hashHorizontal;
    private int hashVertical;
    private int hashRunning;
    private int hashIsDead;
    private int hashIsHurt;
    private int hashDieTrigger;
    private int hashShootTrigger;

    private NPCTpsGun npcGun;
    private Transform currentTarget;

    private void Awake()
    {
        // Get components
        agent = GetComponent<NavMeshAgent>();
        photonView = GetComponent<PhotonView>();
        npcHealth = GetComponent<NPCHealth>();
        npcViewID = photonView.ViewID;
        
        // Get or find animator
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError($"NPC {npcViewID} cannot find Animator component!");
            }
        }
        
        // Cache animation parameter hashes for better performance
        hashHorizontal = Animator.StringToHash("Horizontal");
        hashVertical = Animator.StringToHash("Vertical");
        hashRunning = Animator.StringToHash("Running");
        hashIsDead = Animator.StringToHash("IsDead");
        hashIsHurt = Animator.StringToHash("IsHurt");
        hashDieTrigger = Animator.StringToHash("Die");
        hashShootTrigger = Animator.StringToHash("Shoot");
        
        // Store starting position
        startPosition = transform.position;

        // Set proper scale (1.5x is larger than player)
        transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        
        Debug.Log($"NPC {npcViewID} initialized with animator: {(animator != null ? "Found" : "Missing")}");
    }

    private void Start()
    {
        if (!isInitialized)
        {
            InitializeNPC();
        }
        npcGun = GetComponentInChildren<NPCTpsGun>();
    }

    public void InitializeNPC()
    {
        // Configure NavMeshAgent for proper movement
        if (agent != null)
        {
            agent.speed = moveSpeed;
            agent.stoppingDistance = 1f;
            agent.autoBraking = true;
            agent.acceleration = 12f;
            agent.angularSpeed = 180f;
            agent.avoidancePriority = Random.Range(20, 80); // Different priorities to avoid stacking
            
            // Ensure agent is on the NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 1.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }
        
        // Initialize animator parameters
        if (animator != null)
        {
            animator.SetFloat(hashHorizontal, 0f);
            animator.SetFloat(hashVertical, 0f);
            animator.SetBool(hashRunning, false);
            animator.SetBool(hashIsDead, false);
            
            // Force animation update
            animator.Rebind();
            animator.Update(0f);
            Debug.Log($"NPC {npcViewID} animator initialized");
        }
        
        // For master client, start AI routines
        if (PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(AIRoutine());
            StartCoroutine(FindPlayerRoutine());
        }
        
        isInitialized = true;
        Debug.Log($"NPC {npcViewID} fully initialized. Is Master: {PhotonNetwork.IsMasterClient}");
    }

    private void Update()
    {
        if (isDead) return;

        // Master client controls the NPC movement and behavior
        if (PhotonNetwork.IsMasterClient)
        {
            // Update animation based on movement
            if (animator != null && agent != null)
            {
                Vector3 velocity = agent.velocity;
                float speed = velocity.magnitude;
                
                Vector3 localVelocity = transform.InverseTransformDirection(velocity);
                float forward = localVelocity.z;
                float sideways = localVelocity.x;

                // Set animator parameters locally
                animator.SetFloat(hashHorizontal, sideways / agent.speed);
                animator.SetFloat(hashVertical, forward / agent.speed);
                animator.SetBool(hashRunning, speed > 0.1f);
                
                // Update movement status
                isMoving = speed > 0.1f;
                
                // Sync animation parameters
                if (photonView.IsMine)
                {
                    photonView.RPC("SyncAnimationParameters", RpcTarget.Others, 
                        sideways / agent.speed, 
                        forward / agent.speed, 
                        speed > 0.1f);
                }
            }
        }
    }

    private IEnumerator AIRoutine()
    {
        Debug.Log($"NPC {npcViewID} started AI routine");
        
        // Small initial delay to ensure everything is set up
        yield return new WaitForSeconds(Random.Range(0.1f, 0.5f));
        
        while (!isDead)
        {
            if (agent != null && agent.isOnNavMesh && !isMoving)
            {
                // Generate random position within patrol radius
                Vector3 randomPos = startPosition + Random.insideUnitSphere * patrolRadius;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(randomPos, out hit, patrolRadius, NavMesh.AllAreas))
                {
                    // Set destination and mark as moving
                    agent.SetDestination(hit.position);
                    isMoving = true;
                    Debug.Log($"NPC {npcViewID} moving to: {hit.position}");
                    
                    // Wait until we reach the destination or get close enough
                    float timeout = 0;
                    while (agent.pathPending || 
                          (agent.hasPath && agent.remainingDistance > agent.stoppingDistance) && 
                          timeout < 10f)
                    {
                        timeout += 0.1f;
                        yield return new WaitForSeconds(0.1f);
                    }
                    
                    isMoving = false;
                }
            }

            // Random wait time between movements
            float waitTime = Random.Range(minWaitTime, maxWaitTime);
            yield return new WaitForSeconds(waitTime);
        }
    }
    
    private IEnumerator FindPlayerRoutine()
    {
        while (!isDead)
        {
            FindAndAttackPlayer();
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void FindAndAttackPlayer()
    {
        if (Time.time < nextAttackTime) return;

        Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRange);
        bool foundPlayer = false;
        
        foreach (Collider col in colliders)
        {
            if (col.CompareTag("Player"))
            {
                // Check if player is dead first
                PlayerHealth playerHealth = col.GetComponent<PlayerHealth>();
                if (playerHealth != null && playerHealth.IsDead())
                {
                    // Skip dead players
                    continue;
                }

                foundPlayer = true;
                float distance = Vector3.Distance(transform.position, col.transform.position);
                
                // If player is within attack range
                if (distance <= attackRange)
                {
                    // Stop the NPC while shooting
                    if (agent != null)
                    {
                        agent.isStopped = true;
                        isMoving = false;
                    }

                    // Face the player
                    Vector3 directionToPlayer = (col.transform.position - transform.position).normalized;
                    transform.rotation = Quaternion.LookRotation(directionToPlayer);

                    if (npcGun != null)
                    {
                        npcGun.UpdateAiming(col.transform.position);
                        // Only shoot if we're properly aimed AND player is alive
                        if (IsAimedAtTarget(col.transform.position) && !playerHealth.IsDead())
                        {
                            Attack(col.gameObject);
                            npcGun.Shoot();
                            nextAttackTime = Time.time + attackCooldown;
                        }
                    }
                    break;
                }
                // If player is detected but not in attack range, move towards them
                else if (distance <= detectionRange)
                {
                    if (agent != null)
                    {
                        agent.isStopped = false;
                        agent.SetDestination(col.transform.position);
                        isMoving = true;
                    }
                    if (npcGun != null)
                    {
                        npcGun.UpdateAiming(col.transform.position);
                    }
                }
            }
        }
        
        if (!foundPlayer)
        {
            if (agent != null)
            {
                agent.isStopped = false;
            }
            if (npcGun != null)
            {
                npcGun.ResetAiming();
            }
            if (isMoving && !agent.hasPath)
            {
                isMoving = false;
            }
        }
    }

    private bool IsAimedAtTarget(Vector3 targetPosition)
    {
        Vector3 directionToTarget = (targetPosition - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, directionToTarget);
        return angle < 30f; // Adjust this value to control aim accuracy
    }

    private bool IsPlayerAlive(GameObject player)
    {
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        return playerHealth != null && !playerHealth.IsDead();
    }

    private void Attack(GameObject player)
    {
        if (!IsPlayerAlive(player)) return;

        // Play attack/shoot animation
        photonView.RPC("PlayShootAnimation", RpcTarget.All);

        // Apply damage to player
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        if (playerHealth != null && playerHealth.photonView != null)
        {
            playerHealth.photonView.RPC("TakeDamage", RpcTarget.All, damageAmount, "NPC");
        }
    }

    [PunRPC]
    private void PlayShootAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(hashShootTrigger);
        }
    }
    
    [PunRPC]
    private void PlayHurtAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(hashIsHurt);
        }
    }
    
    [PunRPC]
    private void PlayDeathAnimation()
    {
        if (animator != null)
        {
            animator.SetBool(hashIsDead, true);
            animator.SetTrigger(hashDieTrigger);
        }
    }
    
    [PunRPC]
    private void SyncAnimationParameters(float horizontal, float vertical, bool isRunning)
    {
        if (animator != null)
        {
            animator.SetFloat(hashHorizontal, horizontal);
            animator.SetFloat(hashVertical, vertical);
            animator.SetBool(hashRunning, isRunning);
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Master client sends data
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            
            // Send animation parameters
            if (animator != null)
            {
                stream.SendNext(animator.GetFloat(hashHorizontal));
                stream.SendNext(animator.GetFloat(hashVertical));
                stream.SendNext(animator.GetBool(hashRunning));
            }
            else
            {
                stream.SendNext(0f);
                stream.SendNext(0f);
                stream.SendNext(false);
            }
            
            stream.SendNext(isDead);
            stream.SendNext(isMoving);
            
            // Send agent data if available
            if (agent != null && agent.isOnNavMesh)
            {
                stream.SendNext(agent.destination);
                stream.SendNext(agent.velocity.magnitude);
            }
            else
            {
                stream.SendNext(transform.position);
                stream.SendNext(0f);
            }
        }
        else
        {
            // Clients receive data
            Vector3 networkPosition = (Vector3)stream.ReceiveNext();
            Quaternion networkRotation = (Quaternion)stream.ReceiveNext();
            
            // Receive animation parameters
            float networkHorizontal = (float)stream.ReceiveNext();
            float networkVertical = (float)stream.ReceiveNext();
            bool networkIsRunning = (bool)stream.ReceiveNext();
            
            // Other state data
            isDead = (bool)stream.ReceiveNext();
            isMoving = (bool)stream.ReceiveNext();
            
            // Agent data
            Vector3 networkDestination = (Vector3)stream.ReceiveNext();
            float networkSpeed = (float)stream.ReceiveNext();
            
            // Update client-side animation
            if (animator != null)
            {
                animator.SetFloat(hashHorizontal, networkHorizontal);
                animator.SetFloat(hashVertical, networkVertical);
                animator.SetBool(hashRunning, networkIsRunning);
            }
            
            // Update position and rotation
            transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation, Time.deltaTime * 10f);
            
            // Update agent on client if available
            if (agent != null && agent.isOnNavMesh)
            {
                // Update agent's position
                agent.nextPosition = transform.position;
                
                // Update destination if it's different
                if (Vector3.Distance(agent.destination, networkDestination) > 1f)
                {
                    agent.SetDestination(networkDestination);
                }
                
                // Match the agent's speed
                agent.speed = networkSpeed;
            }
        }
    }

    // Handle damage and death
    public void HandleDamage()
    {
        if (isDead) return;
        photonView.RPC("PlayHurtAnimation", RpcTarget.All);
    }

    public void HandleDeath()
    {
        if (isDead) return;
        isDead = true;

        // Stop movement
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        // Stop all coroutines
        StopAllCoroutines();
        
        // Play death animation
        photonView.RPC("PlayDeathAnimation", RpcTarget.All);
    }
}
