using UnityEngine;
using UnityEngine.UI;

public class LeaderboardEntry : MonoBehaviour {
    [SerializeField]
    private Text rankText;
    [SerializeField]
    private Text playerNameText;
    [SerializeField]
    private Text scoreText;
    [SerializeField]
    private Text killsText;

    public void SetStats(string playerName, int score, int kills) {
        if (playerNameText != null) playerNameText.text = playerName;
        if (scoreText != null) scoreText.text = $"Score: {score}";
        if (killsText != null) killsText.text = $"Kills: {kills}";
    }

    public void SetRank(int rank) {
        if (rankText != null) rankText.text = $"#{rank}";
    }
}