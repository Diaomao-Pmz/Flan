using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>套路在哪个作战阶段能被抽到（门禁）。只改了 Inspector 显示名，存的数值没变</summary>
public enum RoutineMode
{
    Any,                                    // 两种姿态都能用
    [InspectorName("Ranged Stage")] Ranged, // 只在护盾在（远程为主）时用
    [InspectorName("Melee Stage")]  Melee   // 只在护盾碎（近战为主）时用
}

/// <summary>
/// 套路本身算哪一类招式（标签）：决定吃 Tempo Config 姿态倾向里的哪个倍率。
/// 比喻：Mode 是门口的保安（这个阶段让不让进场），定位是身上的队服颜色（按颜色加票）
/// </summary>
public enum RoutineRole
{
    Mix,     // 混合：吃姿态倾向里的 Mix 倍率
    Ranged,  // 远程：吃 Ranged 倍率
    Melee    // 近战：吃 Melee 倍率
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

    [Tooltip("勾选 = 出完这一招，整个套路到此结束，后面的步骤都不出。\n" +
             "算套路打完：照常开始这个套路的后续招式间隔（Rest After），之后 AI 正常做下一个决定。\n" +
             "Replace / Insert 都能勾；Insert 勾了效果和 Replace 勾了一样（原本那一招不会再出）")]
    public bool endRoutineAfter = false;
}

/// <summary>套路步骤上的「开火时移动」：跟随弹幕卡，或者这一步自己选</summary>
public enum StepFireMoveMode
{
    FollowCard,   // 跟随弹幕卡上的设置
    StandStill,   // 站定
    KeepDistance, // 保持距离
    MoveThenHold  // 位移后站定
}

/// <summary>套路步骤上的「脱手释放」：跟随弹幕卡，或者这一步自己定</summary>
public enum StepReleaseMode
{
    FollowCard, // 跟随弹幕卡上的设置
    NoRelease,  // 不脱手：放完再接下一步
    Release     // 脱手：用这一步自己的参数
}

/// <summary>套路步骤上的「近身反应」：跟随弹幕卡，或者这一步自己定</summary>
public enum StepReactionMode
{
    FollowCard, // 跟随弹幕卡上的设置
    Off,        // 这一步不做近身反应
    On          // 这一步允许近身反应（取回用这一步自己的设置）
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

    [Tooltip("这一步放弹幕时 Boss 怎么移动（只对弹幕卡生效；换成替换候补的弹幕卡也生效，插入的候补用它自己卡上的设置）：\n" +
             "Follow Card    = 跟随弹幕卡上的设置\n" +
             "Stand Still    = 站定\n" +
             "Keep Distance  = 保持距离\n" +
             "Move Then Hold = 位移后站定")]
    public StepFireMoveMode fireMoveMode = StepFireMoveMode.FollowCard;

    [Tooltip("这一步自己的开火移动参数（选了 Follow Card 时不用）")]
    public FireMoveSettings fireMove = new FireMoveSettings();

    [Tooltip("这一步放弹幕时脱不脱手（只对弹幕卡生效；换成替换候补的弹幕卡也生效，插入的候补用它自己卡上的设置）：\n" +
             "Follow Card = 跟随弹幕卡上的设置\n" +
             "No Release  = 不脱手：放完再接下一步\n" +
             "Release     = 脱手：结束前几秒把剩下的弹幕交给发射器，Boss 去做下一步（下一步是弹幕卡也行）。\n" +
             "最后一步脱手的那一刻就算套路打完，后续招式间隔从那时开始算")]
    public StepReleaseMode releaseMode = StepReleaseMode.FollowCard;

    [Tooltip("这一步自己的脱手参数（只在选了 Release 时用）")]
    public ReleaseSettings release = new ReleaseSettings();

    [Tooltip("（只在选了 Release 时用）弹幕转到弹幕层后（脱手，或近身反应时托管）子弹从哪发：\n" +
             "勾选 = 跟随 Boss：子弹照样从 Boss 身上发，Boss 走到哪发到哪\n" +
             "不勾 = 钉在原地：从转过去那一刻 Boss 站的位置发完。\n" +
             "选 Follow Card / No Release 时按弹幕卡上的 Follow Boss When Released")]
    public bool followBossWhenReleased = true;

    [Tooltip("这一步放弹幕时玩家贴近要不要先出反应招（只对弹幕卡生效；反应招和条件在 BossAIDecider 的近身反应卡池里配）：\n" +
             "Follow Card = 跟随弹幕卡上的设置\n" +
             "Off         = 不反应\n" +
             "On          = 反应：弹幕先交给发射器发，Boss 出反应招，打完按下面的设置取回")]
    public StepReactionMode reactionMode = StepReactionMode.FollowCard;

    [Tooltip("这一步自己的「反应后取回」（只在选了 On 时用）：\n" +
             "勾选 = 打完把弹幕取回，接着放完这一步；不勾 = 弹幕留给发射器发完，Boss 直接做下一步")]
    public bool reclaimAfterReaction = true;

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

    [Tooltip("在哪个作战阶段能被抽到。Any = 两种姿态都能用；\n" +
             "Ranged Stage = 只在护盾在（远程为主）时；Melee Stage = 只在护盾碎（近战为主）时")]
    public RoutineMode mode = RoutineMode.Any;

    [Tooltip("招式定位：这个套路算哪一类招式。\n" +
             "抽套路时，权重会乘上 Tempo Config 姿态倾向里对应的倍率（Ranged / Melee / Mix）")]
    public RoutineRole role = RoutineRole.Mix;

    [Tooltip("抽套路时的权重")]
    [Min(0)]
    public int baseWeight = 100;

    [Tooltip("用完一次后多久才能再用（秒）。从套路开始那一刻算")]
    [Min(0f)]
    public float cooldown = 5f;

    [Tooltip("后续招式间隔（秒）：这个套路最后一招放完后，这么久之内 Boss 不选新的套路（单招、位移照常）。\n" +
             "0 = 不限制：打完可以马上接新的套路。\n" +
             "最后一步脱手释放的话，脱手那一刻就算打完。\n" +
             "高强度套路填大一点，给玩家留输出窗口。\n" +
             "和 Cooldown 的区别：Cooldown 只管这一个套路多久后能再用（从套路开始算）；这里管所有套路（从这个套路打完算）。\n" +
             "套路被打断（破盾、受击打断等）不算打完，不触发间隔")]
    [Min(0f)]
    public float restAfter = 0f;

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
