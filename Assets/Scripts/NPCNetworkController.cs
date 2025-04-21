using UnityEngine;
using Photon.Pun;
using UnityEngine.AI;

[RequireComponent(typeof(PhotonView))]
[RequireComponent(typeof(NavMeshAgent))]
public class NPCNetworkController : MonoBehaviourPunCallbacks, IPunObservable
{
    private NavMeshAgent agent;
    private PhotonView photonView;
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private Vector3 targetPosition;
    private float lastNetworkPositionUpdate;
    private const float NETWORK_SMOOTHING = 10f;
    private const float POSITION_THRESHOLD = 0.5f;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        photonView = GetComponent<PhotonView>();
        
        // Initialize network variables
        networkPosition = transform.position;
        networkRotation = transform.rotation;
        targetPosition = transform.position;
        lastNetworkPositionUpdate = Time.time;

        // Configure NavMeshAgent for network play
        if (!PhotonNetwork.IsMasterClient)
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
        }
    }

    void Update()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            // Master client handles actual pathfinding and movement
            if (agent.isOnNavMesh && !agent.hasPath)
            {
                SetNewRandomDestination();
            }
        }
        else
        {
            // Clients interpolate to the network position
            if (!agent.isOnNavMesh) return;

            transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * NETWORK_SMOOTHING);
            transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation, Time.deltaTime * NETWORK_SMOOTHING);
        }
    }

    void SetNewRandomDestination()
    {
        // Get a random point on the NavMesh
        Vector3 randomDirection = Random.insideUnitSphere * 20f;
        randomDirection += transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, 20f, NavMesh.AllAreas))
        {
            targetPosition = hit.position;
            agent.SetDestination(targetPosition);
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Send position, rotation, and target
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(targetPosition);
        }
        else
        {
            // Receive position, rotation, and target
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            targetPosition = (Vector3)stream.ReceiveNext();
            
            // Update the last network position time
            lastNetworkPositionUpdate = Time.time;

            // Update the agent's destination if we're not the master
            if (!PhotonNetwork.IsMasterClient && agent.isOnNavMesh)
            {
                agent.SetDestination(targetPosition);
            }
        }
    }
}
