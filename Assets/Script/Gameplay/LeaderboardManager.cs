using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        CheckAndPerformInitialReset();
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
        if (isTimeout)
        {
            timeSeconds = 0f;
            deaths = 0;
        }

        LevelStatEntry existing = levelStats.Find(x => string.Equals(x.levelName, levelName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.timeSeconds = isTimeout ? 0f : timeSeconds;
            existing.deaths = isTimeout ? 0 : deaths;
            existing.isTimeout = isTimeout;
        }
        else
        {
            levelStats.Add(new LevelStatEntry
            {
                levelName = levelName,
                timeSeconds = isTimeout ? 0f : timeSeconds,
                deaths = isTimeout ? 0 : deaths,
                isTimeout = isTimeout
            });
        }

        RecalculateTotals();
    }

    public const float DEFAULT_RUN_DURATION_SECONDS = 300f; // 5 minutes total

    public float GetOverallRunBudgetSeconds()
    {
        if (LevelTimer.Instance != null)
        {
            return LevelTimer.Instance.OverallRunDurationSeconds;
        }
        return DEFAULT_RUN_DURATION_SECONDS;
    }

    public void DistributeTimeoutLevelTimes()
    {
        // Timed-out levels are strictly 00:00 time and 0 deaths per user specification
        foreach (var stat in levelStats)
        {
            if (stat.isTimeout)
            {
                stat.timeSeconds = 0f;
                stat.deaths = 0;
            }
        }
    }

    private void RecalculateTotals()
    {
        DistributeTimeoutLevelTimes();

        totalRunTime = 0f;
        totalRunDeaths = 0;
        totalRunTimeouts = 0;
        foreach (var stat in levelStats)
        {
            if (stat.isTimeout)
            {
                totalRunTimeouts++;
            }
            else
            {
                totalRunTime += stat.timeSeconds;
                totalRunDeaths += stat.deaths;
            }
        }

        float totalBudget = GetOverallRunBudgetSeconds();
        totalRunTime = Mathf.Clamp(totalRunTime, 0f, totalBudget);
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
        List<string> expectedLevels = new List<string>();
        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (!string.IsNullOrEmpty(sceneName) &&
                !sceneName.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) &&
                !sceneName.Equals("Ending", StringComparison.OrdinalIgnoreCase) &&
                !expectedLevels.Contains(sceneName))
            {
                expectedLevels.Add(sceneName);
            }
        }

        if (expectedLevels.Count == 0)
        {
            expectedLevels.AddRange(new[] { "Level 1", "Level 2", "Level 3", "Level 4" });
        }

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
                    timeSeconds = 0f,
                    deaths = 0,
                    isTimeout = true
                });
            }
        }
        RecalculateTotals();
    }

    public bool SaveCurrentRun(string playerName = "")
    {
        if (hasSavedCurrentRun) return false;
        // If any player has a single timeout level, they cannot be on the global leaderboard
        if (totalRunTimeouts > 0) return false;
        if (levelStats.Count == 0 && totalRunTime <= 0f) return false;

        if (string.IsNullOrWhiteSpace(playerName) || playerName == "Player 1")
        {
            playerName = PlayerPrefs.GetString("PlayerName", "Player");
        }
        if (string.IsNullOrWhiteSpace(playerName))
        {
            playerName = "Player";
        }

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

    private const string LEADERBOARD_RESET_VERSION_KEY = "Leaderboard_CleanReset_v1";

    public void ClearLeaderboardData()
    {
        leaderboardData = new LeaderboardDataWrapper();
        if (PlayerPrefs.HasKey(PREFS_KEY))
        {
            PlayerPrefs.DeleteKey(PREFS_KEY);
            PlayerPrefs.Save();
        }
    }

    private void CheckAndPerformInitialReset()
    {
        if (!PlayerPrefs.HasKey(LEADERBOARD_RESET_VERSION_KEY))
        {
            ClearLeaderboardData();
            PlayerPrefs.SetInt(LEADERBOARD_RESET_VERSION_KEY, 1);
            PlayerPrefs.Save();
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
