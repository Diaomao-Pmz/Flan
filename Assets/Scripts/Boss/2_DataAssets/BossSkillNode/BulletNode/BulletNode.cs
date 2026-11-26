using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>旧版弹幕形态枚举。新版用 BulletPhase.pattern 的下拉框选类型，这里只为兼容可能残留的引用而保留</summary>
public enum BossAttackType
{
    Line,
    Random,
    Circle,
    Square,
    Rotation,
    Triangle,
    Star
}

/// <summary>
/// 组合弹幕中的一段。多段叠加就是「同时发射数种弹幕」。
///
/// startDelay 决定组合方式：
///   全填 0        → 并发（直线 + 环形同时喷）
///   递增          → 序列（直线 2 秒 → 环形 3 秒 → 五角星 2 秒）
///   混合          → 交错叠加
///
/// 【图案直接内嵌】pattern 用下拉框选类型，选完显示该类型的通用子弹设置 + 专属参数。
/// 每张卡的参数各存各的，改这张卡不会影响别的卡。
/// </summary>
[Serializable]
public class BulletPhase
{
    [Tooltip("相对组合开始的延迟（秒）。0 表示与组合同时启动。")]
    public float startDelay = 0f;

    [Tooltip("这一段持续多久（秒）")]
    public float duration = 3f;

    [Tooltip("这一段用哪种弹幕：点右侧下拉框选类型")]
    [SerializeReference, SubclassSelector]
    public BulletPatternBase pattern;
}

/// <summary>放弹幕时 Boss 怎么移动</summary>
public enum FireMoveMode
{
    StandStill,    // 站定：原地放完
    KeepDistance,  // 保持距离：出招时在范围里抽一个距离，这一招全程跟玩家保持这个距离
    MoveThenHold   // 位移后站定：出招时按玩家在哪边定好方向，走够格数就站住
}

/// <summary>位移后站定往哪边走</summary>
public enum FireMoveDirection
{
    AwayFromPlayer, // 远离玩家（后撤）
    TowardPlayer    // 靠近玩家
}

/// <summary>开火时移动的参数（弹幕卡的默认值、套路步骤的覆盖值都用它）</summary>
[Serializable]
public class FireMoveSettings
{
    [Tooltip("保持距离：离玩家多远（格），X = 最近，Y = 最远。每次出招在这个范围里抽一个距离，这一招全程保持它。两头填一样就是固定距离")]
    public Vector2 distanceRange = new Vector2(3.5f, 5f);

    [Tooltip("保持距离：抽距离时偏向哪头。0 = 偏向最近（X），1 = 偏向最远（Y），0.5 = 均匀随机")]
    [Range(0f, 1f)]
    public float distanceBias = 0.5f;

    [Tooltip("位移后站定：往哪边走（出招那一刻按玩家在哪边定好方向，之后不再变）")]
    public FireMoveDirection direction = FireMoveDirection.AwayFromPlayer;

    [Tooltip("位移后站定：走多少格就站住。撞墙会提前停；弹幕放完时还没走完也停")]
    [Min(0f)]
    public float moveDistance = 3f;

    [Tooltip("开火时的移速倍率。1 = Boss 当前姿态的正常移速（BossAIDecider 的 Tempo），0.5 = 慢一半")]
    [Min(0f)]
    public float speedMultiplier = 1f;

    /// <summary>保持距离：抽这一招要保持的距离。偏向的算法和 Boss 近战蓄刀（MeleeNode.releaseBias）一样</summary>
    public float RollKeepDistance()
    {
        float near = Mathf.Max(0f, Mathf.Min(distanceRange.x, distanceRange.y));
        float far = Mathf.Max(0f, Mathf.Max(distanceRange.x, distanceRange.y));

        float bias = Mathf.Clamp01(distanceBias);
        float t;
        if (bias <= 0.0001f) t = 0f;
        else if (bias >= 0.9999f) t = 1f;
        else t = Mathf.Pow(UnityEngine.Random.value, (1f - bias) / bias);

        return Mathf.Lerp(near, far, t);
    }
}

/// <summary>
/// 脱手释放的参数（弹幕卡的默认值、套路步骤的覆盖值都用它）。
/// 脱手 = 这张弹幕交给发射器自己发完，Boss 的身体空出来去做下一步。
/// </summary>
[Serializable]
public class ReleaseSettings
{
    [Tooltip("弹幕结束前多少秒脱手。填得比整张弹幕还长 = 一开火就脱手")]
    [Min(0f)]
    public float beforeEnd = 1f;

    [Tooltip("脱手时间（秒）：从脱手那一刻开始算，这段时间里 Boss 不出下一招、只走位（为下一招靠位）。\n" +
             "时间到了，下一招够得着就出。0 = 够得着就马上出")]
    [Min(0f)]
    public float holdTime = 0f;

