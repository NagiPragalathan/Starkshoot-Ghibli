using UnityEngine;
using UnityEngine.UI;
using System.Runtime.InteropServices;
using Photon.Pun;

public class HomeButtonController : MonoBehaviour
{
    [DllImport("__Internal")]
    private static extern void RedirectToURL(string url);

    private Button homeButton;
    private const string HOME_URL = "https://www.google.com";

    void Start()
    {
        // Get the Button component
        homeButton = GetComponent<Button>();
        
        // Add click listener
        if (homeButton != null)
        {
            homeButton.onClick.AddListener(OnHomeButtonClick);
        }
        else
        {
            Debug.LogError("HomeButtonController: No Button component found!");
        }
    }

    void OnHomeButtonClick()
    {
        // Clean up Photon connection if connected
        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.LeaveRoom();
        }

        // Handle redirection based on platform
        #if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL build
            RedirectToURL(HOME_URL);
        #else
            // Unity Editor or other platforms
            Debug.Log($"Would redirect to: {HOME_URL}");
            // Optionally open URL in default browser for testing
            Application.OpenURL(HOME_URL);
        #endif
    }

    void OnDestroy()
    {
        // Clean up the listener when the object is destroyed
        if (homeButton != null)
        {
            homeButton.onClick.RemoveListener(OnHomeButtonClick);
        }
    }
}
