using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class LevelStatEntry
{
    public string levelName;
    public float timeSeconds;
    public int deaths;
}

[System.Serializable]
public class LeaderboardEntry
{
    public string playerName;
    public float totalTimeSeconds;
    public int totalDeaths;
    public float score;
    public string grade;
    public string dateString;
}

[System.Serializable]
public class LeaderboardDataWrapper
{
    public List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
}

public class LeaderboardManager : MonoBehaviour
{
    public static LeaderboardManager Instance { get; private set; }

    private const string PREFS_KEY = "GameLeaderboardData";
    private const int MAX_LEADERBOARD_ENTRIES = 10;
    private const float DEATH_PENALTY_SECONDS = 5.0f;

    [Header("Current Run Live Stats")]
    [SerializeField] private float totalRunTime = 0f;
    [SerializeField] private int totalRunDeaths = 0;
    [SerializeField] private List<LevelStatEntry> levelStats = new List<LevelStatEntry>();

    private LeaderboardDataWrapper leaderboardData = new LeaderboardDataWrapper();
    private bool hasSavedCurrentRun = false;

    public float TotalRunTime => totalRunTime;
    public int TotalRunDeaths => totalRunDeaths;
    public IReadOnlyList<LevelStatEntry> LevelStats => levelStats;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadLeaderboardFromPrefs();
    }

    public void ResetRun()
    {
        totalRunTime = 0f;
        totalRunDeaths = 0;
        levelStats.Clear();
        hasSavedCurrentRun = false;
    }

    public void RecordLevelCompletion(string levelName, float timeSeconds, int deaths)
    {
        LevelStatEntry existing = levelStats.Find(x => string.Equals(x.levelName, levelName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            totalRunTime -= existing.timeSeconds;
            totalRunDeaths -= existing.deaths;
            existing.timeSeconds = timeSeconds;
            existing.deaths = deaths;
        }
        else
        {
            levelStats.Add(new LevelStatEntry
            {
                levelName = levelName,
                timeSeconds = timeSeconds,
                deaths = deaths
            });
        }

        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        totalRunTime = 0f;
        totalRunDeaths = 0;
        foreach (var stat in levelStats)
        {
            totalRunTime += stat.timeSeconds;
            totalRunDeaths += stat.deaths;
        }
    }

    public float CalculatePerformanceScore(float timeSeconds, int deaths)
    {
        return timeSeconds + (deaths * DEATH_PENALTY_SECONDS);
    }

    public string CalculateGrade(float timeSeconds, int deaths)
    {
        float score = CalculatePerformanceScore(timeSeconds, deaths);
        if (score <= 120f) return "S";
        if (score <= 240f) return "A";
        if (score <= 400f) return "B";
        return "C";
    }

    public bool SaveCurrentRun(string playerName = "Player 1")
    {
        if (hasSavedCurrentRun) return false;
        if (levelStats.Count == 0 && totalRunTime <= 0f) return false;

        float score = CalculatePerformanceScore(totalRunTime, totalRunDeaths);
        string grade = CalculateGrade(totalRunTime, totalRunDeaths);
        string currentDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        LeaderboardEntry entry = new LeaderboardEntry
        {
            playerName = playerName,
            totalTimeSeconds = totalRunTime,
            totalDeaths = totalRunDeaths,
            score = score,
            grade = grade,
            dateString = currentDate
        };

        leaderboardData.entries.Add(entry);
        leaderboardData.entries.Sort((a, b) => a.score.CompareTo(b.score));

        if (leaderboardData.entries.Count > MAX_LEADERBOARD_ENTRIES)
        {
            leaderboardData.entries.RemoveRange(MAX_LEADERBOARD_ENTRIES, leaderboardData.entries.Count - MAX_LEADERBOARD_ENTRIES);
        }

        SaveLeaderboardToPrefs();
        hasSavedCurrentRun = true;
        return true;
    }

    public List<LeaderboardEntry> GetTopEntries()
    {
        return new List<LeaderboardEntry>(leaderboardData.entries);
    }

    private void LoadLeaderboardFromPrefs()
    {
        if (PlayerPrefs.HasKey(PREFS_KEY))
        {
            string json = PlayerPrefs.GetString(PREFS_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    leaderboardData = JsonUtility.FromJson<LeaderboardDataWrapper>(json);
                    if (leaderboardData == null) leaderboardData = new LeaderboardDataWrapper();
                }
                catch
                {
                    leaderboardData = new LeaderboardDataWrapper();
                }
            }
        }
    }

    private void SaveLeaderboardToPrefs()
    {
        try
        {
            string json = JsonUtility.ToJson(leaderboardData);
            PlayerPrefs.SetString(PREFS_KEY, json);
            PlayerPrefs.Save();
        }
        catch
        {
        }
    }
}
