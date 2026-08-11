using UnityEngine;

// 定义传送的战术策略
public enum TeleportTargetType
{
    RandomPoint,    // 随机传送点（一阶段防反）
    BehindPlayer,   // 绕背偷袭
    Center          // 回到场地中央（二阶段转场等）
}

[CreateAssetMenu(fileName = "NewTeleportNode", menuName = "ScriptableObjects/TeleportNode")]
public class TeleportNode : ActionNode
{
    [Header("--- 传送专属配置 ---")]
    [Tooltip("传送前摇（秒）。0 表示瞬发。")]
    public float teleportDelay = 0.2f;

    [Tooltip("AI 决定使用哪种传送策略")]
    public TeleportTargetType targetType = TeleportTargetType.RandomPoint;
}