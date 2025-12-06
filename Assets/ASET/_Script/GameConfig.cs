using System;
using UnityEngine;

[Serializable]
public class GameConfig
{
    // ====== 1. Visual & Detection ======

    [Tooltip("Selection Hold Time (Seconds)")]
    public float selectionTimeThreshold = 2.0f;

    [Tooltip("Isolation Distance Threshold")]
    public float isolationDistanceThreshold = 0.08f;

    // ====== 2. Training ======

    [Tooltip("Training Hand (0:Left, 1:Right, 2:Any)")]
    public int trainingHand = 2;

    [Tooltip("Training Rounds (Cards per mode)")]
    public int trainingRounds = 10;

    [Tooltip("Is Fully Random")]
    public bool isFullyRandom = true;

    // =============================

    public static GameConfig GetDefaultConfig()
    {
        return new GameConfig();
    }
}