using System.Collections.Generic;
using System;

[Serializable]
public class SessionData
{
    public string SessionStartTime;
    public List<RoundData> Rounds;
}

[Serializable]
public class RoundData
{
    public string GestureName;
    public float FinalScore;
    public float FinishScore;
    public float ReferenceScore;
    public float RuleScore;
    public float TimeTaken;
    public int mode;
    public int NextDifficulty;
}