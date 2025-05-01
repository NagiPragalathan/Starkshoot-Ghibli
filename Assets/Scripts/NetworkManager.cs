using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using System;
using UnityStandardAssets.Characters.FirstPerson;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public class NetworkManager : MonoBehaviourPunCallbacks {

    [SerializeField]
    private Text connectionText;
    [SerializeField]
    private Transform[] spawnPoints;
    [SerializeField]
    private Camera sceneCamera;
    [SerializeField]
    private GameObject[] playerModel;
    [SerializeField]
    private GameObject serverWindow;
    [SerializeField]
    private GameObject messageWindow;
    [SerializeField]
    private GameObject sightImage;
    [SerializeField]
    private InputField username;
    [SerializeField]
    private InputField roomName;
    [SerializeField]
    private InputField roomList;
    [SerializeField]
    private InputField messagesLog;
    [SerializeField]
    private Text scoreText;
    [SerializeField]
    private Text killsText;
    [SerializeField]
    private Text timerText;
    [SerializeField]
    private GameObject leaderboardPanel;
    [SerializeField]
    private Transform leaderboardContent;
    [SerializeField]
    private GameObject leaderboardEntryPrefab;
    [SerializeField]
    private Dropdown timeSelectionDropdown;
    [SerializeField]
    private float[] timeOptions = { 180f, 300f, 600f }; // 3, 5, 10 minutes

    [Header("NPC Settings")]
    [SerializeField] private GameObject npcPrefab;
    [SerializeField] public int maxNPCs = 3;
    [SerializeField] private float npcSpawnDelay = 5f;

    [SerializeField]
    private Button reloadButton;

    private GameObject player;
    private Queue<string> messages;
    private const int messageCount = 10;
    private string nickNamePrefKey = "PlayerName";
    private Dictionary<string, PlayerStats> playerStats = new Dictionary<string, PlayerStats>();
    private float currentGameTime;
    private bool isGameActive = false;
    private Dictionary<string, int> killStreaks = new Dictionary<string, int>();
    private float roomListUpdateTimer = 0f;
    private const float ROOM_LIST_UPDATE_INTERVAL = 3f;
    private Dictionary<string, RoomInfo> cachedRoomList = new Dictionary<string, RoomInfo>();
    private Dictionary<string, int> roomPlayerCounts = new Dictionary<string, int>();
    private bool isReconnecting = false;
    private const float RECONNECT_INTERVAL = 2f;
    private const int MAX_RECONNECT_ATTEMPTS = 5;
    private int currentReconnectAttempts = 0;
    private string lastRoomName = null;
    private bool wasInRoom = false;
    private const float CONNECTION_CHECK_INTERVAL = 1f;
    private float connectionCheckTimer = 0f;

    // Add a new dictionary to track bot kills
    private Dictionary<string, int> botKills = new Dictionary<string, int>();

    // Add this class to track player statistics
    private class PlayerStats {
        public int Score { get; set; }
        public int Kills { get; set; }

        public PlayerStats() {
            Score = 0;
            Kills = 0;
        }
    }

    // Add a HashSet to track processed kills
    private HashSet<string> processedKills = new HashSet<string>();

    // Add these at the top of the NetworkManager class
    private List<GameObject> activeNPCs = new List<GameObject>();
    private List<GameObject> deadNPCs = new List<GameObject>();
    private float npcCleanupInterval = 3f; // Check and cleanup every 3 seconds

    // Add this at the start of the class, after other private fields
    private const string PLAYER_STATS_PROP_KEY = "PlayerStats";

    /// <summary>
    /// Start is called on the frame when a script is enabled just before
    /// any of the Update methods is called the first time.
    /// </summary>
    void Start() {
        messages = new Queue<string>(messageCount);
        if (PlayerPrefs.HasKey(nickNamePrefKey)) {
            username.text = PlayerPrefs.GetString(nickNamePrefKey);
        }
        
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.ConnectUsingSettings();
        connectionText.text = "Connecting to lobby...";
        
        // Initialize UI
        scoreText.text = "Score: 0";
        killsText.text = "Kills: 0";
        
        // Setup time selection dropdown
        SetupTimeDropdown();
        
        // Initialize timer with default time (5 minutes)
        currentGameTime = timeOptions[1];
        if (timerText != null) {
            timerText.text = FormatTime(currentGameTime);
        }
        
        if (leaderboardPanel != null) {
            leaderboardPanel.SetActive(false);
        }
        
        // Initialize player stats
        InitializePlayerStats();
        
        // Make sure UI is initialized with zero values
        if (scoreText != null) scoreText.text = "Score: 0";
        if (killsText != null) killsText.text = "Kills: 0";

        // Add these lines to handle application focus
        Application.runInBackground = true;
        PhotonNetwork.KeepAliveInBackground = 3000; // Keep connection alive for 3 seconds in background

        SetupNPCPrefab();

        if (PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(NPCMaintenanceRoutine());
        }

        // Setup reload button
        if (reloadButton != null)
        {
            reloadButton.onClick.RemoveListener(ReloadScene); // Remove any existing listeners first
            reloadButton.onClick.AddListener(ReloadScene);
        }

        // Make sure the server window is visible when starting
        if (serverWindow != null)
        {
            serverWindow.SetActive(true);
        }

        // Initialize connection if not already connected
        if (!PhotonNetwork.IsConnected)
        {
            PhotonNetwork.AutomaticallySyncScene = true;
            PhotonNetwork.ConnectUsingSettings();
            if (connectionText != null)
            {
                connectionText.text = "Connecting to lobby...";
            }
        }
    }

    void SetupTimeDropdown() {
        if (timeSelectionDropdown != null) {
            timeSelectionDropdown.ClearOptions();
            List<string> options = new List<string>();
            
            foreach (float time in timeOptions) {
                int minutes = Mathf.FloorToInt(time / 60f);
                options.Add($"{minutes} Minutes");
            }
            
            timeSelectionDropdown.AddOptions(options);
            timeSelectionDropdown.value = 1; // Default to second option (5 minutes)
        }
    }

    /// <summary>
    /// Called on the client when you have successfully connected to a master server.
    /// </summary>
    public override void OnConnectedToMaster() {
        Debug.Log("Connected to Master Server. Joining lobby...");
        
        if (isReconnecting && wasInRoom && !string.IsNullOrEmpty(lastRoomName)) {
            // Try to rejoin the previous room
            Debug.Log($"Attempting to rejoin room: {lastRoomName}");
            PhotonNetwork.RejoinRoom(lastRoomName);
        } else {
            // Normal connection flow
            PhotonNetwork.JoinLobby(TypedLobby.Default);
        }
    }

    /// <summary>
    /// Called on the client when the connection was lost or you disconnected from the server.
    /// </summary>
    /// <param name="cause">DisconnectCause data associated with this disconnect.</param>
    public override void OnDisconnected(DisconnectCause cause) {
        Debug.LogWarning($"Disconnected from server: {cause}");
        
        // Store room information before disconnect
        if (PhotonNetwork.InRoom) {
            lastRoomName = PhotonNetwork.CurrentRoom.Name;
            wasInRoom = true;
        }

        // Don't try to reconnect if it was an intended disconnect
        if (cause != DisconnectCause.DisconnectByClientLogic) {
            StartCoroutine(TryReconnect());
        }

        if (connectionText != null) {
            connectionText.text = $"Disconnected: {cause}. Attempting to reconnect...";
        }

        // Don't reset the game state immediately
        if (cause == DisconnectCause.DisconnectByClientLogic) {
            // Reset game state only if it's an intended disconnect
            isGameActive = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    /// <summary>
    /// Callback function on joined lobby.
    /// </summary>
    public override void OnJoinedLobby() {
        Debug.Log("Joined Lobby successfully!");
        serverWindow.SetActive(true);
        connectionText.text = "";
        
        // The room list will automatically start updating via OnRoomListUpdate callback
    }

    /// <summary>
    /// Callback function on reveived room list update.
    /// </summary>
    /// <param name="rooms">List of RoomInfo.</param>
    public override void OnRoomListUpdate(List<RoomInfo> rooms) {
        Debug.Log($"Room list updated. Total rooms: {rooms.Count}");
        
        foreach (RoomInfo room in rooms) {
            if (room.RemovedFromList) { 
                cachedRoomList.Remove(room.Name);
                roomPlayerCounts.Remove(room.Name);
                continue;
            }

            // Update or add room info to cache
            cachedRoomList[room.Name] = room;
            roomPlayerCounts[room.Name] = room.PlayerCount;
        }

        UpdateRoomListDisplay();
    }

    private void UpdateRoomListDisplay() {
        if (roomList == null) return;

        if (cachedRoomList.Count == 0) {
            roomList.text = "No rooms available.";
            return;
        }

        roomList.text = "";
        foreach (var kvp in cachedRoomList) {
            RoomInfo room = kvp.Value;
            if (room == null) continue;

            // Get the current player count from our tracking dictionary
            int currentPlayerCount = roomPlayerCounts.ContainsKey(room.Name) ? 
                roomPlayerCounts[room.Name] : room.PlayerCount;

            string roomStatus = GetRoomStatusText(room, currentPlayerCount);
            
            roomList.text += $"Room: {room.Name}\n" +
                            $"Players: {currentPlayerCount}/{room.MaxPlayers}\n" +
                            $"Status: {roomStatus}\n" +
                            GetRoomCustomPropertiesText(room) +
                            "-------------------\n";
        }
    }

    private string GetRoomStatusText(RoomInfo room, int currentPlayerCount) {
        if (!room.IsOpen) return "Closed";
        if (currentPlayerCount >= room.MaxPlayers) return "Full";
        if (room.CustomProperties.ContainsKey("GameState")) {
            string gameState = (string)room.CustomProperties["GameState"];
            if (gameState == "InProgress") return "Game in Progress";
            if (gameState == "Ending") return "Game Ending";
        }
        return "Waiting for Players";
    }

    private string GetRoomCustomPropertiesText(RoomInfo room) {
        if (room == null || room.CustomProperties == null) return "";

        string properties = "";
        if (room.CustomProperties.ContainsKey("GameTime")) {
            float gameTime = (float)room.CustomProperties["GameTime"];
            int minutes = Mathf.FloorToInt(gameTime / 60f);
            properties += $"Game Time: {minutes} minutes\n";
        }
        return properties;
    }

    /// <summary>
    /// The button click callback function for join room.
    /// </summary>
    public void JoinRoom() {
        serverWindow.SetActive(false);
        connectionText.text = "Joining room...";
        PhotonNetwork.LocalPlayer.NickName = username.text;
        PlayerPrefs.SetString(nickNamePrefKey, username.text);
        
        RoomOptions roomOptions = new RoomOptions() {
            IsVisible = true,
            IsOpen = true,
            MaxPlayers = 8,
            PublishUserId = true,
            EmptyRoomTtl = 0,
            PlayerTtl = 0,
            CleanupCacheOnLeave = true,
            CustomRoomProperties = new ExitGames.Client.Photon.Hashtable() {
                {"GameTime", timeOptions[timeSelectionDropdown.value]},
                {"CreatedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")},
                {"GameState", "Waiting"}
            },
            CustomRoomPropertiesForLobby = new string[] { "GameTime", "CreatedAt", "GameState" }
        };

        if (PhotonNetwork.IsConnectedAndReady) {
            PhotonNetwork.JoinOrCreateRoom(roomName.text, roomOptions, TypedLobby.Default);
        } else {
            connectionText.text = "PhotonNetwork connection is not ready, try restart it.";
        }
    }

    /// <summary>
    /// Callback function on joined room.
    /// </summary>
    public override void OnJoinedRoom() {
        Debug.Log($"Joined room: {PhotonNetwork.CurrentRoom.Name}");
        connectionText.text = "";
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        // Get the game time from room properties
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("GameTime")) {
            float gameTime = (float)PhotonNetwork.CurrentRoom.CustomProperties["GameTime"];
            currentGameTime = gameTime;
        }
        
        // Start the game timer if master client
        if (PhotonNetwork.IsMasterClient) {
            isGameActive = true;
            photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);
        }
        
        Respawn(0.0f);
        
        // Initialize stats when joining room
        InitializePlayerStats();

        // Update the room player count immediately when someone joins
        string roomName = PhotonNetwork.CurrentRoom.Name;
        if (roomPlayerCounts.ContainsKey(roomName)) {
            roomPlayerCounts[roomName] = PhotonNetwork.CurrentRoom.PlayerCount;
            photonView.RPC("UpdateRoomPlayerCount", RpcTarget.All, roomName, PhotonNetwork.CurrentRoom.PlayerCount);
        }

        if (isReconnecting) {
            Debug.Log("Successfully rejoined room after reconnection");
            isReconnecting = false;
            wasInRoom = false;
            lastRoomName = null;
            
            // Restore player state if needed
            RestorePlayerState();
        }

        if (PhotonNetwork.IsMasterClient)
        {
            // Spawn NPCs with a slight delay to ensure everything is set up
            StartCoroutine(SpawnInitialNPCs());
        }
    }

    /// <summary>
    /// Start spawn or respawn a player.
    /// </summary>
    /// <param name="spawnTime">Time waited before spawn a player.</param>
    void Respawn(float spawnTime) {
        sightImage.SetActive(false);
        sceneCamera.enabled = true;
        StartCoroutine(RespawnCoroutine(spawnTime));
    }

    /// <summary>
    /// The coroutine function to spawn player.
    /// </summary>
    /// <param name="spawnTime">Time waited before spawn a player.</param>
    IEnumerator RespawnCoroutine(float spawnTime) {
        yield return new WaitForSeconds(spawnTime);
        messageWindow.SetActive(true);
        sightImage.SetActive(true);
        int playerIndex = Random.Range(0, playerModel.Length);
        int spawnIndex = Random.Range(0, spawnPoints.Length);
        player = PhotonNetwork.Instantiate(playerModel[playerIndex].name, spawnPoints[spawnIndex].position, spawnPoints[spawnIndex].rotation, 0);
        
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        playerHealth.RespawnEvent += Respawn;
        playerHealth.AddMessageEvent += AddMessage;
        
        sceneCamera.enabled = false;
        if (spawnTime == 0) {
            AddMessage("Player " + PhotonNetwork.LocalPlayer.NickName + " Joined Game.");
        } else {
            AddMessage("Player " + PhotonNetwork.LocalPlayer.NickName + " Respawned.");
        }
    }

    /// <summary>
    /// Add message to message panel.
    /// </summary>
    /// <param name="message">The message that we want to add.</param>
    void AddMessage(string message) {
        photonView.RPC("AddMessage_RPC", RpcTarget.All, message);
    }

    /// <summary>
    /// RPC function to call add message for each client.
    /// </summary>
    /// <param name="message">The message that we want to add.</param>
    [PunRPC]
    void AddMessage_RPC(string message) {
        messages.Enqueue(message);
        if (messages.Count > messageCount) {
            messages.Dequeue();
        }
        messagesLog.text = "";
        foreach (string m in messages) {
            messagesLog.text += m + "\n";
        }
    }

    /// <summary>
    /// Callback function when other player disconnected.
    /// </summary>
    public override void OnPlayerLeftRoom(Player other) {
        if (PhotonNetwork.IsMasterClient) {
            AddMessage("Player " + other.NickName + " Left Game.");
        }

        string roomName = PhotonNetwork.CurrentRoom.Name;
        if (roomPlayerCounts.ContainsKey(roomName)) {
            roomPlayerCounts[roomName] = PhotonNetwork.CurrentRoom.PlayerCount;
            photonView.RPC("UpdateRoomPlayerCount", RpcTarget.All, roomName, PhotonNetwork.CurrentRoom.PlayerCount);
        }
    }

    // Add this method to handle UI updates
    private void UpdateUIStats(int score, int kills) {
        // Ensure UI updates happen on the main thread
        if (scoreText != null) {
            scoreText.text = $"Score: {score}";
            Debug.Log($"Updated score text to: {score}");
        } else {
            Debug.LogWarning("scoreText is null!");
        }
        
        if (killsText != null) {
            killsText.text = $"Kills: {kills}";
            Debug.Log($"Updated kills text to: {kills}");
        } else {
            Debug.LogWarning("killsText is null!");
        }
    }

    [PunRPC]
    private void UpdatePlayerStats_RPC(string playerName, int score, int kills) {
        Debug.Log($"UpdatePlayerStats_RPC received for {playerName}. Score: {score}, Kills: {kills}");
        
        // Update local dictionary first
        if (!playerStats.ContainsKey(playerName)) {
            playerStats[playerName] = new PlayerStats();
        }
        playerStats[playerName].Score = score;
        playerStats[playerName].Kills = kills;
        
        // Update room properties if we're in a room
        if (PhotonNetwork.InRoom) {
            ExitGames.Client.Photon.Hashtable statsData = new ExitGames.Client.Photon.Hashtable();
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PLAYER_STATS_PROP_KEY)) {
                statsData = (ExitGames.Client.Photon.Hashtable)PhotonNetwork.CurrentRoom.CustomProperties[PLAYER_STATS_PROP_KEY];
            }
            
            // Create or update player stats in the room properties
            ExitGames.Client.Photon.Hashtable playerData = new ExitGames.Client.Photon.Hashtable() {
                {"Score", score},
                {"Kills", kills}
            };
            statsData[playerName] = playerData;
            
            // Update the room properties
            ExitGames.Client.Photon.Hashtable roomProps = new ExitGames.Client.Photon.Hashtable() {
                {PLAYER_STATS_PROP_KEY, statsData}
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);
        }
        
        // Update UI for the local player
        if (playerName == PhotonNetwork.LocalPlayer.NickName) {
            UpdateUIStats(score, kills);
        }
    }

    public void AddKill() {
        string playerName = PhotonNetwork.LocalPlayer.NickName;
        if (!playerStats.ContainsKey(playerName)) {
            playerStats[playerName] = new PlayerStats();
        }
        
        // Update local stats
        playerStats[playerName].Kills++;
        int currentScore = playerStats[playerName].Score + 100; // Add 100 points per kill
        playerStats[playerName].Score = currentScore;
        
        // Debug message
        Debug.Log($"AddKill called for {playerName}. Kills: {playerStats[playerName].Kills}, Score: {currentScore}");
        
        // Update UI immediately
        UpdateUIStats(currentScore, playerStats[playerName].Kills);
        
        // Send update to all clients
        photonView.RPC("UpdatePlayerStats_RPC", RpcTarget.All, 
            playerName, 
            currentScore, 
            playerStats[playerName].Kills);
    }

    public void AddScore(int scoreAmount) {
        string playerName = PhotonNetwork.LocalPlayer.NickName;
        if (!playerStats.ContainsKey(playerName)) {
            playerStats[playerName] = new PlayerStats();
        }
        
        // Update local stats
        int currentScore = playerStats[playerName].Score + scoreAmount;
        playerStats[playerName].Score = currentScore;
        
        // Debug message
        Debug.Log($"AddScore called for {playerName}. New Score: {currentScore}");
        
        // Update UI immediately
        UpdateUIStats(currentScore, playerStats[playerName].Kills);
        
        // Send update to all clients
        photonView.RPC("UpdatePlayerStats_RPC", RpcTarget.All, 
            playerName, 
            currentScore, 
            playerStats[playerName].Kills);
    }

    private void InitializePlayerStats() {
        string playerName = PhotonNetwork.LocalPlayer.NickName;
        if (!playerStats.ContainsKey(playerName)) {
            playerStats[playerName] = new PlayerStats();
            killStreaks[playerName] = 0;  // Initialize kill streak
            // Initialize UI with zero values
            UpdateUIStats(0, 0);
        }
    }

    void Update() {
        if (isGameActive && PhotonNetwork.IsMasterClient) {
            if (currentGameTime > 0) {
                currentGameTime -= Time.deltaTime;
                photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);

                if (currentGameTime <= 0) {
                    currentGameTime = 0;
                    photonView.RPC("EndGame", RpcTarget.All);
                }
            }
        }

        // Add room list refresh logic when in lobby
        if (PhotonNetwork.InLobby && !PhotonNetwork.InRoom) {
            roomListUpdateTimer -= Time.deltaTime;
            if (roomListUpdateTimer <= 0f) {
                roomListUpdateTimer = ROOM_LIST_UPDATE_INTERVAL;
                // Room list updates are automatically sent by the server
                // We just need to make sure we're properly handling the OnRoomListUpdate callback
                UpdateRoomListDisplay();
            }
        }

        // Add periodic refresh for room list
        float refreshTimer = 0f;
        const float REFRESH_INTERVAL = 1f; // Update every second

        if (PhotonNetwork.InLobby && !PhotonNetwork.InRoom) {
            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f) {
                refreshTimer = REFRESH_INTERVAL;
                UpdateRoomListDisplay();
            }
        }

        // Add connection monitoring
        if (PhotonNetwork.IsConnected) {
            connectionCheckTimer -= Time.deltaTime;
            if (connectionCheckTimer <= 0f) {
                connectionCheckTimer = CONNECTION_CHECK_INTERVAL;
                // Check if we're still properly connected
                if (PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer ||
                    PhotonNetwork.NetworkClientState == ClientState.Joined) {
                    // Connection is healthy
                    if (connectionText != null) {
                        connectionText.text = "";
                    }
                }
            }
        }

        // Check NPC count more frequently when game is active (every 1 second)
        if (PhotonNetwork.IsMasterClient && isGameActive && Time.frameCount % 60 == 0) // 60 frames ≈ 1 second at 60 FPS
        {
            MaintainNPCCount();
        }
    }

    [PunRPC]
    void SyncTimer(float time) {
        currentGameTime = time;
        if (timerText != null) {
            timerText.text = FormatTime(currentGameTime);
        }
    }

    string FormatTime(float timeInSeconds) {
        timeInSeconds = Mathf.Max(0, timeInSeconds); // Ensure time doesn't go negative
        int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60f);
        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    [PunRPC]
    void EndGame() {
        isGameActive = false;
        
        // Disable all player functionality
        if (player != null) {
            // Disable all interactive components
            var components = player.GetComponents<MonoBehaviour>();
            foreach (var component in components) {
                if (component != this && // Don't disable NetworkManager
                    (component.GetType().Name.Contains("Controller") ||
                     component.GetType().Name.Contains("Shooting") ||
                     component.GetType().Name.Contains("Weapon") ||
                     component.GetType().Name.Contains("Gun") ||
                     component.GetType().Name.Contains("Health") ||
                     component.GetType().Name.Contains("Mover"))) {
                    component.enabled = false;
                }
            }

            // Also disable components in children (weapons, etc.)
            var childComponents = player.GetComponentsInChildren<MonoBehaviour>();
            foreach (var component in childComponents) {
                if (component.GetType().Name.Contains("Weapon") ||
                    component.GetType().Name.Contains("Gun") ||
                    component.GetType().Name.Contains("Shooting")) {
                    component.enabled = false;
                }
            }
        }

        // Disable all NPCs
        GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
        foreach (GameObject npc in npcs)
        {
            NPCController controller = npc.GetComponent<NPCController>();
            if (controller != null)
            {
                controller.enabled = false;
            }
            
            NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.isStopped = true;
                agent.enabled = false;
            }
        }

        // Ensure cursor is visible and can interact with UI
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Force a final stats sync
        string playerName = PhotonNetwork.LocalPlayer.NickName;
        if (playerStats.ContainsKey(playerName)) {
            photonView.RPC("UpdatePlayerStats_RPC", RpcTarget.All, 
                playerName, 
                playerStats[playerName].Score, 
                playerStats[playerName].Kills);
        }
        
        // Show leaderboard after a short delay to ensure stats are synced
        StartCoroutine(DelayedShowLeaderboard());
    }

    private IEnumerator DelayedShowLeaderboard() {
        // Wait for stats to sync across network
        yield return new WaitForSeconds(0.5f);
        ShowFinalLeaderboard();
    }

    void ShowFinalLeaderboard() {
        if (leaderboardContent == null || leaderboardPanel == null) {
            Debug.LogError("Leaderboard UI components are missing!");
            return;
        }

        Debug.Log("Starting to show leaderboard...");

        // Clear existing entries
        foreach (Transform child in leaderboardContent) {
            if (child != null) {
                Destroy(child.gameObject);
            }
        }

        // Get stats from room properties
        var sortedPlayers = new List<KeyValuePair<string, PlayerStats>>();
        
        if (PhotonNetwork.CurrentRoom != null && 
            PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PLAYER_STATS_PROP_KEY)) {
            ExitGames.Client.Photon.Hashtable statsData = 
                (ExitGames.Client.Photon.Hashtable)PhotonNetwork.CurrentRoom.CustomProperties[PLAYER_STATS_PROP_KEY];
            
            foreach (DictionaryEntry entry in statsData) {
                string playerName = entry.Key.ToString();
                ExitGames.Client.Photon.Hashtable playerData = (ExitGames.Client.Photon.Hashtable)entry.Value;
                
                PlayerStats stats = new PlayerStats {
                    Score = (int)playerData["Score"],
                    Kills = (int)playerData["Kills"]
                };
                
                sortedPlayers.Add(new KeyValuePair<string, PlayerStats>(playerName, stats));
                Debug.Log($"Retrieved player stats: {playerName} - Score: {stats.Score}, Kills: {stats.Kills}");
            }
        }

        // Sort players by score and kills
        sortedPlayers = sortedPlayers
            .OrderByDescending(p => p.Value.Score)
            .ThenByDescending(p => p.Value.Kills)
            .ToList();

        // Create leaderboard entries
        int maxEntries = 8;
        for (int i = 0; i < maxEntries; i++) {
            GameObject entry = Instantiate(leaderboardEntryPrefab, leaderboardContent);
            LeaderboardEntry entryScript = entry.GetComponent<LeaderboardEntry>();
            
            if (i < sortedPlayers.Count) {
                var playerStat = sortedPlayers[i];
                Debug.Log($"Creating leaderboard entry for {playerStat.Key}: Score={playerStat.Value.Score}, Kills={playerStat.Value.Kills}");
                entryScript.SetStats(
                    playerStat.Key,
                    playerStat.Value.Score,
                    playerStat.Value.Kills,
                    i + 1
                );
            } else {
                entryScript.SetStats("-|-", 0, 0, i + 1);
                if (entryScript.scoreText != null) entryScript.scoreText.text = "-|-";
                if (entryScript.killsText != null) entryScript.killsText.text = "-|-";
            }
        }

        // Make sure the leaderboard is visible
        leaderboardPanel.SetActive(true);
        Debug.Log("Leaderboard display completed");
    }

    // Remove the duplicate OnRoomPropertiesUpdate method and combine functionality into a single method
    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged) {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);
        
        // Handle player stats updates for leaderboard
        if (propertiesThatChanged.ContainsKey(PLAYER_STATS_PROP_KEY) && 
            leaderboardPanel != null && 
            leaderboardPanel.activeSelf) {
            ShowFinalLeaderboard();
        }
        
        // Handle reconnection updates
        if (isReconnecting && PhotonNetwork.InRoom) {
            // Make sure we have the latest room properties after reconnecting
            UpdateCachedRoomInfo(PhotonNetwork.CurrentRoom.Name, PhotonNetwork.CurrentRoom.CustomProperties);
        }
        
        // Update room list display
        if (PhotonNetwork.InRoom) {
            string roomName = PhotonNetwork.CurrentRoom.Name;
            if (cachedRoomList.ContainsKey(roomName)) {
                // Update only the properties that changed
                RoomInfo currentRoomInfo = cachedRoomList[roomName];
                if (currentRoomInfo != null) {
                    // Update the cached room info with new properties
                    UpdateCachedRoomInfo(roomName, propertiesThatChanged);
                    UpdateRoomListDisplay();
                }
            }
        }
    }

    // Add method to reset game timer
    public void ResetGameTimer() {
        if (PhotonNetwork.IsMasterClient) {
            currentGameTime = timeOptions[1];
            isGameActive = true;
            photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);
        }
    }

    // Add method to pause/resume timer
    public void SetGameActive(bool active) {
        if (PhotonNetwork.IsMasterClient) {
            isGameActive = active;
            photonView.RPC("SyncGameState", RpcTarget.All, active);
        }
    }

    [PunRPC]
    void SyncGameState(bool active) {
        isGameActive = active;
    }

    public void ReturnToLobby() {
        // Clean up before leaving
        if (leaderboardPanel != null) {
            leaderboardPanel.SetActive(false);
        }
        
        if (PhotonNetwork.IsConnected) {
            PhotonNetwork.LeaveRoom();
        }
        
        SceneManager.LoadScene("LobbyScene");
    }

    // Add method to safely set UI text
    private void SafeSetText(Text textComponent, string message) {
        if (textComponent != null) {
            textComponent.text = message;
        }
    }

    // Add method to check if UI is valid
    private bool IsUIValid() {
        return connectionText != null && 
               scoreText != null && 
               killsText != null && 
               timerText != null && 
               leaderboardPanel != null && 
               leaderboardContent != null;
    }

    // Add OnDestroy to clean up
    void OnDestroy() {
        // Clean up references
        connectionText = null;
        scoreText = null;
        killsText = null;
        timerText = null;
        leaderboardPanel = null;
        leaderboardContent = null;

        if (reloadButton != null)
        {
            reloadButton.onClick.RemoveListener(ReloadScene);
        }
    }

    // Add this helper method to update cached room info
    private void UpdateCachedRoomInfo(string roomName, ExitGames.Client.Photon.Hashtable properties) {
        if (cachedRoomList.TryGetValue(roomName, out RoomInfo roomInfo)) {
            // Update only the properties that changed
            foreach (DictionaryEntry entry in properties) {
                if (roomInfo.CustomProperties.ContainsKey(entry.Key)) {
                    roomInfo.CustomProperties[entry.Key] = entry.Value;
                } else {
                    roomInfo.CustomProperties.Add(entry.Key, entry.Value);
                }
            }
        }
    }

    [PunRPC]
    private void AddKill_RPC(string killerName)
    {
        // This method now only handles player kills
        if (!playerStats.ContainsKey(killerName))
        {
            playerStats[killerName] = new PlayerStats();
        }
        
        // Update kill streak and calculate score
        killStreaks[killerName]++;
        int scoreToAdd = CalculateKillScore(killStreaks[killerName]);
        
        // Update killer's stats
        playerStats[killerName].Kills++;
        int currentScore = playerStats[killerName].Score + scoreToAdd;
        playerStats[killerName].Score = currentScore;
        
        // Add kill streak notification to chat
        string notification = GetKillStreakNotification(killStreaks[killerName]);
        if (!string.IsNullOrEmpty(notification))
        {
            AddMessage($"{killerName} - {notification}!");
        }
        
        // Update UI if this is the killer's client
        if (killerName == PhotonNetwork.LocalPlayer.NickName)
        {
            UpdateUIStats(currentScore, playerStats[killerName].Kills);
        }
    }

    private int CalculateKillScore(int killStreak) {
        switch (killStreak) {
            case 1:
                return 10;  // First kill
            case 2:
                return 15;  // Double kill
            case 3:
                return 25;  // Triple kill
            case 4:
                return 40;  // Killing spree
            default:
                return 60;  // God like
        }
    }

    private string GetKillStreakNotification(int killStreak) {
        switch (killStreak) {
            case 2:
                return "Double Kill";
            case 3:
                return "Triple Kill";
            case 4:
                return "Killing Spree";
            case 5:
                return "God Like";
            default:
                return null;
        }
    }

    // Add this method to reset kill streak when a player dies
    public void ResetKillStreak(string playerName) {
        if (killStreaks.ContainsKey(playerName)) {
            killStreaks[playerName] = 0;
        }
    }

    public override void OnCreatedRoom() {
        Debug.Log($"Room created successfully: {PhotonNetwork.CurrentRoom.Name}");
        // The room list will automatically update for all clients in the lobby
    }

    public override void OnCreateRoomFailed(short returnCode, string message) {
        Debug.LogError($"Failed to create room: {message}");
        connectionText.text = $"Room creation failed: {message}";
        serverWindow.SetActive(true);
    }

    public override void OnJoinRoomFailed(short returnCode, string message) {
        Debug.LogError($"Failed to join room: {message}");
        
        if (isReconnecting && wasInRoom) {
            // If rejoining failed, clear the stored room info and join the lobby
            lastRoomName = null;
            wasInRoom = false;
            PhotonNetwork.JoinLobby(TypedLobby.Default);
        }
        
        connectionText.text = $"Failed to join room: {message}";
        serverWindow.SetActive(true);
    }

    // Add these new callbacks to track player join/leave events
    public override void OnPlayerEnteredRoom(Player newPlayer) {
        base.OnPlayerEnteredRoom(newPlayer);
        
        string roomName = PhotonNetwork.CurrentRoom.Name;
        if (roomPlayerCounts.ContainsKey(roomName)) {
            roomPlayerCounts[roomName] = PhotonNetwork.CurrentRoom.PlayerCount;
            // Update all clients
            photonView.RPC("UpdateRoomPlayerCount", RpcTarget.All, roomName, PhotonNetwork.CurrentRoom.PlayerCount);
        }
    }

    [PunRPC]
    private void UpdateRoomPlayerCount(string roomName, int playerCount) {
        if (roomPlayerCounts.ContainsKey(roomName)) {
            roomPlayerCounts[roomName] = playerCount;
            UpdateRoomListDisplay();
        }
    }

    // Add this method to handle reconnection attempts
    private IEnumerator TryReconnect() {
        isReconnecting = true;
        currentReconnectAttempts = 0;

        while (!PhotonNetwork.IsConnected && currentReconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
            Debug.Log($"Attempting to reconnect... Attempt {currentReconnectAttempts + 1}/{MAX_RECONNECT_ATTEMPTS}");
            connectionText.text = $"Reconnecting... Attempt {currentReconnectAttempts + 1}";

            // Try to reconnect
            PhotonNetwork.ConnectUsingSettings();
            currentReconnectAttempts++;

            // Wait for the reconnection interval
            yield return new WaitForSeconds(RECONNECT_INTERVAL);
        }

        if (!PhotonNetwork.IsConnected) {
            Debug.LogError("Failed to reconnect after maximum attempts");
            connectionText.text = "Failed to reconnect. Please restart the game.";
        }

        isReconnecting = false;
    }

    // Add method to restore player state after reconnection
    private void RestorePlayerState() {
        if (player != null) {
            // Restore player position, health, etc.
            PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth != null) {
                playerHealth.enabled = true;
            }

            PlayerNetworkMover playerMover = player.GetComponent<PlayerNetworkMover>();
            if (playerMover != null) {
                playerMover.enabled = true;
            }
        }

        // Sync game state
        if (PhotonNetwork.IsMasterClient) {
            photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);
            photonView.RPC("SyncGameState", RpcTarget.All, isGameActive);
        }
    }

    // Add OnApplicationPause and OnApplicationFocus handlers
    void OnApplicationPause(bool isPaused) {
        if (!isPaused) {
            // Application resumed
            if (!PhotonNetwork.IsConnected && !isReconnecting) {
                StartCoroutine(TryReconnect());
            }
        }
    }

    void OnApplicationFocus(bool hasFocus) {
        if (hasFocus) {
            // Application gained focus
            if (!PhotonNetwork.IsConnected && !isReconnecting) {
                StartCoroutine(TryReconnect());
            }
        }
    }

    public GameObject SpawnNPC()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("Only MasterClient can spawn NPCs!");
            return null;
        }
        
        Debug.Log("SpawnNPC called by MasterClient");
        
        // Choose a random spawn point
        int spawnIndex = Random.Range(0, spawnPoints.Length);
        Vector3 spawnPosition = spawnPoints[spawnIndex].position;
        Quaternion spawnRotation = spawnPoints[spawnIndex].rotation;

        // Verify spawn point is on NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(spawnPosition, out hit, 1.0f, NavMesh.AllAreas))
        {
            spawnPosition = hit.position;
        }
        
        // Check that we have the NPC prefab
        if (npcPrefab == null)
        {
            Debug.LogError("NPC Prefab is null! Cannot spawn NPC.");
            return null;
        }
        
        try
        {
            // Instantiate the NPC
            GameObject npc = PhotonNetwork.Instantiate(npcPrefab.name, spawnPosition, spawnRotation, 0);
            
            if (npc != null)
            {
                PhotonView pv = npc.GetComponent<PhotonView>();
                Debug.Log($"NPC spawned with PhotonView ID: {pv.ViewID} at position: {spawnPosition}");
                SetupNPC(npc);
                
                // Don't add message here since the bot name isn't set yet
                // The message will be added in the SetBotName RPC in NPCHealth
                
                return npc;
            }
            else
            {
                Debug.LogError("Failed to instantiate NPC prefab!");
                return null;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error spawning NPC: {e.Message}");
            return null;
        }
    }

    private void SetupNPC(GameObject npc)
    {
        if (npc == null) return;
        
        // Set proper scale (1.5x is larger than player)
        npc.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        
        // Set up NPC-specific components
        npc.tag = "NPC";
        npc.layer = LayerMask.NameToLayer("Shootable");
        
        // Get PhotonView
        PhotonView photonView = npc.GetComponent<PhotonView>();
        if (photonView == null)
        {
            Debug.LogError("NPC is missing PhotonView component!");
            return;
        }
        
        // Add unique name to help with debugging
        npc.name = $"NPC_{photonView.ViewID}";
        
        // Setup components
        NPCController npcController = npc.GetComponent<NPCController>();
        if (npcController == null)
        {
            npcController = npc.AddComponent<NPCController>();
        }
        
        // Ensure animator component is active
        Animator animator = npc.GetComponent<Animator>();
        if (animator == null)
        {
            animator = npc.GetComponentInChildren<Animator>();
        }
        
        // If still no animator found, log error
        if (animator == null)
        {
            Debug.LogError($"NPC {npc.name} has no Animator component!");
        }
        else
        {
            // Make sure animator is enabled and reset
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
            Debug.Log($"NPC {npc.name} animator initialized");
        }
        
        // Configure NavMeshAgent with unique settings to avoid stacking
        NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.height = 1.8f;
            agent.radius = 0.5f;
            agent.baseOffset = 0f;
            agent.speed = Random.Range(3.0f, 4.0f); // Slightly different speeds
            agent.acceleration = 12f;
            agent.angularSpeed = 180f;
            agent.stoppingDistance = 1f;
            agent.avoidancePriority = Random.Range(20, 80); // Different priorities
            
            // Ensure agent is on NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(npc.transform.position, out hit, 1.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }

        // Configure collider to match player size
        CapsuleCollider collider = npc.GetComponent<CapsuleCollider>();
        if (collider != null)
        {
            collider.height = 1.8f;
            collider.radius = 0.5f;
            collider.center = new Vector3(0, 0.9f, 0);
        }
        
        // Setup health component
        NPCHealth npcHealth = npc.GetComponent<NPCHealth>();
        if (npcHealth == null)
        {
            npcHealth = npc.AddComponent<NPCHealth>();
        }

        // Force the NPC to initialize
        npcController.InitializeNPC();
        
        // Force initialize on all clients
        photonView.RPC("InitializeRemoteNPC", RpcTarget.Others);
    }

    [PunRPC]
    private void InitializeRemoteNPC()
    {
        // This is called on client side to ensure the NPC is properly initialized
        NPCController controller = GetComponent<NPCController>();
        if (controller != null)
        {
            controller.InitializeNPC();
        }
        
        // Set proper scale
        transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        
        // Get the animator component
        Animator animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        
        // Check if animator exists before using it
        if (animator != null)
        {
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
            Debug.Log($"Remote NPC {photonView.ViewID} animator initialized");
        }
    }

    private void VerifyNPCSetup(GameObject npc)
    {
        if (npc == null)
        {
            Debug.LogError("NPC object is null!");
            return;
        }

        // Check essential components
        var health = npc.GetComponent<NPCHealth>();
        if (health == null) Debug.LogError("NPCHealth component missing!");

        var photonView = npc.GetComponent<PhotonView>();
        if (photonView == null) Debug.LogError("PhotonView component missing!");

        var animator = npc.GetComponent<Animator>();
        if (animator == null)
        {
            animator = npc.GetComponentInChildren<Animator>();
            if (animator == null) Debug.LogError("Animator component missing!");
        }

        var npcController = npc.GetComponent<NPCController>();
        if (npcController == null) Debug.LogError("NPCController component missing!");

        var agent = npc.GetComponent<NavMeshAgent>();
        if (agent == null) Debug.LogError("NavMeshAgent component missing!");

        // Verify layer
        if (npc.layer != LayerMask.NameToLayer("Shootable"))
            Debug.LogError("NPC not in Shootable layer!");

        // Verify tag
        if (npc.tag != "NPC")
            Debug.LogError("NPC tag not set correctly!");
    }

    private void MaintainNPCCount()
    {
        if (!PhotonNetwork.IsMasterClient || !isGameActive) return;

        // Only run this if the game is active
        GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
        int currentNPCCount = npcs.Length;

        // If we have too many NPCs, clean them up
        if (currentNPCCount > maxNPCs)
        {
            CleanupAndMaintainNPCs();
            return;
        }

        // Only spawn if we have less than maxNPCs
        if (currentNPCCount < maxNPCs)
        {
            SpawnNPC();
            Debug.Log($"Maintaining NPC count: Spawned new NPC. Count: {currentNPCCount + 1}/{maxNPCs}");
        }
    }

    private IEnumerator SpawnInitialNPCs()
    {
        yield return new WaitForSeconds(1f); // Wait for room to fully initialize
        
        // Add a message that bots are joining the battle
        AddMessage("Bots are joining the battle!");
        
        // Spawn exactly maxNPCs NPCs
        for (int i = 0; i < maxNPCs; i++)
        {
            SpawnNPC();
            yield return new WaitForSeconds(0.5f); // Small delay between spawns
        }
    }

    public void RequestNPCRespawn(Vector3 deathPosition)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        // Wait a short delay before spawning a new NPC
        StartCoroutine(DelayedRespawn());
    }

    private IEnumerator DelayedRespawn()
    {
        yield return new WaitForSeconds(2f); // Wait 2 seconds before respawning
        
        GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
        int activeCount = npcs.Count(npc => {
            NPCHealth health = npc.GetComponent<NPCHealth>();
            return health != null && !health.IsDead();
        });

        if (activeCount < maxNPCs)
        {
            SpawnNPC();
        }
    }

    public void SetupNPCPrefab()
    {
        // Add this method to your NetworkManager to verify NPC prefab setup
        GameObject npcPrefab = Resources.Load<GameObject>("NPCPrefab");
        if (npcPrefab != null)
        {
            // Check components
            NPCHealth health = npcPrefab.GetComponent<NPCHealth>();
            if (health == null)
            {
                Debug.LogError("NPCPrefab missing NPCHealth component!");
            }

            PhotonView photonView = npcPrefab.GetComponent<PhotonView>();
            if (photonView == null)
            {
                Debug.LogError("NPCPrefab missing PhotonView component!");
            }
            else
            {
                // Verify PhotonView settings
                photonView.ObservedComponents = new List<Component> { health };
                photonView.Synchronization = ViewSynchronization.UnreliableOnChange;
            }

            Animator animator = npcPrefab.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError("NPCPrefab missing Animator component!");
            }
            else
            {
                // Verify animator parameters
                bool hasIsHurt = false;
                bool hasIsDead = false;
                foreach (AnimatorControllerParameter param in animator.parameters)
                {
                    if (param.name == "IsHurt") hasIsHurt = true;
                    if (param.name == "IsDead") hasIsDead = true;
                }
                if (!hasIsHurt) Debug.LogError("Animator missing IsHurt parameter!");
                if (!hasIsDead) Debug.LogError("Animator missing IsDead parameter!");
            }
        }
        else
        {
            Debug.LogError("NPCPrefab not found in Resources folder!");
        }
    }

    // Add new method for bot kills
    [PunRPC]
    private void AddBotKill_RPC(string killerName, string botName)
    {
        // Add logging to track when this is called
        Debug.Log($"AddBotKill_RPC called - Killer: {killerName}, Bot: {botName}, IsMasterClient: {PhotonNetwork.IsMasterClient}");

        // Ensure this bot hasn't already been counted
        string killKey = $"{botName}_{killerName}";
        if (!processedKills.Contains(killKey))
        {
            processedKills.Add(killKey);
            
            if (!playerStats.ContainsKey(killerName))
            {
                playerStats[killerName] = new PlayerStats();
            }
            
            // Update kill streak and calculate score
            killStreaks[killerName]++;
            int scoreToAdd = CalculateBotKillScore(killStreaks[killerName]);
            
            // Update killer's stats
            playerStats[killerName].Kills++;
            int currentScore = playerStats[killerName].Score + scoreToAdd;
            playerStats[killerName].Score = currentScore;
            
            // Track bot kills separately
            if (!botKills.ContainsKey(killerName))
            {
                botKills[killerName] = 0;
            }
            botKills[killerName]++;
            
            // Add kill message to chat
            AddMessage($"{killerName} eliminated {botName}!");
            
            // Add kill streak notification to chat
            string notification = GetKillStreakNotification(killStreaks[killerName]);
            if (!string.IsNullOrEmpty(notification))
            {
                AddMessage($"{killerName} - {notification}!");
            }
            
            // Update UI if this is the killer's client
            if (killerName == PhotonNetwork.LocalPlayer.NickName)
            {
                UpdateUIStats(currentScore, playerStats[killerName].Kills);
            }
        }
        else
        {
            Debug.Log($"Kill already processed for {killKey}");
        }
    }

    // Add method to calculate bot kill score (you can adjust the values)
    private int CalculateBotKillScore(int killStreak)
    {
        // Bots might be worth less points than player kills
        switch (killStreak)
        {
            case 1:
                return 5;  // First bot kill
            case 2:
                return 8;  // Double kill
            case 3:
                return 12; // Triple kill
            case 4:
                return 20; // Killing spree
            default:
                return 30; // God like
        }
    }

    // Add method to clear processed kills (call this when starting a new game or as needed)
    public void ClearProcessedKills()
    {
        processedKills.Clear();
    }

    [PunRPC]
    public void RequestBotRespawnRPC(Vector3 deathPosition)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        Debug.Log($"Master client received bot respawn request");
        
        if (isGameActive)
        {
            CleanupAndMaintainNPCs();
        }
    }

    private IEnumerator NPCMaintenanceRoutine()
    {
        while (true)
        {
            if (PhotonNetwork.IsMasterClient && isGameActive)
            {
                CleanupAndMaintainNPCs();
            }
            yield return new WaitForSeconds(npcCleanupInterval);
        }
    }

    private void CleanupAndMaintainNPCs()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // Clean up our tracking lists first
        activeNPCs.RemoveAll(npc => npc == null);
        deadNPCs.RemoveAll(npc => npc == null);

        // Find all NPCs in the scene
        GameObject[] allNPCs = GameObject.FindGameObjectsWithTag("NPC");
        
        // Clear our lists before rebuilding them
        activeNPCs.Clear();
        deadNPCs.Clear();
        
        // Update our active and dead NPC lists
        foreach (GameObject npc in allNPCs)
        {
            NPCHealth health = npc.GetComponent<NPCHealth>();
            if (health != null)
            {
                if (health.IsDead())
                {
                    deadNPCs.Add(npc);
                    // Immediately destroy dead NPCs
                    PhotonNetwork.Destroy(npc);
                }
                else
                {
                    activeNPCs.Add(npc);
                }
            }
        }

        Debug.Log($"NPC Status - Active: {activeNPCs.Count}, Dead: {deadNPCs.Count}, Total: {allNPCs.Length}");

        // If we have too many active NPCs, destroy the excess ones
        while (activeNPCs.Count > maxNPCs)
        {
            if (activeNPCs.Count > 0)
            {
                GameObject npcToRemove = activeNPCs[activeNPCs.Count - 1];
                activeNPCs.RemoveAt(activeNPCs.Count - 1);
                if (npcToRemove != null)
                {
                    PhotonNetwork.Destroy(npcToRemove);
                }
            }
        }

        // Only spawn new NPCs if we have less than maxNPCs active
        int npcsNeeded = maxNPCs - activeNPCs.Count;
        if (npcsNeeded > 0)
        {
            Debug.Log($"Spawning {npcsNeeded} new NPCs to maintain {maxNPCs} total");
            for (int i = 0; i < npcsNeeded; i++)
            {
                SpawnNPC();
            }
        }
    }

    public void ReloadScene()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            // Keep the camera active until the scene actually reloads
            if (sceneCamera != null)
            {
                sceneCamera.enabled = true;
            }

            // Disable player controls but keep visuals
            if (player != null)
            {
                PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
                if (playerHealth != null)
                {
                    playerHealth.enabled = false;
                }

                PlayerNetworkMover playerMover = player.GetComponent<PlayerNetworkMover>();
                if (playerMover != null)
                {
                    playerMover.enabled = false;
                }
            }

            // Clean up current game state
            isGameActive = false;
            ClearProcessedKills();
            
            // Destroy all NPCs before reloading
            GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
            foreach (GameObject npc in npcs)
            {
                if (npc != null)
                {
                    PhotonNetwork.Destroy(npc);
                }
            }

            // Start the reload process
            StartCoroutine(SmoothReloadCoroutine());
        }
        else
        {
            Debug.Log("Only the host can restart the game!");
            AddMessage("Only the host can restart the game!");
        }
    }

    private IEnumerator SmoothReloadCoroutine()
    {
        // Wait a short moment to ensure everything is disabled properly
        yield return new WaitForSeconds(0.1f);

        if (PhotonNetwork.IsConnected)
        {
            // Leave the room first
            PhotonNetwork.LeaveRoom();
            
            // After leaving the room, disconnect from the server
            PhotonNetwork.Disconnect();
        }

        // Load the scene
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        
        // Reset UI elements
        if (serverWindow != null)
        {
            serverWindow.SetActive(true);
        }
        
        if (connectionText != null)
        {
            connectionText.text = "Connecting to lobby...";
        }

        // Start the reconnection process
        StartCoroutine(ReconnectAfterReload());
    }

    private IEnumerator ReconnectAfterReload()
    {
        yield return new WaitForSeconds(1f); // Wait a moment for the scene to load
        
        // Reconnect to Photon
        if (!PhotonNetwork.IsConnected)
        {
            PhotonNetwork.ConnectUsingSettings();
            
            // Wait for connection
            while (!PhotonNetwork.IsConnected)
            {
                yield return new WaitForSeconds(0.5f);
            }
            
            // Join the lobby once connected
            PhotonNetwork.JoinLobby();
        }
        
        // Reset UI
        if (serverWindow != null)
        {
            serverWindow.SetActive(true);
        }
        if (connectionText != null)
        {
            connectionText.text = "";
        }

        // Make sure cursor is visible and unlocked
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
}