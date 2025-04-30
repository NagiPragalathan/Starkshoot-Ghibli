using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Photon.Pun;

public class LeaderboardEntry : MonoBehaviour {
    public TextMeshProUGUI rankText;
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI killsText;
    public Image backgroundImage;

    [Header("Colors")]
    public Color firstPlaceColor = new Color(1f, 0.92f, 0.4f, 0.5f); // Gold
    public Color secondPlaceColor = new Color(0.85f, 0.85f, 0.85f, 0.5f); // Silver
    public Color thirdPlaceColor = new Color(1f, 0.7f, 0.4f, 0.5f); // Bronze
    public Color defaultColor = new Color(0.13f, 0.15f, 0.18f, 0.3f); // Subtle dark
    public Color localPlayerColor = new Color(0.3f, 0.7f, 1f, 0.5f); // Blue highlight

    void Start() {
        Debug.Log("LeaderboardEntry Start called");
        if (playerNameText == null) Debug.LogError("playerNameText is null!");
        if (scoreText == null) Debug.LogError("scoreText is null!");
        if (killsText == null) Debug.LogError("killsText is null!");
    }

    public void SetStats(string playerName, int score, int kills, int rank) {
        Debug.Log($"Setting stats for {playerName}: Score={score}, Kills={kills}");

        // Rank
        if (rankText != null)
        {
            rankText.text = rank.ToString();
            rankText.fontSize = 100;
            rankText.fontStyle = (rank <= 3) ? FontStyles.Bold : FontStyles.Normal;
        }

        // Player Name
        if (playerNameText != null)
        {
            playerNameText.text = playerName;
            playerNameText.fontSize = 100;
            playerNameText.color = (playerName == PhotonNetwork.LocalPlayer.NickName) ? new Color(1f, 0.95f, 0.6f) : Color.white;
        }

        // Score
        if (scoreText != null)
        {
            scoreText.text = (score > 0) ? score.ToString("N0") : "-|-";
            scoreText.fontSize = 100;
            scoreText.color = Color.white;
        }

        // Kills
        if (killsText != null)
        {
            killsText.text = (kills > 0) ? kills.ToString() : "-|-";
            killsText.fontSize = 100;
            killsText.color = Color.white;
        }

        // Background color by rank
        if (backgroundImage != null)
        {
            if (playerName == PhotonNetwork.LocalPlayer.NickName)
                backgroundImage.color = localPlayerColor;
            else if (rank == 1)
                backgroundImage.color = firstPlaceColor;
            else if (rank == 2)
                backgroundImage.color = secondPlaceColor;
            else if (rank == 3)
                backgroundImage.color = thirdPlaceColor;
            else
                backgroundImage.color = defaultColor;
        }
    }
}