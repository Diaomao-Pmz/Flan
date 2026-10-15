using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>套路属于哪个作战姿态</summary>
public enum RoutineMode
{
    Any,      // 两种姿态都能用
    Ranged,   // 只在护盾在（远程为主）时用
    Melee     // 只在护盾碎（近战为主）时用
}

/// <summary>套路对玩家空中状态的要求</summary>
public enum PlayerAirCondition
{
    Any,       // 不限
    Grounded,  // 玩家站在地上
    Airborne,  // 玩家不在地上（跳跃、下落、飞行都算）
    Flying     // 玩家正在飞行
}

/// <summary>某一步的招式够不着玩家时怎么办</summary>
public enum StepOutOfRange
{
    Approach, // 先移动靠位，到了射程再出这一招（靠位失败太多次则放弃整个套路）
    Skip,     // 跳过这一步，直接看下一步
    Abort     // 整个套路作废
}

/// <summary>玩家相对 Boss 的位置（以 Boss 上一次近战 / 突进出手的朝向为"正面"）</summary>
public enum PlayerSide
{
    Any,     // 不限
    Behind,  // 玩家绕到了 Boss 背后（站在上一招打的反方向）
    InFront  // 玩家在 Boss 面前（上一招打的那一侧）
}

/// <summary>
/// 一组"看情况"的条件。所有填了的条件都满足才算满足（AND）。
/// 目前给套路步骤的候补招式用；以后套路自身的触发条件也可以合并进来。
/// </summary>
[Serializable]
public class RoutineCondition
{
    [Tooltip("玩家在 Boss 背后 / 面前。\n" +
             "\"正面\" = Boss 上一次近战或突进出手的方向；Boss 还没出过近战 / 突进时，Behind 永远不满足")]
    public PlayerSide playerSide = PlayerSide.Any;

    [Tooltip("玩家至少离 Boss 这么远")]
    [Min(0f)]
    public float minDistance = 0f;

    [Tooltip("玩家最多离 Boss 这么远")]
    [Min(0f)]
    public float maxDistance = 99f;

    [Tooltip("对玩家空中状态的要求")]
    public PlayerAirCondition playerAirCondition = PlayerAirCondition.Any;

    /// <summary>此刻条件是否成立</summary>
    public bool Evaluate(in BossContext ctx, float dist)
    {
        if (dist < minDistance || dist > maxDistance) return false;

        switch (playerSide)
        {
            case PlayerSide.Behind:  if (!ctx.IsPlayerBehindBoss) return false; break;
            case PlayerSide.InFront: if (ctx.IsPlayerBehindBoss) return false; break;
        }

        switch (playerAirCondition)
        {
            case PlayerAirCondition.Grounded: return ctx.IsPlayerGrounded;
            case PlayerAirCondition.Airborne: return !ctx.IsPlayerGrounded;
            case PlayerAirCondition.Flying:   return ctx.IsPlayerFlying;
            default:                          return true;
        }
    }
}

/// <summary>
/// 情境加成：满足条件时，把这张卡 / 这个套路的抽取权重乘一个倍数。
/// 比喻：卡的"出场分"上贴的加分条。
///   倍率 &gt; 1 = 这种情况下更爱出；&lt; 1 = 少出；0 = 这种情况下不出
/// </summary>
[Serializable]
public class WeightModifier
{
    [Tooltip("满足这些条件时生效（所有填了的条件都满足才算）")]
    public RoutineCondition condition = new RoutineCondition();

    [Tooltip("满足条件时权重乘多少。\n" +
             "大于 1 = 更爱出（例如 3 = 三倍）；小于 1 = 少出；0 = 这种情况下完全不出")]
    [Min(0f)]
    public float multiplier = 2f;

    /// <summary>把一组加成作用到权重上：所有满足条件的倍率相乘</summary>
    public static float Factor(List<WeightModifier> modifiers, in BossContext ctx, float dist)
    {
        if (modifiers == null || modifiers.Count == 0) return 1f;

        float factor = 1f;
        for (int i = 0; i < modifiers.Count; i++)
        {
            WeightModifier m = modifiers[i];
            if (m == null || m.condition == null) continue;
            if (m.condition.Evaluate(ctx, dist)) factor *= m.multiplier;
        }
        return factor;
    }
}

/// <summary>候补招式生效时怎么处理这一步原本的招式</summary>
public enum VariantMode
{
    Replace, // 替换：出候补，这一步就算完成（原本的招不出）
    Insert   // 插入：先出候补，再出这一步原本的招
}

