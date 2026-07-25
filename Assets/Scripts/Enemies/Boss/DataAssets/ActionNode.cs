using UnityEngine;

public class ActionNode : ScriptableObject
{
    [Header("--- AI 决策核心数据 ---")]
    [Tooltip("动作的标识名称（可用于匹配动画、技能名称等）")]
    public string actionName;

    [Tooltip("AI 抽卡时的默认基础权重")]
    public int baseWeight = 100;

    [Tooltip("该技能的冷却时间（秒）")]
    public float cooldown = 0f;

    [Tooltip("允许释放该技能的最小玩家距离")]
    public float minCastDistance = 0f;

    [Tooltip("允许释放该技能的最大玩家距离")]
    public float maxCastDistance = 15f;

    [Header("--- 通用动画配置 ---")]
    [Tooltip("蓄力/前摇动画名")]
    public string chargeAnimName;
    [Tooltip("释放/持续动画名")]
    public string activeAnimName;
    [Tooltip("收招/后摇动画名")]
    public string recoverAnimName;
}
