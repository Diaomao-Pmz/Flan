using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一段挥击。多段串起来就是一套近战连招。
/// 时序：windupTime（前摇）→ hitboxDuration（判定窗口）→ recoverTime（后摇）
/// </summary>
[Serializable]
public class MeleeSwing
{
    [Header("--- 时序 ---")]
    [Tooltip("前摇时长（秒）。这段时间内不产生判定，用于播放起手动作。")]
    public float windupTime = 0.3f;

    [Tooltip("判定窗口时长（秒）。填 0 表示只在单帧做一次瞬间判定。")]
    public float hitboxDuration = 0.1f;

    [Tooltip("后摇时长（秒）。这段时间 Boss 处于硬直，是玩家的输出窗口。")]
    public float recoverTime = 0.2f;

    [Header("--- 判定框 ---")]
    [Tooltip("判定框【底边中点】相对 Boss 脚底（轴心）的偏移。\n" +
             "X 会自动乘以朝向，填正数即可（表示身前）；Y 填 0 = 判定框贴地，往上长出 hitboxSize 的高度。")]
    public Vector2 hitboxOffset = new Vector2(1.5f, 0f);

    [Tooltip("判定框尺寸（宽, 高）。")]
    public Vector2 hitboxSize = new Vector2(2f, 2f);

    [Header("--- 伤害 ---")]
    public int damage = 15;

    [Header("--- 动画（可选）---")]
    [Tooltip("本段挥击的动画名。留空则使用 Node 上的 activeAnimName。")]
    public string swingAnimName;

    /// <summary>判定框最远那条边离 Boss 多远（水平方向）</summary>
    public float FarEdge => Mathf.Abs(hitboxOffset.x) + hitboxSize.x * 0.5f;

    /// <summary>
    /// 一套挥击的"出手距离"：取第一段挥击（起手那一下必须能打到）判定框的最远边。
    /// 没有有效挥击时返回 false。
    /// </summary>
    public static bool TryGetOpenerReach(List<MeleeSwing> swings, out float reach)
    {
        reach = 0f;
        if (swings == null) return false;
        for (int i = 0; i < swings.Count; i++)
        {
            if (swings[i] == null) continue;
            reach = swings[i].FarEdge;
            return true;
        }
        return false;
    }
}

[CreateAssetMenu(fileName = "NewMeleeNode", menuName = "ScriptableObjects/MeleeNode")]
public class MeleeNode : ActionNode
{
    [Header("--- 近战专属配置 ---")]
    [Tooltip("挥击序列。一段就是单次攻击，多段就是连招。")]
    public List<MeleeSwing> swings = new List<MeleeSwing>();

    [Tooltip("勾选后每一段挥击开始前都重新面向玩家（追踪型连招）；" +
             "不勾则整套连招沿用第一段的朝向（可被玩家绕背躲开）。")]
    public bool faceTargetEachSwing = true;

    [Header("--- 射程 ---")]
    [Tooltip("勾选 = 最大射程按判定框自动计算：第一段挥击判定框的最远边 + 下面的余量。\n" +
             "改了判定框大小 / 偏移，射程自动跟着变，不会出现\"觉得够得着其实够不着\"。\n" +
             "不勾 = 用上面手填的 Max Cast Distance")]
    public bool autoRangeFromHitbox = true;

    [Tooltip("自动射程的余量（格）。距离是按双方脚底算的，玩家身体还有半个身宽，\n" +
             "所以余量约等于玩家半身宽时刚好擦到；想让 Boss 贴得更近才出手就调小（可填负数）")]
    public float autoRangeMargin = 0.2f;

    public override float MaxCastDistance
    {
        get
        {
            if (autoRangeFromHitbox && MeleeSwing.TryGetOpenerReach(swings, out float reach))
                return Mathf.Max(minCastDistance, reach + autoRangeMargin);
            return maxCastDistance;
        }
    }
}
