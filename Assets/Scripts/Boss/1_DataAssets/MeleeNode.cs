using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 挥击方向。【只影响占位动画】，不会自动旋转判定框。
/// </summary>
public enum MeleeSwingDirection
{
    /// <summary>竖直：前摇往上举，攻击阶段砸下来</summary>
    [InspectorName("竖直（举起 → 砸下）")]
    Vertical,

    /// <summary>水平：前摇往 Boss 身后拉，攻击阶段横着甩到判定框</summary>
    [InspectorName("水平（后拉 → 横扫）")]
    Horizontal,
}

/// <summary>
/// 一段挥击。多段串起来就是一套近战连招。
/// 时序：windupTime（前摇）→ holdTime（蓄势：每次随机憋 0 ~ holdTime 秒）→ attackTime（攻击：挥过去）→ recoverTime（后摇）
/// 判定只在 hitboxActiveRange 指定的那一小段生效（从攻击阶段的第 0 秒算）。
/// </summary>
[Serializable]
public class MeleeSwing
{
    [Header("--- 时序 ---")]
    [Tooltip("前摇时长（秒）。用于播放起手动作（占位方块往上举 / 往后拉）。")]
    public float windupTime = 0.3f;

    [Tooltip("蓄势时长上限（秒）：前摇之后，方块举着不动，最多憋这么久再挥下去。\n" +
             "每次这一段挥击开始时，先在 0 ~ Hold Time 之间抽好这一刀憋多久（下面的 Release Bias 决定偏向）—— 快慢刀。\n" +
             "填 0 = 没有蓄势，前摇完直接挥。")]
    [Min(0f)]
    public float holdTime = 0f;

    [FormerlySerializedAs("hitboxDuration")]
    [Tooltip("攻击时长（秒）：挥过去的整个动作。占位方块在这段时间里从前摇的位置挥到判定框，\n" +
             "在判定开始的那一刻正好到达。判定什么时候生效看下面的 Hitbox Active Range。")]
    public float attackTime = 0.1f;

    [Tooltip("后摇时长（秒）。这段时间 Boss 处于硬直，是玩家的输出窗口。")]
    public float recoverTime = 0.2f;

    [Tooltip("判定生效的时间段 (X, Y)，单位秒，【从攻击阶段开始（第 0 秒）算起】：\n" +
             "攻击开始后第 X 秒判定框打开，第 Y 秒关掉。方块在第 X 秒正好挥到判定框。\n" +
             "  X 填 -1 = 攻击一开始\n" +
             "  Y 填 -1 = 攻击结束时（= Attack Time）\n" +
             "  X 和 Y 相等 = 只判定一帧\n" +
             "默认 (-1, -1) = 整个攻击阶段都有判定。Y 可以伸进后摇，超出「攻击 + 后摇」的部分会被截掉。\n" +
             "拼刀也只在这段时间里能拼上。")]
    public Vector2 hitboxActiveRange = new Vector2(-1f, -1f);

    [Tooltip("出刀偏向：每次抽「憋多久」时，偏向 Hold Time 的开头还是结尾。\n" +
             "  0.5 = 均匀随机\n" +
             "  小于 0.5 = 更容易抽到靠前（快刀多）\n" +
             "  大于 0.5 = 更容易抽到靠后（慢刀多）\n" +
             "  0 / 1 = 每次都不憋 / 每次都憋满\n" +
             "这个值正好是平均位置：Hold Time 0.5、偏向 0.3 → 平均憋 0.15 秒。")]
    [Range(0f, 1f)]
    public float releaseBias = 0.5f;

    /// <summary>
    /// 抽这一刀要憋多久（0 ~ holdTime）。每次挥击开始时调用一次。
    /// 做法：均匀随机数 u（0~1）的 k 次方，k = (1 - 偏向) / 偏向。
    /// u^k 的平均值正好是 1 / (k + 1) = 偏向；偏向 0.5 时 k = 1，就是均匀随机。
    /// </summary>
    public float RollHoldTime()
    {
        float max = Mathf.Max(0f, holdTime);
        if (max <= 0f) return 0f;

        float bias = Mathf.Clamp01(releaseBias);
        if (bias <= 0.0001f) return 0f;
        if (bias >= 0.9999f) return max;

        float k = (1f - bias) / bias;
        return max * Mathf.Pow(UnityEngine.Random.value, k);
    }

    /// <summary>攻击开始之后还剩多久这一段才结束 = 攻击 + 后摇</summary>
    public float AfterAttackLength => Mathf.Max(0f, attackTime) + Mathf.Max(0f, recoverTime);

    /// <summary>判定打开的时刻（从攻击开始算，已截到「攻击 + 后摇」以内）</summary>
    public float HitboxStart
        => Mathf.Clamp(hitboxActiveRange.x < 0f ? 0f : hitboxActiveRange.x, 0f, AfterAttackLength);

    /// <summary>判定关掉的时刻（从攻击开始算，不早于打开的时刻）</summary>
    public float HitboxEnd
        => Mathf.Max(HitboxStart,
                     Mathf.Clamp(hitboxActiveRange.y < 0f ? Mathf.Max(0f, attackTime) : hitboxActiveRange.y, 0f, AfterAttackLength));

    [Header("--- 判定框 ---")]
    [Tooltip("判定框【底边中点】相对 Boss 脚底（轴心）的偏移。\n" +
             "X 会自动乘以朝向，填正数即可（表示身前）；Y 填 0 = 判定框贴地，往上长出 hitboxSize 的高度。")]
    public Vector2 hitboxOffset = new Vector2(1.5f, 0f);

    [Tooltip("判定框尺寸（宽, 高）。")]
    public Vector2 hitboxSize = new Vector2(2f, 2f);

    [Header("--- 伤害 ---")]
    public int damage = 15;

    [Header("--- 拼刀 ---")]
    [Tooltip("这一段挥击能不能被玩家拼刀（AA1 / BB1 这类带拼刀框的招）。\n" +
             "取消勾选 = 只能躲、不能拼。")]
    public bool clashable = true;

    [Header("--- 动画（可选）---")]
    [Tooltip("占位动画的挥击方向。\n" +
             "竖直 = 前摇往上举，攻击阶段砸下来\n" +
             "水平 = 前摇往 Boss 身后拉，攻击阶段横着甩到判定框\n\n" +
             "只改动画，判定框不会跟着转：横扫的招请自己把 Hitbox Size 改成又宽又矮。")]
    public MeleeSwingDirection swingDirection = MeleeSwingDirection.Vertical;

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