/// <summary>候补招式：条件满足时，替换这一步的默认招式，或插在它前面</summary>
[Serializable]
public class StepVariant
{
    [Tooltip("满足这些条件时改出下面的招式")]
    public RoutineCondition condition = new RoutineCondition();

    [Tooltip("候补的招式。它自己也必须够得着玩家才会被选中，够不着就看下一个候补")]
    public ActionNode action;

    [Tooltip("Replace = 替换：出候补，这一步就算完成\n" +
             "Insert  = 插入：先出候补，再出这一步原本的招（例：玩家在背后 → 先补一下击退，再放激光）。\n" +
             "每一步最多插入一次：插入后再轮到这一步时不再检查候补，直接出原本的招，避免无限循环")]
    public VariantMode mode = VariantMode.Replace;
}

/// <summary>套路里的一步</summary>
[Serializable]
public class RoutineStep
{
    [Tooltip("这一步默认出的招式（任意 ActionNode 资产：弹幕、近战、激光、突进、传送…）")]
    public ActionNode action;

    [Tooltip("出这一招之前先停顿多久（秒），用来调套路节奏。第一步一般填 0")]
    [Min(0f)]
    public float delayBefore = 0f;

    [Tooltip("够不着玩家时怎么办：\n" +
             "Approach = 先走过去再出\n" +
             "Skip     = 跳过这一步\n" +
             "Abort    = 整个套路作废")]
    public StepOutOfRange whenOutOfRange = StepOutOfRange.Approach;

    [Tooltip("候补招式（可留空）。轮到这一步时从上往下检查：\n" +
             "第一个【条件满足且够得着玩家】的候补替换默认招式；都不满足就出默认招式。\n" +
             "有出招前停顿时，停顿结束后会再检查一次，玩家在停顿期间的走位也会被看到。\n" +
             "例：默认 = 激光；候补 [玩家在背后] → 击退近战")]
    public List<StepVariant> variants = new List<StepVariant>();
}

/// <summary>
/// 【Boss 套路】—— 几个招式按顺序串起来，为同一个目的服务。
///
/// ==========================================================
/// 比喻：单招是"随手抽一张牌打出去"，套路是"一本战术手册"——
///       AI 先挑一本手册，再照着一页一页出牌。
///
/// 例子：
///   远程姿态「逼退射击」 = 近战挥击（勾选击退把人推开）→ 传送拉开 → 扇形弹幕
///   近战姿态「封位突进」 = 封路弹幕 → 突进斩 → 近身连斩
///
/// 套路里的每一步仍然复用现有的执行器、射程判定和靠位逻辑，执行器一行都不用改。
/// 每一步还可以配"候补招式"：手册的批注 ——"默认出激光；如果玩家绕到背后，改出击退近战"。
/// 步骤里招式自己的冷却【不】拦套路（套路有自己的冷却），但出招后照常记冷却，影响单招抽卡。
/// ==========================================================
/// </summary>
[CreateAssetMenu(fileName = "NewBossRoutine", menuName = "ScriptableObjects/BossRoutine")]
public class BossRoutine : ScriptableObject
{
    [Header("--- 基本信息 ---")]
    [Tooltip("套路名称，用于日志和调试文字")]
    public string routineName;

    [Tooltip("属于哪个作战姿态。Any = 两种姿态都能用")]
    public RoutineMode mode = RoutineMode.Any;

    [Tooltip("抽套路时的权重")]
    [Min(0)]
    public int baseWeight = 100;

    [Tooltip("用完一次后多久才能再用（秒）。从套路开始那一刻算")]
    [Min(0f)]
    public float cooldown = 5f;

    [Header("--- 触发条件 ---")]
    [Tooltip("玩家至少离 Boss 这么远才会选这个套路")]
    [Min(0f)]
    public float minPlayerDistance = 0f;

    [Tooltip("玩家最多离 Boss 这么远才会选这个套路")]
    [Min(0f)]
    public float maxPlayerDistance = 99f;

    [Tooltip("对玩家空中状态的要求")]
    public PlayerAirCondition playerAirCondition = PlayerAirCondition.Any;

    [Header("--- 步骤 ---")]
    [Tooltip("按顺序执行。空槽位会被跳过")]
    public List<RoutineStep> steps = new List<RoutineStep>();

    public string DisplayName => string.IsNullOrEmpty(routineName) ? name : routineName;

    /// <summary>至少有一步配了招式</summary>
    public bool HasAnyStep
    {
        get
        {
            if (steps == null) return false;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] != null && steps[i].action != null) return true;
            }
            return false;
        }
    }
}
