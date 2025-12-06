using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using TMPro;
using System.Linq;
using System.Collections;
using System;

public class RandomModeManager : MonoBehaviour
{
    // --- Data Structures ---
    [System.Serializable]
    public class GestureEntry
    {
        public string GestureName;
        public Sprite GestureSprite;
        [HideInInspector] public List<Vector3> RawLandmarks;
    }

    [System.Serializable]
    private class GestureJsonData
    {
        public string GestureId;
        public List<Vector3> Landmarks;
        public string ScreenshotFileName;
    }

    [System.Serializable]
    public class SessionRoundRecord
    {
        public string GestureName;
        public float FinalScore;
        public int FinishScore;
        public int ReferenceScore;
        public int RuleScore;
        public float TimeTaken;
        public Attitude mode;
        public int NextDifficulty;
    }

    [System.Serializable]
    public class GameSessionLog
    {
        public string SessionStartTime;
        public List<SessionRoundRecord> Rounds = new List<SessionRoundRecord>();
    }

    private enum Finger { Thumb, Index, Middle, Ring, Pinky }
    private enum FingerState { Unknown, Straight, Curled }
    public enum Attitude { Neutral, Good, Bad }

    // --- Unity Inspector Variables ---
    [Header("MediaPipe Runner")]
    [SerializeField] private HandLandmarkerRunner handLandmarkerRunner;

    [Header("UI References")]
    [SerializeField] private Image targetImageUI;
    [SerializeField] private TextMeshProUGUI scoreTextUI;
    [SerializeField] private TextMeshProUGUI countdownTextUI;

    [Header("Dynamic Difficulty UI")]
    [SerializeField] private TextMeshProUGUI difficultyTextUI;
    [SerializeField] private TextMeshProUGUI hintTextUI;

    [Header("Audio & Feedback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip moodExpressClip;
    [SerializeField] private AudioClip keepClip;
    [SerializeField] private AudioClip roundSuccessClip;

    [Header("Attitude Mode Assets")]
    public Sprite thumbUpSprite;
    public Sprite thumbDownSprite;

    [Header("Gesture Configuration")]
    [SerializeField] private List<GestureEntry> allGestures = new List<GestureEntry>();

    [Header("Scoring Weights")]
    [Range(0, 1)][SerializeField] private float ruleWeight = 0.6f;
    [Range(0, 1)][SerializeField] private float jsonWeight = 0.4f;

    // --- [Modified] Path Variables ---
    // 训练日志保存路径
    private string LogSavePath
    {
        get
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "ASET/data/");
#else
            return Path.Combine(Application.persistentDataPath, "SavedData");
#endif
        }
    }

