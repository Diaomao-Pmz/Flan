using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 突进斩：先冲到玩家面前，再打出一套挥击。
/// 第二阶段复用 MeleeSwing 结构，所以近战卡上调好的手感参数可以直接抄过来。
/// </summary>
[CreateAssetMenu(fileName = "NewDashAttackNode", menuName = "ScriptableObjects/DashAttackNode")]
public class DashAttackNode : ActionNode
{
    [Header("--- 第一阶段：位移 ---")]
    [Tooltip("起跳前摇（秒）。玩家靠这段时间读招。")]
    public float windupBeforeDash = 0.35f;

    [Tooltip("冲刺速度")]
    public float dashSpeed = 14f;

    [Tooltip("最远冲刺距离。防止玩家在天涯海角时 Boss 一路狂奔。")]
    public float maxDashDistance = 10f;

    [Tooltip("冲到距玩家这么近就停下，接挥击。太小会让 Boss 冲过头。")]
    public float stopDistance = 1.6f;

    [Tooltip("冲刺超时（秒）。撞不到也走不动时的兜底，防止卡死。")]
    public float dashTimeout = 1.2f;

    [Tooltip("冲刺途中撞墙就停下")]
    public bool stopOnWall = true;

    [Tooltip("落地后、挥击前的停顿（秒）。留一点让玩家看清 Boss 停在哪。")]
    public float pauseAfterDash = 0.1f;

    [Header("--- 第二阶段：挥击 ---")]
    [Tooltip("冲到位之后打出的挥击序列。结构与 MeleeNode 完全一致。")]
    public List<MeleeSwing> swings = new List<MeleeSwing>();

    [Tooltip("勾选后每段挥击重新面向玩家；不勾则沿用冲刺结束时的朝向。")]
    public bool faceTargetEachSwing = false;

    [Header("--- 动画（可选）---")]
    [Tooltip("冲刺阶段的动画名。留空则不切换。")]
    public string dashAnimName;
}
