using UnityEngine;
using Photon.Pun;

public class CloudMovement : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Position References")]
    [SerializeField] private GameObject startPointObject;
    [SerializeField] private GameObject endPointObject;

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float waitTimeAtEnds = 2f;
    [SerializeField] private float networkSmoothTime = 0.1f;

    private Vector3 startPosition;
    private Vector3 endPosition;
    private Vector3 networkPosition;
    private Vector3 velocity;
    private bool movingToEnd = true;
    private float waitTimer = 0f;

    private void Start()
    {
        if (startPointObject == null || endPointObject == null)
        {
            Debug.LogError("Start or End point objects not set for cloud! Please assign them in the inspector.");
            enabled = false;
            return;
        }

        // Get positions from the GameObjects
        startPosition = startPointObject.transform.position;
        endPosition = endPointObject.transform.position;

        // Set initial position
        transform.position = startPosition;
        networkPosition = startPosition;

        Debug.Log($"Cloud initialized - Start: {startPosition}, End: {endPosition}");
    }

    private void Update()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            UpdateMasterClient();
        }
        else
        {
            UpdateClient();
        }
    }

    private void UpdateMasterClient()
    {
        if (waitTimer > 0)
        {
            waitTimer -= Time.deltaTime;
            return;
        }

        Vector3 targetPosition = movingToEnd ? endPosition : startPosition;
        
        // Calculate smooth movement
        float step = moveSpeed * Time.deltaTime;
        Vector3 newPosition = Vector3.MoveTowards(transform.position, targetPosition, step);
        
        // Apply movement with route clamping
        Vector3 routeDirection = (endPosition - startPosition).normalized;
        float distanceAlongRoute = Vector3.Dot(newPosition - startPosition, routeDirection);
        float totalRouteLength = Vector3.Distance(startPosition, endPosition);
        distanceAlongRoute = Mathf.Clamp(distanceAlongRoute, 0f, totalRouteLength);
        
        transform.position = startPosition + (routeDirection * distanceAlongRoute);
        networkPosition = transform.position;

        if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
        {
            movingToEnd = !movingToEnd;
            waitTimer = waitTimeAtEnds;
            Debug.Log($"Cloud reached {(movingToEnd ? "end" : "start")} position");
        }
    }

    private void UpdateClient()
    {
        transform.position = Vector3.SmoothDamp(
            transform.position,
            networkPosition,
            ref velocity,
            networkSmoothTime,
            Mathf.Infinity,
            Time.deltaTime
        );
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(movingToEnd);
            stream.SendNext(waitTimer);
        }
        else
        {
            networkPosition = (Vector3)stream.ReceiveNext();
            movingToEnd = (bool)stream.ReceiveNext();
            waitTimer = (float)stream.ReceiveNext();
        }
    }

    private void OnDrawGizmos()
    {
        if (startPointObject != null && endPointObject != null)
        {
            Gizmos.color = Color.cyan;
            Vector3 start = Application.isPlaying ? startPosition : startPointObject.transform.position;
            Vector3 end = Application.isPlaying ? endPosition : endPointObject.transform.position;
            
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(start, 1f);
            Gizmos.DrawWireSphere(end, 1f);
        }
    }
} 