    // 录制手势读取路径
    private string GestureRecordPath
    {
        get
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "ASET/record/");
#else
            // 在真机上，我们去沙盒目录找刚才录制的手势
            return Path.Combine(Application.persistentDataPath, "SavedRecordings");
#endif
        }
    }

    private bool _isDataLoaded = false;
    private List<Vector3> targetRawLandmarks;
    private string _currentGestureName = "";

    private List<Vector3> _referenceLikeLandmarks;
    private List<Vector3> _referenceDislikeLandmarks;

    private GestureEntry _nextSelectedGesture;
    private HandLandmarkerResult _latestResult = default;
    private volatile bool _isNewResultAvailable = false;

    private int _currentDifficultyLevel = 1;
    private bool _isInFinalCountdown = false;
    private float _timeToReach60 = 0f;
    private int _highestScoreInFinals = 0;
    private float _countdownTimer = 10f;
    private float _scoreUpdateTimer = 0.5f;
    private bool _isHandVisible = false;
    private int _currentJSONScore = 0;
    private int _currentRuleScore = 0;
    private int _lastCountdownInt = -1;

    private bool _hasRoundEnded = false;

    private bool _isInAttitudeMode = false;
    private float _attitudeCycleTimer = 0f;
    private Attitude _currentAttitude = Attitude.Neutral;
    private Attitude _expressedAttitude = Attitude.Neutral;

    private float _attitudeHoldTimer = 0f;
    private Attitude _pendingAttitude = Attitude.Neutral;
    private const float ATTITUDE_HOLD_REQUIRED_TIME = 3.0f;

    private const float ATTITUDE_SCORE_THRESHOLD = 70f;
    private const float KEEP_SOUND_CD_DURATION = 8.0f;
    private float _keepSoundCooldown = 0f;
    private bool _isAttitudeDetectedForAudio = false;
    private Coroutine _musicCycleCoroutine = null;

    private List<int> _difficultyHistory = new List<int>();
    private const int HISTORY_SIZE = 3;

    private int _lastCompletionScore = 0;
    private Attitude _lastExpressedAttitude = Attitude.Good;
    private float _lastCalculatedFinalScore = 0f;

    private const float MAIN_COUNTDOWN_TIME = 10.0f;
    private const float FINAL_COUNTDOWN_TIME = 3.5f;
    private const float ATTITUDE_COUNTDOWN_TIME = 30.0f;
    private const float ATTITUDE_CYCLE_TIME = 5.0f;
    private const int SCORE_THRESHOLD = 60;
    private const int ATTITUDE_ROUND_INTERVAL = 5;

    private class RoundResult
    {
        public string GestureName;
        public int FinalScore;
    }
    private List<RoundResult> _roundHistory = new List<RoundResult>();
    private int _totalAccumulatedScore = 0;
    private GameSessionLog _currentSessionLog;
    private string _sessionLogFileName;


    void Start()
    {
        if (handLandmarkerRunner == null) return;

        // [Modified] 确保日志文件夹存在
        if (!Directory.Exists(LogSavePath))
        {
            Directory.CreateDirectory(LogSavePath);
        }

        LoadAllTargetLandmarks();
        if (allGestures.Count > 0)
        {
            _isDataLoaded = true;
            UpdateDifficultyUI();
            StartNewGameSession();
            StartCoroutine(ShowLogicAndSetNextGesture());
            handLandmarkerRunner.OnHandLandmarksOutput.AddListener(OnHandLandmarksReceived);
        }
    }

    private void OnApplicationQuit()
    {
        if (_currentSessionLog != null && _currentSessionLog.Rounds.Count > 0) SaveSessionLogToFile();
    }

    void Update()
    {
        if (!_isDataLoaded) return;

        if (_isNewResultAvailable)
        {
            ProcessLandmarks(_latestResult);
            _isNewResultAvailable = false;
        }

        UpdateScoreUI();

        _countdownTimer -= Time.deltaTime;

        if (_keepSoundCooldown > 0f)
        {
            _keepSoundCooldown -= Time.deltaTime;
        }

        if (_isInAttitudeMode)
        {
            ProcessAttitudeMode();
        }
        else if (_isInFinalCountdown)
        {
            ProcessFinalCountdown();
        }
        else
        {
            ProcessNormalCountdown();
            if (!IsCoroutineRunning) UpdateStatsUI();
        }
    }

    private bool ShouldEnterAttitudeMode()
    {
        return (_roundHistory.Count > 0 && (_roundHistory.Count % ATTITUDE_ROUND_INTERVAL == 0));
    }

    private void ProcessNormalCountdown()
    {
        _timeToReach60 += Time.deltaTime;

        int currentFinalScore = (int)(_currentJSONScore * jsonWeight + _currentRuleScore * ruleWeight);

        if (currentFinalScore >= SCORE_THRESHOLD && !_isInFinalCountdown && !_hasRoundEnded)
        {
            EnterFinalCountdown();
        }
        else if (_countdownTimer <= 0f && !_hasRoundEnded)
        {
            _hasRoundEnded = true;
            if (audioSource != null && roundSuccessClip != null)
            {
                audioSource.PlayOneShot(roundSuccessClip);
            }

            _lastCompletionScore = 0;
            _timeToReach60 = MAIN_COUNTDOWN_TIME;

            _lastCalculatedFinalScore = CalculateAlgorithmScore(0, MAIN_COUNTDOWN_TIME, Attitude.Neutral);
            RecordCurrentRoundDetails(0, MAIN_COUNTDOWN_TIME);

            if (ShouldEnterAttitudeMode()) EnterAttitudeMode();
            else
            {
                _lastExpressedAttitude = Attitude.Neutral;
                _currentDifficultyLevel = CalculateNextDifficulty();
                UpdateDifficultyUI();
                StartCoroutine(ShowLogicAndSetNextGesture());
            }
        }
    }

    private void ProcessFinalCountdown()
    {
        int countdownInt = Mathf.FloorToInt(_countdownTimer);
        if (countdownInt < 0) countdownInt = 0;

        if (countdownInt != _lastCountdownInt)
        {
            if (countdownTextUI != null) countdownTextUI.text = countdownInt.ToString();
            if (hintTextUI != null) hintTextUI.text = $"Hold for {countdownInt}s!";
            _lastCountdownInt = countdownInt;
        }

        int currentFinalScore = (int)(_currentJSONScore * jsonWeight + _currentRuleScore * ruleWeight);
        if (currentFinalScore > _highestScoreInFinals) _highestScoreInFinals = currentFinalScore;

        if (_countdownTimer <= 0f && !_hasRoundEnded)
        {
            _hasRoundEnded = true;
            if (audioSource != null && roundSuccessClip != null)
            {
                audioSource.PlayOneShot(roundSuccessClip);
            }

            _lastCompletionScore = _highestScoreInFinals;
            _timeToReach60 = Mathf.Min(_timeToReach60, MAIN_COUNTDOWN_TIME);

            _difficultyHistory.Add(_currentDifficultyLevel);
            if (_difficultyHistory.Count > HISTORY_SIZE) _difficultyHistory.RemoveAt(0);

            _lastCalculatedFinalScore = CalculateAlgorithmScore(_lastCompletionScore, _timeToReach60, _lastExpressedAttitude);

            UpdateUiStatistics(_lastCompletionScore);
            RecordCurrentRoundDetails(_lastCompletionScore, _timeToReach60);

            if (ShouldEnterAttitudeMode()) EnterAttitudeMode();
            else
            {
                _lastExpressedAttitude = Attitude.Neutral;
                _lastCalculatedFinalScore = CalculateAlgorithmScore(_lastCompletionScore, _timeToReach60, Attitude.Neutral);
                _currentDifficultyLevel = CalculateNextDifficulty();
                UpdateDifficultyUI();
                StartCoroutine(ShowLogicAndSetNextGesture());
            }
        }
    }

    private void ProcessAttitudeMode()
    {
        _attitudeCycleTimer -= Time.deltaTime;

        if (_attitudeCycleTimer <= 0f)
        {
            _attitudeCycleTimer = ATTITUDE_CYCLE_TIME;
            _currentAttitude = (_currentAttitude == Attitude.Good) ? Attitude.Bad : Attitude.Good;
            if (targetImageUI != null) targetImageUI.sprite = (_currentAttitude == Attitude.Good) ? thumbUpSprite : thumbDownSprite;
        }

        Attitude currentFrameAttitude = Attitude.Neutral;
        if (_isHandVisible && _latestResult.handLandmarks != null && _latestResult.handLandmarks.Count > 0)
        {
            currentFrameAttitude = GetExpressedAttitude(_latestResult.handLandmarks[0].landmarks);
        }

        if (currentFrameAttitude != Attitude.Neutral)
        {
            _isAttitudeDetectedForAudio = true;
            if (_keepSoundCooldown <= 0f && keepClip != null)
            {
                AudioSource.PlayClipAtPoint(keepClip, Camera.main.transform.position);
                _keepSoundCooldown = KEEP_SOUND_CD_DURATION;
            }

            if (currentFrameAttitude == _pendingAttitude)
            {
                _attitudeHoldTimer += Time.deltaTime;
                float remainingTime = ATTITUDE_HOLD_REQUIRED_TIME - _attitudeHoldTimer;
                if (hintTextUI != null)
                {
                    string attitudeStr = (currentFrameAttitude == Attitude.Good) ? "Good" : "Bad";
                    hintTextUI.text = $"Holding {attitudeStr}... {remainingTime:F1}s";
                }

                if (_attitudeHoldTimer >= ATTITUDE_HOLD_REQUIRED_TIME)
                {
                    _expressedAttitude = currentFrameAttitude;
                    Debug.Log($"Attitude Confirmed: {_expressedAttitude}");
                    _lastExpressedAttitude = _expressedAttitude;
                    FinishAttitudeMode();
                    return;
                }
            }
            else
            {
                _pendingAttitude = currentFrameAttitude;
                _attitudeHoldTimer = 0f;
            }
        }
        else
        {
            _isAttitudeDetectedForAudio = false;
            _pendingAttitude = Attitude.Neutral;
            _attitudeHoldTimer = 0f;
            if (hintTextUI != null) hintTextUI.text = "Show Attitude (Good/Bad)!";
        }

        if (_countdownTimer <= 0f)
        {
            _lastExpressedAttitude = Attitude.Neutral;
            FinishAttitudeMode();
        }
    }

    private void FinishAttitudeMode()
    {
        if (_musicCycleCoroutine != null) StopCoroutine(_musicCycleCoroutine);
        if (audioSource != null) audioSource.Stop();

        _lastCalculatedFinalScore = CalculateAlgorithmScore(_lastCompletionScore, _timeToReach60, _lastExpressedAttitude);
        _isInAttitudeMode = false;
        _currentDifficultyLevel = CalculateNextDifficulty();
        UpdateDifficultyUI();
        StartCoroutine(ShowLogicAndSetNextGesture());
    }

    private void EnterAttitudeMode()
    {
        _isInAttitudeMode = true;
        _isInFinalCountdown = false;
        _highestScoreInFinals = 0;

        _countdownTimer = ATTITUDE_COUNTDOWN_TIME;
        _attitudeCycleTimer = 0f;
        _currentAttitude = Attitude.Good;

        _attitudeHoldTimer = 0f;
        _pendingAttitude = Attitude.Neutral;
        _keepSoundCooldown = 0f;
        _isAttitudeDetectedForAudio = false;

        if (countdownTextUI != null) countdownTextUI.text = "";

        if (_musicCycleCoroutine != null) StopCoroutine(_musicCycleCoroutine);
        _musicCycleCoroutine = StartCoroutine(MusicLoopCoroutine());
    }

    private IEnumerator MusicLoopCoroutine()
    {
        if (audioSource == null || moodExpressClip == null) yield break;

        while (_isInAttitudeMode)
        {
            audioSource.clip = moodExpressClip;
            audioSource.Play();

            float playTimer = 0f;
            float clipLength = moodExpressClip.length;

            while (playTimer < clipLength)
            {
                if (!_isInAttitudeMode) yield break;

                if (_isAttitudeDetectedForAudio)
                {
                    if (audioSource.isPlaying) audioSource.Pause();
                }
                else
                {
                    if (!audioSource.isPlaying) audioSource.UnPause();
                }

                playTimer += Time.deltaTime;
                yield return null;
            }

            audioSource.Stop();
            float waitTimer = 0f;
            while (waitTimer < 5.0f)
            {
                if (!_isInAttitudeMode) yield break;
                waitTimer += Time.deltaTime;
                yield return null;
            }
        }
    }

    private void EnterFinalCountdown()
    {
        _isInFinalCountdown = true;
        _countdownTimer = FINAL_COUNTDOWN_TIME;
        _highestScoreInFinals = (int)(_currentJSONScore * jsonWeight + _currentRuleScore * ruleWeight);
        _lastCountdownInt = Mathf.FloorToInt(_countdownTimer);
        if (countdownTextUI != null) countdownTextUI.text = _lastCountdownInt.ToString();
    }

    private float CalculateAlgorithmScore(int finishScore, float timeTaken, Attitude attitude)
    {
        float scoreFactor = Mathf.Clamp01(finishScore / 100.0f);
        float timeFactor = Mathf.Clamp01(1.0f - (timeTaken / MAIN_COUNTDOWN_TIME));

        float attitudeVal = 0.5f;
        if (attitude == Attitude.Good) attitudeVal = 1.0f;
        else if (attitude == Attitude.Bad) attitudeVal = 0.0f;

        float rawScore = (scoreFactor * 0.4f) + (timeFactor * 0.4f) + (attitudeVal * 0.2f);
        return rawScore * 100.0f;
    }

    private int CalculateNextDifficulty()
    {
        float finalScore = _lastCalculatedFinalScore;
        int nextDifficulty = _currentDifficultyLevel;

        bool isThreeConsecutiveSame = false;
        if (_difficultyHistory.Count >= 3)
        {
            int lastDiff = _difficultyHistory[_difficultyHistory.Count - 1];
            if (_difficultyHistory[_difficultyHistory.Count - 2] == lastDiff &&
                _difficultyHistory[_difficultyHistory.Count - 3] == lastDiff)
            {
                isThreeConsecutiveSame = true;
            }
        }

        if (isThreeConsecutiveSame)
        {
            int currentDiff = _currentDifficultyLevel;
            if (currentDiff == 5) return 3;

            if (finalScore >= 60) nextDifficulty = Mathf.Min(currentDiff + 1, 5);
            else nextDifficulty = Mathf.Max(currentDiff - 1, 1);

            return nextDifficulty;
        }

        if (finalScore >= 80) nextDifficulty = Mathf.Min(_currentDifficultyLevel + 2, 5);
        else if (finalScore >= 60) nextDifficulty = Mathf.Min(_currentDifficultyLevel + 1, 5);
        else
        {
            bool dropDifficulty = UnityEngine.Random.Range(0f, 1f) > 0.5f;
            if (dropDifficulty) nextDifficulty = Mathf.Max(_currentDifficultyLevel - 1, 1);
            else nextDifficulty = _currentDifficultyLevel;
        }

        return nextDifficulty;
    }

    private Coroutine _logicCoroutine = null;
    private bool IsCoroutineRunning => _logicCoroutine != null;

    private IEnumerator ShowLogicAndSetNextGesture()
    {
        if (_logicCoroutine != null) yield break;
        _logicCoroutine = StartCoroutine(DoLogicPauseInternal());
    }

    private IEnumerator DoLogicPauseInternal()
    {
        PrepareNextGesture();
        yield return new WaitForSeconds(1.0f);
        ActivateNewGestureState();
        _logicCoroutine = null;
    }

    private string GenerateLogicChainString()
    {
        string historyString = "--- Round Statistics ---\n";
        if (_roundHistory.Count == 0) historyString += "No rounds completed yet.";
        else
        {
            float averageScore = (float)_totalAccumulatedScore / _roundHistory.Count;
            historyString += $"Completed Gestures: {_roundHistory.Count}\n";
            historyString += $"Total Score: {_totalAccumulatedScore}\n";
            historyString += $"Average Score: {averageScore:F2}";
        }
        return historyString;
    }

    private void UpdateStatsUI()
    {
        string stats = GenerateLogicChainString();
        if (hintTextUI != null) hintTextUI.text = stats;
    }

    private void UpdateUiStatistics(int finalScore)
    {
        _roundHistory.Add(new RoundResult { GestureName = _currentGestureName, FinalScore = finalScore });
        _totalAccumulatedScore += finalScore;
    }

    private void UpdateDifficultyUI()
    {
        if (difficultyTextUI != null) difficultyTextUI.text = $"Difficulty: {_currentDifficultyLevel}";
    }

    private void UpdateScoreUI()
    {
        _scoreUpdateTimer -= Time.deltaTime;
        if (_scoreUpdateTimer <= 0f)
        {
            _scoreUpdateTimer = 0.5f;
            if (scoreTextUI != null)
            {
                if (!_isHandVisible) scoreTextUI.text = "Current Score: 0";
                else
                {
                    int finalScore = (int)(_currentJSONScore * jsonWeight + _currentRuleScore * ruleWeight);
                    scoreTextUI.text = $"Current Score: {finalScore}";
                }
            }
        }
    }

    private Attitude GetExpressedAttitude(IList<NormalizedLandmark> landmarks)
    {
        GetHandVectors(landmarks, "Right", out Vector2 handUp, out Vector2 handRight);

        float ruleLike = ScoreRule_Like(landmarks, GetFingerStates(landmarks), handUp);
        float ruleDislike = ScoreRule_Dislike(landmarks, GetFingerStates(landmarks), handUp);

        int jsonLike = 0;
        int jsonDislike = 0;
        if (_referenceLikeLandmarks != null) jsonLike = CalculateJSONScore(landmarks, _referenceLikeLandmarks);
        if (_referenceDislikeLandmarks != null) jsonDislike = CalculateJSONScore(landmarks, _referenceDislikeLandmarks);

        float finalLikeScore = Mathf.Max(ruleLike, jsonLike);
        float finalDislikeScore = Mathf.Max(ruleDislike, jsonDislike);

        float thumbTipY = landmarks[4].y;
        float wristY = landmarks[0].y;
        bool isHandUp = (thumbTipY < (wristY - 0.1f));
        bool isHandDown = (thumbTipY > (wristY + 0.1f));

        if (finalLikeScore >= ATTITUDE_SCORE_THRESHOLD && isHandUp) return Attitude.Good;
        if (finalDislikeScore >= ATTITUDE_SCORE_THRESHOLD && isHandDown) return Attitude.Bad;

        return Attitude.Neutral;
    }

    private void StartNewGameSession()
    {
        _roundHistory.Clear();
        _difficultyHistory.Clear();
        _difficultyHistory.Add(1);
        _totalAccumulatedScore = 0;
        _sessionLogFileName = "random_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
        _currentSessionLog = new GameSessionLog { SessionStartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        UpdateStatsUI();
    }

    private void RecordCurrentRoundDetails(int finishScore, float time)
    {
        float roundedTime = Mathf.Round(time * 100f) / 100f;
        float finalScore = CalculateAlgorithmScore(finishScore, time, _lastExpressedAttitude);

        var record = new SessionRoundRecord
        {
            GestureName = _currentGestureName,
            FinishScore = finishScore,
            FinalScore = (float)Math.Round(finalScore, 2),
            ReferenceScore = _currentJSONScore,
            RuleScore = _currentRuleScore,
            TimeTaken = roundedTime,
            mode = _lastExpressedAttitude,
            NextDifficulty = CalculateNextDifficulty()
        };

        if (_currentSessionLog != null)
        {
            _currentSessionLog.Rounds.Add(record);
            Debug.Log($"Recorded: {record.GestureName}, Finish: {finishScore}, Final: {record.FinalScore}, Next: {record.NextDifficulty}");
        }
    }

    private void SaveSessionLogToFile()
    {
        try
        {
            // [Modified] 使用兼容的 LogSavePath
            string directoryPath = LogSavePath;
            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

            string fullPath = Path.Combine(directoryPath, _sessionLogFileName);
            string jsonString = JsonUtility.ToJson(_currentSessionLog, true);
            File.WriteAllText(fullPath, jsonString);
            Debug.Log($"Log saved: {fullPath}");
        }
        catch (System.Exception e) { Debug.LogError($"Log save failed: {e.Message}"); }
    }

    private void OnHandLandmarksReceived(HandLandmarkerResult result)
    {
        if (IsCoroutineRunning) return;
        _latestResult = result;
        _isNewResultAvailable = true;
    }

    private void ProcessLandmarks(HandLandmarkerResult result)
    {
        if (!_isDataLoaded) return;
        if (targetRawLandmarks == null) return;

        if (result.handLandmarks == null || result.handLandmarks.Count == 0)
        {
            _isHandVisible = false;
            _currentJSONScore = 0;
            _currentRuleScore = 0;
            return;
        }

        _isHandVisible = true;
        var userLandmarksRaw = result.handLandmarks[0].landmarks;
        string handedness = result.handedness[0].categories[0].categoryName;

        _currentJSONScore = CalculateJSONScore(userLandmarksRaw, targetRawLandmarks);
        _currentRuleScore = CalculateStaticRuleScore(_currentGestureName, userLandmarksRaw, handedness);
    }

    public void PrepareNextGesture()
    {
        if (allGestures.Count == 0) return;
        string difficultyPrefix = _currentDifficultyLevel.ToString();
        var availableGestures = allGestures.Where(g => g.GestureName.StartsWith(difficultyPrefix)).ToList();
        if (availableGestures.Count == 0) availableGestures = allGestures;
        int randomIndex = UnityEngine.Random.Range(0, availableGestures.Count);
        _nextSelectedGesture = availableGestures[randomIndex];
        if (targetImageUI != null) targetImageUI.sprite = _nextSelectedGesture.GestureSprite;
    }

    public void ActivateNewGestureState()
    {
        if (_nextSelectedGesture == null || _nextSelectedGesture.GestureName == null)
        {
            if (allGestures.Count > 0) _nextSelectedGesture = allGestures[0];
            else return;
        }

        targetRawLandmarks = _nextSelectedGesture.RawLandmarks;
        _currentGestureName = _nextSelectedGesture.GestureName;

        if (scoreTextUI != null) scoreTextUI.text = "Score: 0";
        if (countdownTextUI != null) countdownTextUI.text = "";

        _currentJSONScore = 0;
        _currentRuleScore = 0;
        _isHandVisible = false;
        _countdownTimer = MAIN_COUNTDOWN_TIME;
        _isInFinalCountdown = false;
        _timeToReach60 = 0f;
        _highestScoreInFinals = 0;
        _isInAttitudeMode = false;
        _lastCountdownInt = -1;
        _hasRoundEnded = false;

        Debug.Log($"Activated: {_currentGestureName}");
    }

    private void LoadAllTargetLandmarks()
    {
        // [Modified] 使用兼容的 GestureRecordPath
        string recordPath = GestureRecordPath;
        if (!Directory.Exists(recordPath))
        {
            Debug.LogWarning($"Record path not found: {recordPath}");
            return;
        }

        string[] files = Directory.GetFiles(recordPath, "*.json");

        foreach (string file in files)
        {
            try
            {
                string jsonString = File.ReadAllText(file);
                GestureJsonData data = JsonUtility.FromJson<GestureJsonData>(jsonString);

                if (data.Landmarks == null || data.Landmarks.Count != 21) continue;

                string fileName = Path.GetFileNameWithoutExtension(file).ToLower();

                if (fileName.Contains("like") && !fileName.Contains("dislike"))
                {
                    _referenceLikeLandmarks = data.Landmarks;
                }
                else if (fileName.Contains("dislike"))
                {
                    _referenceDislikeLandmarks = data.Landmarks;
                }

                foreach (var entry in allGestures)
                {
                    if (file.EndsWith("gesture_" + entry.GestureName + ".json"))
                    {
                        entry.RawLandmarks = data.Landmarks;
                    }
                }
            }
            catch (System.Exception) { }
        }

        for (int i = allGestures.Count - 1; i >= 0; i--)
        {
            if (allGestures[i].RawLandmarks == null || allGestures[i].RawLandmarks.Count != 21)
                allGestures.RemoveAt(i);
        }
    }

    // --- Scoring Systems (JSON & Rule) ---
    #region JSON Scoring
    private int CalculateJSONScore(IList<NormalizedLandmark> userLandmarksRaw, List<Vector3> targetLandmarksRaw)
    {
        float[][] userLandmarks = NormalizeLandmarks_Live(userLandmarksRaw);
        float[][] targetLandmarks = NormalizeLandmarks_JSON(targetLandmarksRaw);
        if (userLandmarks == null || targetLandmarks == null) return 0;
        float totalDistance = 0f;
        for (int i = 0; i < 21; i++)
        {
            float dx = userLandmarks[i][0] - targetLandmarks[i][0];
            float dy = userLandmarks[i][1] - targetLandmarks[i][1];
            float dz = userLandmarks[i][2] - targetLandmarks[i][2];
            totalDistance += Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        const float MAX_DISTANCE_THRESHOLD = 5.0f;
        float clampedDistance = Mathf.Clamp(totalDistance, 0f, MAX_DISTANCE_THRESHOLD);
        return Mathf.RoundToInt(100f * (1f - (clampedDistance / MAX_DISTANCE_THRESHOLD)));
    }

    private float[][] NormalizeLandmarks_Live(IList<NormalizedLandmark> landmarks)
    {
        if (landmarks.Count != 21) return null;
        var p0_vec = new Vector3(landmarks[0].x, landmarks[0].y, landmarks[0].z);
        var p9_vec = new Vector3(landmarks[9].x, landmarks[9].y, landmarks[9].z);
        float scaleRef = Vector3.Distance(p0_vec, p9_vec);
        if (scaleRef < 0.001f) scaleRef = 1.0f;
        float[][] normalized = new float[21][];
        for (int i = 0; i < 21; i++)
        {
            var p = landmarks[i];
            normalized[i] = new float[3];
            normalized[i][0] = (p.x - p0_vec.x) / scaleRef;
            normalized[i][1] = (p.y - p0_vec.y) / scaleRef;
            normalized[i][2] = (p.z - p0_vec.z) / scaleRef;
        }
        return normalized;
    }

    private float[][] NormalizeLandmarks_JSON(List<Vector3> landmarks)
    {
        if (landmarks == null || landmarks.Count < 21) return null;
        var p0_vec = landmarks[0];
        var p9_vec = landmarks[9];
        float scaleRef = Vector3.Distance(p0_vec, p9_vec);
        if (scaleRef < 0.001f) scaleRef = 1.0f;
        float[][] normalized = new float[21][];
        for (int i = 0; i < 21; i++)
        {
            var p = landmarks[i];
            normalized[i] = new float[3];
            normalized[i][0] = (p.x - p0_vec.x) / scaleRef;
            normalized[i][1] = (p.y - p0_vec.y) / scaleRef;
            normalized[i][2] = (p.z - p0_vec.z) / scaleRef;
        }
        return normalized;
    }
    #endregion

    #region Static Rule Engine
    private const float FINGER_STRAIGHT_THRESHOLD = 1.6f;
    private const float FINGER_CURLED_THRESHOLD = 1.3f;
    private const float THUMB_STRAIGHT_THRESHOLD = 1.3f;
    private const float THUMB_CURLED_THRESHOLD = 1.1f;
    private const float OK_DISTANCE_THRESHOLD = 0.08f;
    private const float DIRECTION_DOT_THRESHOLD = 0.6f;

    private Vector2 GetVector2(NormalizedLandmark lm) { return new Vector2(lm.x, lm.y); }

    private void GetHandVectors(IList<NormalizedLandmark> landmarks, string handedness, out Vector2 handUp, out Vector2 handRight)
    {
        Vector2 wrist = GetVector2(landmarks[0]);
        Vector2 indexKnuckle = GetVector2(landmarks[5]);
        Vector2 middleKnuckle = GetVector2(landmarks[9]);
        handUp = (middleKnuckle - wrist).normalized;
        if (handedness == "Right") handRight = (indexKnuckle - middleKnuckle).normalized;
        else handRight = (middleKnuckle - indexKnuckle).normalized;
    }

    private Vector2 GetNormalizedDirection(NormalizedLandmark tip, NormalizedLandmark baseKnuckle)
    {
        return (GetVector2(tip) - GetVector2(baseKnuckle)).normalized;
    }

    private float GetDistance(NormalizedLandmark p1, NormalizedLandmark p2)
    {
        return Vector2.Distance(GetVector2(p1), GetVector2(p2));
    }

    private FingerState GetFingerState(IList<NormalizedLandmark> landmarks, Finger finger)
    {
        var wrist = landmarks[0];
        NormalizedLandmark tip, knuckle;
        float straightThreshold, curledThreshold;

        switch (finger)
        {
            case Finger.Thumb:
                tip = landmarks[4]; knuckle = landmarks[2];
                straightThreshold = THUMB_STRAIGHT_THRESHOLD; curledThreshold = THUMB_CURLED_THRESHOLD;
                break;
            case Finger.Index:
                tip = landmarks[8]; knuckle = landmarks[5];
                straightThreshold = FINGER_STRAIGHT_THRESHOLD; curledThreshold = FINGER_CURLED_THRESHOLD;
                break;
            case Finger.Middle:
                tip = landmarks[12]; knuckle = landmarks[9];
                straightThreshold = FINGER_STRAIGHT_THRESHOLD; curledThreshold = FINGER_CURLED_THRESHOLD;
                break;
            case Finger.Ring:
                tip = landmarks[16]; knuckle = landmarks[13];
                straightThreshold = FINGER_STRAIGHT_THRESHOLD; curledThreshold = FINGER_CURLED_THRESHOLD;
                break;
            case Finger.Pinky:
            default:
                tip = landmarks[20]; knuckle = landmarks[17];
                straightThreshold = FINGER_STRAIGHT_THRESHOLD; curledThreshold = FINGER_CURLED_THRESHOLD;
                break;
        }

        float dist_tip_wrist = GetDistance(tip, wrist);
        float dist_knuckle_wrist = GetDistance(knuckle, wrist);

        if (dist_knuckle_wrist < 0.01f) return FingerState.Unknown;
        float ratio = dist_tip_wrist / dist_knuckle_wrist;

        if (ratio > straightThreshold) return FingerState.Straight;
        if (ratio < curledThreshold) return FingerState.Curled;
        return FingerState.Unknown;
    }

    private Dictionary<Finger, FingerState> GetFingerStates(IList<NormalizedLandmark> landmarks)
    {
        return new Dictionary<Finger, FingerState>
        {
            [Finger.Thumb] = GetFingerState(landmarks, Finger.Thumb),
            [Finger.Index] = GetFingerState(landmarks, Finger.Index),
            [Finger.Middle] = GetFingerState(landmarks, Finger.Middle),
            [Finger.Ring] = GetFingerState(landmarks, Finger.Ring),
            [Finger.Pinky] = GetFingerState(landmarks, Finger.Pinky)
        };
    }

    private int CalculateStaticRuleScore(string gestureNameWithPrefix, IList<NormalizedLandmark> landmarks, string handedness)
    {
        if (landmarks == null || landmarks.Count != 21) return 0;
        string ruleName = new string(gestureNameWithPrefix.SkipWhile(c => char.IsDigit(c)).ToArray());
        var states = GetFingerStates(landmarks);
        GetHandVectors(landmarks, handedness, out Vector2 handUp, out Vector2 handRight);

        switch (ruleName)
        {
            case "fist": case "grabbing": return ScoreRule_Fist(states);
            case "like": return ScoreRule_Like(landmarks, states, handUp);
            case "dislike": return ScoreRule_Dislike(landmarks, states, handUp);
            case "one": return ScoreRule_One(landmarks, states, handUp);
            case "point": return ScoreRule_Point(landmarks, states, handUp, handRight);
            case "peace": case "two_up": return ScoreRule_Peace(states);
            case "palm": case "stop": return ScoreRule_Palm(states);
            case "three": return ScoreRule_Three(states);
            case "four": return ScoreRule_Four(states);
            case "ok": return ScoreRule_Ok(landmarks, states);
            case "call": return ScoreRule_Call(states);
            case "rock": return ScoreRule_Rock(states);
            case "Fingers crossed": return ScoreRule_FingersCrossed(landmarks, states, handRight, handedness);
            default: return 50;
        }
    }

    private int ScoreRule_Fist(Dictionary<Finger, FingerState> states)
    {
        float score = 0;
        if (states[Finger.Index] == FingerState.Curled) score += 25;
        if (states[Finger.Middle] == FingerState.Curled) score += 25;
        if (states[Finger.Ring] == FingerState.Curled) score += 25;
        if (states[Finger.Pinky] == FingerState.Curled) score += 25;
        return (int)score;
    }

    private int ScoreRule_Like(IList<NormalizedLandmark> landmarks, Dictionary<Finger, FingerState> states, Vector2 handUp)
    {
        float score = 0;
        if (states[Finger.Index] == FingerState.Curled) score += 12.5f;
        if (states[Finger.Middle] == FingerState.Curled) score += 12.5f;
        if (states[Finger.Ring] == FingerState.Curled) score += 12.5f;
        if (states[Finger.Pinky] == FingerState.Curled) score += 12.5f;
        if (states[Finger.Thumb] == FingerState.Straight) score += 25;
        Vector2 thumbDir = GetNormalizedDirection(landmarks[4], landmarks[2]);
        if (Vector2.Dot(thumbDir, handUp) > DIRECTION_DOT_THRESHOLD) score += 25;
        return (int)score;
    }

    private int ScoreRule_Dislike(IList<NormalizedLandmark> landmarks, Dictionary<Finger, FingerState> states, Vector2 handUp)
    {
        float score = 0;
        if (states[Finger.Index] == FingerState.Curled) score += 12.5f;
        if (states[Finger.Middle] == FingerState.Curled) score += 12.5f;
        if (states[Finger.Ring] == FingerState.Curled) score += 12.5f;
        if (states[Finger.Pinky] == FingerState.Curled) score += 12.5f;
        if (states[Finger.Thumb] == FingerState.Straight) score += 25;
        Vector2 thumbDir = GetNormalizedDirection(landmarks[4], landmarks[2]);
        if (Vector2.Dot(thumbDir, handUp) < -DIRECTION_DOT_THRESHOLD) score += 25;
        return (int)score;
    }

    private int ScoreRule_One(IList<NormalizedLandmark> landmarks, Dictionary<Finger, FingerState> states, Vector2 handUp)
    {
        float score = 0;
        if (states[Finger.Index] == FingerState.Straight) score += 40;
        if (states[Finger.Middle] == FingerState.Curled) score += 15;
        if (states[Finger.Ring] == FingerState.Curled) score += 15;
        if (states[Finger.Pinky] == FingerState.Curled) score += 15;
        Vector2 indexDir = GetNormalizedDirection(landmarks[8], landmarks[5]);
        if (Vector2.Dot(indexDir, handUp) > DIRECTION_DOT_THRESHOLD) score += 15;
        return (int)score;
    }

    private int ScoreRule_Point(IList<NormalizedLandmark> landmarks, Dictionary<Finger, FingerState> states, Vector2 handUp, Vector2 handRight)
    {
        float score = 0;
        if (states[Finger.Index] == FingerState.Straight) score += 40;
        if (states[Finger.Middle] == FingerState.Curled) score += 15;
        if (states[Finger.Ring] == FingerState.Curled) score += 15;
        if (states[Finger.Pinky] == FingerState.Curled) score += 15;
        Vector2 indexDir = GetNormalizedDirection(landmarks[8], landmarks[5]);
        if (Mathf.Abs(Vector2.Dot(indexDir, handRight)) > DIRECTION_DOT_THRESHOLD) score += 15;
        return (int)score;
    }

    private int ScoreRule_Peace(Dictionary<Finger, FingerState> states)
    {
        float score = 0;
        if (states[Finger.Index] == FingerState.Straight) score += 35;
        if (states[Finger.Middle] == FingerState.Straight) score += 35;
        if (states[Finger.Ring] == FingerState.Curled) score += 15;
        if (states[Finger.Pinky] == FingerState.Curled) score += 15;
        return (int)score;
    }

    private int ScoreRule_Palm(Dictionary<Finger, FingerState> states)
    {
        float score = 0;
        if (states[Finger.Thumb] == FingerState.Straight) score += 20;
        if (states[Finger.Index] == FingerState.Straight) score += 20;
        if (states[Finger.Middle] == FingerState.Straight) score += 20;
        if (states[Finger.Ring] == FingerState.Straight) score += 20;
        if (states[Finger.Pinky] == FingerState.Straight) score += 20;
        return (int)score;
    }

    private int ScoreRule_Three(Dictionary<Finger, FingerState> states)
    {
        float score = 0;
        if (states[Finger.Index] == FingerState.Straight) score += 30;
        if (states[Finger.Middle] == FingerState.Straight) score += 30;
        if (states[Finger.Ring] == FingerState.Straight) score += 30;
        if (states[Finger.Pinky] == FingerState.Curled) score += 10;
        return (int)score;
    }

    private int ScoreRule_Four(Dictionary<Finger, FingerState> states)
    {
        float score = 0;
        if (states[Finger.Thumb] == FingerState.Curled) score += 20;
        if (states[Finger.Index] == FingerState.Straight) score += 20;
        if (states[Finger.Middle] == FingerState.Straight) score += 20;
        if (states[Finger.Ring] == FingerState.Straight) score += 20;
        if (states[Finger.Pinky] == FingerState.Straight) score += 20;
        return (int)score;
    }

    private int ScoreRule_Ok(IList<NormalizedLandmark> landmarks, Dictionary<Finger, FingerState> states)
    {
        float score = 0;
        if (states[Finger.Middle] == FingerState.Straight) score += 20;
        if (states[Finger.Ring] == FingerState.Straight) score += 20;
        if (states[Finger.Pinky] == FingerState.Straight) score += 20;
        float tipDistance = GetDistance(landmarks[4], landmarks[8]);
        if (tipDistance < OK_DISTANCE_THRESHOLD) score += 40;
        return (int)score;
    }

    private int ScoreRule_Call(Dictionary<Finger, FingerState> states)
    {
        float score = 0;
        if (states[Finger.Thumb] == FingerState.Straight) score += 30;
        if (states[Finger.Pinky] == FingerState.Straight) score += 30;
        if (states[Finger.Index] == FingerState.Curled) score += 13.3f;
        if (states[Finger.Middle] == FingerState.Curled) score += 13.3f;
        if (states[Finger.Ring] == FingerState.Curled) score += 13.3f;
        return (int)score;
    }

    private int ScoreRule_Rock(Dictionary<Finger, FingerState> states)
    {
        float score = 0;
        if (states[Finger.Index] == FingerState.Straight) score += 35;
        if (states[Finger.Pinky] == FingerState.Straight) score += 35;
        if (states[Finger.Middle] == FingerState.Curled) score += 15;
        if (states[Finger.Ring] == FingerState.Curled) score += 15;
        return (int)score;
    }

    private int ScoreRule_FingersCrossed(IList<NormalizedLandmark> landmarks, Dictionary<Finger, FingerState> states, Vector2 handRight, string handedness)
    {
        float score = 0;
        if (states[Finger.Ring] == FingerState.Curled) score += 15;
        if (states[Finger.Pinky] == FingerState.Curled) score += 15;
        if (states[Finger.Index] == FingerState.Straight) score += 15;
        if (states[Finger.Middle] == FingerState.Straight) score += 15;

        Vector2 indexDir = GetNormalizedDirection(landmarks[8], landmarks[5]);
        Vector2 middleDir = GetNormalizedDirection(landmarks[12], landmarks[9]);

        float indexDot = Vector2.Dot(indexDir, handRight);
        float middleDot = Vector2.Dot(middleDir, handRight);

        bool isCrossed = false;
        if (handedness == "Right")
        {
            if (indexDot > 0.1f && middleDot < -0.1f) isCrossed = true;
        }
        else
        {
            if (indexDot < -0.1f && middleDot > 0.1f) isCrossed = true;
        }

        if (isCrossed) score += 40;
        return (int)score;
    }
    #endregion
}