    [Tooltip("脱手后、出下一招之前这段走位允许冲刺：位移卡池只考虑冲刺卡（不看传送卡）。\n" +
             "不勾 = 这段走位只走路，不问位移卡池")]
    public bool allowDash = false;
}

/// <summary>
/// 弹幕招式卡。只有一种弹幕的卡，Phases 里放 1 个元素即可；组合弹幕放多个。
/// </summary>
[CreateAssetMenu(fileName = "NewBulletNode", menuName = "ScriptableObjects/BulletNode")]
public class BulletNode : ActionNode
{
    [Header("--- 弹幕配置 ---")]
    [Tooltip("弹幕段列表。单一弹幕放 1 段，组合弹幕放多段")]
    public List<BulletPhase> phases = new List<BulletPhase>();

    [Header("--- 开火时移动 ---")]
    [Tooltip("放这张弹幕时 Boss 怎么移动（默认值；套路步骤可以覆盖）：\n" +
             "Stand Still    = 站定，原地放完\n" +
             "Keep Distance  = 保持距离：每次出招在范围里抽一个距离，全程跟玩家保持它\n" +
             "Move Then Hold = 位移后站定：远离 / 靠近玩家走够格数就站住，不管玩家多近")]
    public FireMoveMode fireMoveMode = FireMoveMode.StandStill;

    [Tooltip("开火时移动的参数，只显示当前方式用得上的")]
    public FireMoveSettings fireMove = new FireMoveSettings();

    [Header("--- 脱手释放 ---")]
    [Tooltip("勾选 = 放到结束前几秒就脱手：剩下的弹幕交给发射器自己发完，Boss 去做下一步（默认值；套路步骤可以覆盖）。\n" +
             "单招抽到这张卡也生效：脱手后 Boss 去想下一招。\n" +
             "脱手的弹幕：玩家打断 Boss 下一招不影响它；破盾 / 转阶段 / 死亡时停")]
    public bool allowRelease = false;

    [Tooltip("脱手释放的参数，只在勾了上一项时显示（套路步骤选「Release」时用步骤自己的）")]
    public ReleaseSettings release = new ReleaseSettings();

    [Tooltip("转到弹幕层后（脱手，或近身反应时托管；卡上或套路步骤上设的都算）子弹从哪发：\n" +
             "勾选 = 跟随 Boss：子弹照样从 Boss 身上发，Boss 走到哪发到哪\n" +
             "不勾 = 钉在原地：从转过去那一刻 Boss 站的位置发完（近身反应取回后回到 Boss 身上发）。\n" +
             "钉在原地的弹幕脱手后不再触发走位中的近身反应")]
    public bool followBossWhenReleased = true;

    [Header("--- 近身反应 ---")]
    [Tooltip("勾选 = 玩家贴近时 Boss 先出一招反应招（默认值；套路步骤可以覆盖）。反应招和条件在 BossAIDecider 的近身反应卡池里配。\n" +
             "· Boss 站着放这张弹幕时：弹幕不中断，先交给发射器自己发（托管），身体去出反应招\n" +
             "· 这张弹幕脱手了、并且跟随 Boss（上面勾选）：Boss 走位时玩家贴近也会反应，打完回到原来的流程\n" +
             "反应招被玩家打断：只打断反应招，托管的弹幕照样发完")]
    public bool allowCloseReaction = false;

    [Tooltip("反应招打完后把弹幕取回：弹幕还没发完就回到 Boss 身上，Boss 接着放完这一步（开火时移动接着走）；\n" +
             "已经发完就直接做下一步；反应期间已经过了脱手的时间点，就当作已经脱手。\n" +
             "不勾 = 弹幕留给发射器发完，Boss 直接做下一步")]
    public bool reclaimAfterReaction = true;

    /// <summary>
    /// 整套弹幕的总时长，供执行器等待：取所有段中「延迟 + 时长」的最大值。
    /// </summary>
    public float TotalDuration
    {
        get
        {
            float max = 0f;
            if (phases == null) return max;
            for (int i = 0; i < phases.Count; i++)
            {
                BulletPhase p = phases[i];
                if (p == null || p.pattern == null) continue;
                max = Mathf.Max(max, p.startDelay + p.duration);
            }
            return max;
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 在 Inspector 里点「+」新加一段时，Unity 会让新段和上一段共用同一份图案参数（改一个另一个跟着变）。
    /// 每次改动后检查一遍：发现共用就给后面那段复制一份独立的
    /// </summary>
    private void OnValidate()
    {
        if (phases == null) return;
        var seen = SerializeReferenceDedup.NewSet();
        for (int i = 0; i < phases.Count; i++)
        {
            BulletPhase p = phases[i];
            if (p == null) continue;
            p.pattern = SerializeReferenceDedup.Unshare(p.pattern, seen);
        }
    }
#endif
}
