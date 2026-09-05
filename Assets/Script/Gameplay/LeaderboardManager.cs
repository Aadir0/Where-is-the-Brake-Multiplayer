using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class LevelStatEntry
{
    public string levelName;
    public float timeSeconds;
    public int deaths;
    public bool isTimeout;
}

[System.Serializable]
public class LeaderboardEntry
{
    public string playerName;
    public float totalTimeSeconds;
    public int totalDeaths;
    public int totalTimeouts;
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
    private static LeaderboardManager _instance;
    public static LeaderboardManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = UnityEngine.Object.FindFirstObjectByType<LeaderboardManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("LeaderboardManager");
                    _instance = go.AddComponent<LeaderboardManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

    private const string PREFS_KEY = "GameLeaderboardData";
    private const int MAX_LEADERBOARD_ENTRIES = 5;
    private const float DEATH_PENALTY_SECONDS = 5.0f;
    private const float TIMEOUT_PENALTY_SECONDS = 15.0f;

    [Header("Current Run Live Stats")]
    [SerializeField] private float totalRunTime = 0f;
    [SerializeField] private int totalRunDeaths = 0;
    [SerializeField] private int totalRunTimeouts = 0;
    [SerializeField] private List<LevelStatEntry> levelStats = new List<LevelStatEntry>();

    private LeaderboardDataWrapper leaderboardData = new LeaderboardDataWrapper();
    private bool hasSavedCurrentRun = false;

    public float TotalRunTime => totalRunTime;
    public int TotalRunDeaths => totalRunDeaths;
    public int TotalRunTimeouts => totalRunTimeouts;
    public IReadOnlyList<LevelStatEntry> LevelStats => levelStats;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        LoadLeaderboardFromPrefs();
    }

    public void ResetRun()
    {
        totalRunTime = 0f;
        totalRunDeaths = 0;
        totalRunTimeouts = 0;
        levelStats.Clear();
        hasSavedCurrentRun = false;
    }

    public void RecordLevelCompletion(string levelName, float timeSeconds, int deaths, bool isTimeout = false)
    {
        LevelStatEntry existing = levelStats.Find(x => string.Equals(x.levelName, levelName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.timeSeconds = timeSeconds;
            existing.deaths = deaths;
            existing.isTimeout = isTimeout;
        }
        else
        {
            levelStats.Add(new LevelStatEntry
            {
                levelName = levelName,
                timeSeconds = timeSeconds,
                deaths = deaths,
                isTimeout = isTimeout
            });
        }

        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        totalRunTime = 0f;
        totalRunDeaths = 0;
        totalRunTimeouts = 0;
        foreach (var stat in levelStats)
        {
            totalRunTime += stat.timeSeconds;
            totalRunDeaths += stat.deaths;
            if (stat.isTimeout) totalRunTimeouts++;
        }
    }

    public float CalculatePerformanceScore(float timeSeconds, int deaths, int timeouts = 0)
    {
        return timeSeconds + (deaths * DEATH_PENALTY_SECONDS) + (timeouts * TIMEOUT_PENALTY_SECONDS);
    }

    public string CalculateGrade(float timeSeconds, int deaths, int timeouts = 0)
    {
        float score = timeSeconds + (deaths * DEATH_PENALTY_SECONDS);
        string baseGrade;
        if (score <= 130f) baseGrade = "S";
        else if (score <= 220f) baseGrade = "A";
        else if (score <= 330f) baseGrade = "B";
        else baseGrade = "C";

        // Rule: Each timeout level reduces 1 rank
        string[] rankOrder = { "S", "A", "B", "C", "D", "F" };
        int baseIndex = Array.IndexOf(rankOrder, baseGrade);
        if (baseIndex < 0) baseIndex = 0;
        int finalIndex = Mathf.Clamp(baseIndex + timeouts, 0, rankOrder.Length - 1);
        return rankOrder[finalIndex];
    }

    public void EnsureAllLevelsRecorded()
    {
        string[] expectedLevels = { "Level 1", "Level 2", "Level 3", "Level 4" };
        foreach (string lvl in expectedLevels)
        {
            bool exists = false;
            foreach (var st in levelStats)
            {
                if (string.Equals(st.levelName, lvl, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                levelStats.Add(new LevelStatEntry
                {
                    levelName = lvl,
                    timeSeconds = 60.0f,
                    deaths = 0,
                    isTimeout = true
                });
            }
        }
        RecalculateTotals();
    }

    public bool SaveCurrentRun(string playerName = "Player 1")
    {
        if (hasSavedCurrentRun) return false;
        if (levelStats.Count == 0 && totalRunTime <= 0f) return false;

        float score = CalculatePerformanceScore(totalRunTime, totalRunDeaths, totalRunTimeouts);
        string grade = CalculateGrade(totalRunTime, totalRunDeaths, totalRunTimeouts);
        string currentDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        LeaderboardEntry entry = new LeaderboardEntry
        {
            playerName = playerName,
            totalTimeSeconds = totalRunTime,
            totalDeaths = totalRunDeaths,
            totalTimeouts = totalRunTimeouts,
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
        if (leaderboardData.entries.Count > MAX_LEADERBOARD_ENTRIES)
        {
            return leaderboardData.entries.GetRange(0, MAX_LEADERBOARD_ENTRIES);
        }
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
