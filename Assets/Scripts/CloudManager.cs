using UnityEngine;
using Photon.Pun;

public class CloudManager : MonoBehaviourPunCallbacks
{
    [System.Serializable]
    public class CloudRoute
    {
        public GameObject startPoint;
        public GameObject endPoint;
    }

    [SerializeField] private GameObject cloudPrefab;
    [SerializeField] private CloudRoute[] cloudRoutes;
    
    private void Start()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            SpawnClouds();
        }
    }

    private void SpawnClouds()
    {
        foreach (CloudRoute route in cloudRoutes)
        {
            if (route.startPoint == null || route.endPoint == null)
            {
                Debug.LogError("Cloud route is missing start or end point!");
                continue;
            }

            // Spawn cloud at start position
            GameObject cloud = PhotonNetwork.Instantiate(cloudPrefab.name, 
                route.startPoint.transform.position, 
                Quaternion.identity);

            // Configure cloud movement
            CloudMovement cloudMove = cloud.GetComponent<CloudMovement>();
            if (cloudMove != null)
            {
                // Set references through reflection since they're private
                var startPointField = cloudMove.GetType().GetField("startPointObject", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                    
                var endPointField = cloudMove.GetType().GetField("endPointObject", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);

                if (startPointField != null && endPointField != null)
                {
                    startPointField.SetValue(cloudMove, route.startPoint);
                    endPointField.SetValue(cloudMove, route.endPoint);
                }
            }
        }
    }
} 