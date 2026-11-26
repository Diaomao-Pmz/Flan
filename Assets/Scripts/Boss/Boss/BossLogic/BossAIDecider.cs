using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Boss 的决策大脑。
///
/// Inspector 按语义分列表，方便调试；内部仍合并成一个统一卡池抽卡，
/// 抽卡逻辑与技能类型完全无关（只读基类的 baseWeight / minCastDistance / MaxCastDistance / cooldown）。
///
/// 【本次改动】Specials 拆成 SpecialBullets / SpecialMelees 两个桶。
/// 注意它们的类型仍是 List&lt;ActionNode&gt; 而不是 List&lt;LaserNode&gt; —— 这是有意的：
/// 按语义分桶而非按类型分桶，以后再加特殊技能只要丢进对应的桶，本文件一行都不用改。
///
/// 【姿态倾向】合并卡池时保留每张牌来自哪个桶（远程 / 近战）。
/// 抽卡权重 = 招式基础权重 × 当前姿态对该类别的倍率（套路按它的「招式定位」Ranged / Melee / Mix 取倍率）：
///   护盾在 → 远程为主，用 rangedModeWeights
///   护盾碎 → 近战为主，用 meleeModeWeights
///
/// 【意图驱动】（比喻：先想好下一招打什么，再按这一招的需要去站位）
/// 每打完一招，MoveState 一进来就调 PlanIntent() 定下「下一招」（意图），
/// 然后朝这一招的理想距离走；满足出手条件就切 CombatState，由 SelectSkill() 兑现意图。
///
/// 定意图的顺序（PlanIntent）：
///   1. 正在执行套路 → 意图就是套路的下一步
///   2. 选新套路：当前姿态 + 当前一/二阶段、条件满足的套路按权重挑一个
///      （有 singleInterleaveChance 的概率跳过这一层，改出单招）
///   3. 单招：只看【阶段标签】允许、不在冷却的卡，按权重抽 ——【不看当前距离】，
///      够不着就让 Boss 走过去，所以走位完全由"下一招要什么"决定
///   4. 兜底招式 fallbackNode（不管射程直接出）
///
/// 出手时（SelectSkill）：
///   · 兜底卡 → 不看射程直接出
///   · 套路 → 推进下一步；单招 → 出手前再校验射程和冷却，不通过就作废、回去重新定意图
///
/// 【位移卡池】传送卡 / 冲刺卡不参与出招抽卡，而是"到位的手段"：
/// 移动阶段开始时（以及撞墙时），MoveState 调 PickMovement() 问一句"要不要闪过去 / 冲过去"。
///   · Cornered（撞墙）：按列表顺序取第一张可用的 —— 保命用，要稳定
///   · TooFar / TooClose / Random：所有满足条件的放一起按权重抽一张，再按它的 Chance 决定闪还是走
///     （多选项随机 + 概率 + 冷却，玩家能感觉到规律，但骗不出固定反应）
///
/// 【弹幕在发时的规则表】弹幕卡可以「脱手」：剩下的弹幕交给发射器自己发完（弹幕层），Boss 身体去做下一步。
/// 弹幕层里还有弹幕时，下一张卡（套路下一步 / 单招 / 兜底 / 位移卡池）按规则表决定能不能出：
/// 不能出就【等弹幕发完】，等的时候照常走位、不算靠位超时；位移卡池里不能出的卡直接不考虑（改走路）。
/// 类别按卡的类型判断，卡上的 whileBarrage 可以单独改成允许 / 禁止。
///
/// 【近身反应】放弹幕时（或脱手后跟随 Boss 的弹幕还在发、Boss 在走位时）玩家贴近，
/// 从近身反应卡池里挑一张满足条件的反应招先出。每掷一次骰（不管掷没掷中）都进全局反应冷却。
/// 走位中触发的反应招放进最优先的槽（QueueReaction），不动套路进度和单招意图。
/// </summary>
public class BossAIDecider : MonoBehaviour
{
    /// <summary>招式类别 —— 由它被放进哪个桶决定</summary>
    public enum SkillCategory
    {
        Ranged,   // Bullets / SpecialBullets
        Melee     // Melees / SpecialMelees
    }

    /// <summary>某个姿态下，各类招式的权重倍率（单招和套路都用）</summary>
    [System.Serializable]
    public class ModeWeights
    {
        [Tooltip("远程单招（弹幕 / 特殊远程池）和定位为 Ranged 的套路")]
        [Min(0f)] public float ranged = 1f;
        [Tooltip("近战单招（近战 / 特殊近战池）和定位为 Melee 的套路")]
        [Min(0f)] public float melee = 1f;
        [Tooltip("定位为 Mix 的套路（单招没有 Mix，不受影响）")]
        [Min(0f)] public float mix = 1f;

        public ModeWeights() { }

        public ModeWeights(float ranged, float melee, float mix = 1f)
        {
            this.ranged = ranged;
            this.melee = melee;
            this.mix = mix;
        }

        public float Get(SkillCategory c) => c == SkillCategory.Ranged ? ranged : melee;

        public float Get(RoutineRole r)
        {
            switch (r)
            {
                case RoutineRole.Ranged: return ranged;
                case RoutineRole.Melee:  return melee;
                default:                 return mix;
            }
        }
    }

    /// <summary>位移卡什么情况下考虑使用</summary>
    public enum MoveTrigger
    {
        TooFar,    // 离意图理想距离太远（需要靠近）
        TooClose,  // 离意图理想距离太近（需要拉开）
        Cornered,  // 移动途中撞墙
        Random     // 每个移动阶段开始时随机，纯为节奏变化
    }

    /// <summary>位移卡池里的一个条目</summary>
    [System.Serializable]
    public class MovementEntry
    {
        [Tooltip("在哪个作战阶段能用：Mix = 都能用 / Ranged = 只在护盾在时 / Melee = 只在破盾后")]
        public SkillPhaseTag phaseTag = SkillPhaseTag.Mix;

        [Tooltip("什么情况下考虑使用：\n" +
                 "TooFar   = 当前距离比意图的理想距离【远】出 Min Gap~Max Gap 格\n" +
                 "TooClose = 当前距离比意图的理想距离【近】出 Min Gap~Max Gap 格\n" +
                 "Cornered = 移动途中撞墙（按列表顺序取第一张，不看权重）\n" +
                 "Random   = 每个移动阶段开始时都有机会（建议 Chance 填 0.2 左右）")]
        public MoveTrigger trigger = MoveTrigger.TooFar;

        [Tooltip("差距下限（格）。只对 TooFar / TooClose 生效")]
        [Min(0f)]
        public float minGap = 5f;

        [Tooltip("差距上限（格）。只对 TooFar / TooClose 生效。\n" +
                 "配两条同类条目、区间错开，就能做到\"差得少闪到 A、差得多闪到 B\"")]
        [Min(0f)]
        public float maxGap = 99f;

        [Tooltip("多张卡同时满足条件时的抽取权重（Cornered 不看这个）")]
        [Min(0)]
        public int weight = 100;

        [Tooltip("抽中后真正位移的概率（0~1）。没位移就走路过去")]
        [Range(0f, 1f)]
        public float chance = 1f;

        [Tooltip("位移卡：传送卡（TeleportNode）或冲刺卡（DashMoveNode），放别的卡不会生效。\n" +
                 "冷却用卡上的 Cooldown（与受击传送、套路共用）。\n" +
                 "传送卡推荐 Target Type = Intent Distance；冲刺卡推荐 To Distance、距离填 -1：都会到意图招式的理想距离")]
        public ActionNode action;
    }

    /// <summary>
    /// 一个作战姿态的战斗节奏：两招之间怎么移动、什么时候出手。
    /// </summary>
    [System.Serializable]
    public class CombatTempo
    {
        [Tooltip("两招之间至少移动多久（秒），在 X~Y 之间随机。留给玩家喘息和输出的窗口。\n" +
                 "勾选下面的「进射程立即出手」时，进了射程就不再等这段时间")]
        public Vector2 repositionTime = new Vector2(0.8f, 1.8f);

        [Tooltip("勾选 = 意图招式一进射程就立刻出手（节奏快）\n" +
                 "不勾 = 至少移动完上面的换位时间，且在射程内才出手（走走停停）")]
        public bool fireImmediatelyInRange = true;

        [Tooltip("最多为一张意图卡靠位多久（秒）。超时还没进射程就放弃它：\n" +
                 "先从射程内的卡里抽一张出，没有就出兜底卡，再没有就重新定意图")]
        [Min(0.1f)]
        public float maxApproachTime = 4f;

        [Tooltip("这个姿态下的移速倍率：实际移速 = Movement Config 的基础移速 × 这个倍率。\n" +
                 "1 = 基础移速；1.2 = 快 20%；0 = 不走。（旧表里的 -1 当作 1）")]
        [FormerlySerializedAs("moveSpeed")]
        public float moveSpeedMultiplier = 1f;

        public CombatTempo() { }

        public CombatTempo(float minReposition, float maxReposition, bool immediate, float maxApproach)
        {
            repositionTime = new Vector2(minReposition, maxReposition);
            fireImmediatelyInRange = immediate;
            maxApproachTime = maxApproach;
        }

        /// <summary>随机取一次换位时间</summary>
        public float RollRepositionTime()
            => Random.Range(Mathf.Min(repositionTime.x, repositionTime.y), Mathf.Max(repositionTime.x, repositionTime.y));
    }

    /// <summary>
    /// 规则表：弹幕层里还有弹幕在发时，每一类卡能不能出。
    /// 类别按卡的类型判断（不看放在哪个卡池）：
    ///   BulletNode = 弹幕，MeleeNode = 近战，DashAttackNode = 特殊近战，LaserNode = 特殊远程，
    ///   DashMoveNode = 冲刺，TeleportNode = 传送。别的类型一律允许
    /// </summary>
    [System.Serializable]
    public class BarrageRuleTable
    {
        [Tooltip("弹幕卡（BulletNode）")]
        public bool bullet = true;
        [Tooltip("近战卡（MeleeNode）")]
        public bool melee = true;
        [Tooltip("特殊近战（DashAttackNode 突进斩）")]
        public bool specialMelee = false;
        [Tooltip("特殊远程（LaserNode 激光）")]
        public bool specialRanged = false;
        [Tooltip("冲刺卡（DashMoveNode）：套路步骤里的冲刺、位移卡池里的冲刺都算")]
        public bool dash = true;
        [Tooltip("传送卡（TeleportNode）：套路步骤里的传送、位移卡池里的传送都算。\n" +
                 "（受击传送不归它管，由传送卡自己的设置决定）")]
        public bool teleport = false;

        public bool Allows(ActionNode node)
        {
            if (node is BulletNode) return bullet;
            if (node is MeleeNode) return melee;
            if (node is DashAttackNode) return specialMelee;
            if (node is LaserNode) return specialRanged;
            if (node is DashMoveNode) return dash;
            if (node is TeleportNode) return teleport;
            return true;
        }
    }

    private BossController boss;

    // ==========================================================
    //  Inspector：按语义分列表，仅为调试方便
    // ==========================================================

    /// <summary>近身反应卡池里的一个条目</summary>
    [System.Serializable]
    public class ReactionEntry
    {
        [Tooltip("在哪个作战阶段能用：Mix = 都能用 / Ranged = 只在护盾在时 / Melee = 只在破盾后")]
        public SkillPhaseTag phaseTag = SkillPhaseTag.Mix;

        [Tooltip("满足这些条件才会反应（所有填了的条件都满足才算）：玩家距离、在 Boss 背后 / 面前、空中状态")]
        public RoutineCondition condition = new RoutineCondition();

        [Tooltip("多条同时满足条件时的抽取权重")]
        [Min(0)]
        public int weight = 100;

        [Tooltip("抽中后真正反应的概率（0~1）。不管掷没掷中，都进全局反应冷却（Reaction Cooldown）")]
        [Range(0f, 1f)]
        public float chance = 1f;

        [Tooltip("反应招：近战、突进斩等。它自己也必须够得着玩家、冷却好了、规则表允许（弹幕在发时）才会被选。\n" +
                 "冷却用卡上的 Cooldown（和其他地方共用）。弹幕卡放这里不生效")]
        public ActionNode action;
    }

    /// <summary>
    /// 卡池里的一个条目 =「卡套」：阶段标签 + 招式卡。
    /// 比喻：卡本身不变，卡套上写着"哪个阶段能用"。同一张卡在不同池子里可以套不同的卡套。
    /// 泛型参数限制这个池子只能拖哪种卡（例如 Bullets 池只能拖 BulletNode）。
    /// </summary>
    [System.Serializable]
    public class PoolEntry<T> where T : ActionNode
    {
        [Tooltip("这张卡在哪个作战阶段能被单独抽到：\n" +
                 "Mix    = 两个阶段都能用\n" +
                 "Ranged = 只在远程为主阶段（护盾在）\n" +
                 "Melee  = 只在近战为主阶段（护盾碎）")]
        public SkillPhaseTag phaseTag = SkillPhaseTag.Mix;

        [Tooltip("招式卡")]
        public T action;

        [Tooltip("情境加成（可留空）：满足条件时抽取权重乘倍率。\n" +
                 "例：玩家距离 0~4 → ×3（贴脸时更爱出）；玩家在飞 → ×0（飞行时不出）")]
        public List<WeightModifier> weightModifiers = new List<WeightModifier>();
    }

    /// <summary>套路池里的一个条目：套路 + 情境加成</summary>
    [System.Serializable]
    public class RoutineEntry
    {
        [Tooltip("套路资产。它自己的姿态 / 距离 / 玩家状态条件仍然是\"能不能用\"的开关")]
        public BossRoutine routine;

        [Tooltip("情境加成（可留空）：满足条件时这个套路的抽取权重乘倍率，是\"更爱不爱用\"的偏好")]
        public List<WeightModifier> weightModifiers = new List<WeightModifier>();
    }

    [Header("--- 配置表 ---")]
    [Tooltip("一阶段的 AI 配置表（单招池、套路池、位移卡池、近身反应池、兜底卡、规则表）。必填：留空会在 Console 报错，Boss 不出招")]
    [FormerlySerializedAs("aiConfig")]
    [SerializeField] private BossAIConfig phase1AIConfig;

    [Tooltip("二阶段的 AI 配置表。进入二阶段时换用这张。\n" +
             "留空 = 二阶段沿用一阶段的表")]
    [SerializeField] private BossAIConfig phase2AIConfig;

    [Tooltip("一阶段的战斗节奏、姿态倾向、动作倍率。必填：留空会在 Console 报错，先用默认数值")]
    [FormerlySerializedAs("tempoConfig")]
    [SerializeField] private BossTempoConfig phase1TempoConfig;

    [Tooltip("二阶段的战斗节奏、姿态倾向、动作倍率。进入二阶段时换用这张。\n" +
             "留空 = 二阶段沿用一阶段的表")]
    [SerializeField] private BossTempoConfig phase2TempoConfig;

    // ---- 生效中的配置（一阶段没拖表时用一张空表 / 默认表顶上，并报错）----
    private BossAIConfig activeAI1;
    private BossAIConfig activeAI2;
    private BossTempoConfig activeTempo1;
    private BossTempoConfig activeTempo2;

    /// <summary>一阶段生效的表。没拖 → 报错，用一张空表（招式池全空，Boss 不出招）</summary>
    private BossAIConfig P1Conf
    {
        get
        {
            if (activeAI1 == null)
            {
                if (phase1AIConfig != null) activeAI1 = phase1AIConfig;
                else
                {
                    activeAI1 = ScriptableObject.CreateInstance<BossAIConfig>();
                    activeAI1.name = "AIConfig（未配置）";
                    Debug.LogError("[BossAIDecider] 没有拖 Phase 1 AI Config！一阶段招式池为空，Boss 不会出招。", this);
                }
            }
            return activeAI1;
        }
    }

    /// <summary>二阶段生效的表：拖了二阶段表用它；没拖 → 沿用一阶段</summary>
    private BossAIConfig P2Conf
    {
        get
        {
            if (activeAI2 == null) activeAI2 = phase2AIConfig != null ? phase2AIConfig : P1Conf;
            return activeAI2;
        }
    }

    /// <summary>当前阶段生效的表</summary>
    private BossAIConfig CurrentConf
        => boss != null && boss.bossState != null && boss.bossState.bossMechanic.isPhase2 ? P2Conf : P1Conf;

    /// <summary>一阶段生效的节奏表。没拖 → 报错，用默认数值</summary>
    private BossTempoConfig P1Tempo
    {
        get
        {
            if (activeTempo1 == null)
            {
                if (phase1TempoConfig != null) activeTempo1 = phase1TempoConfig;
                else
                {
                    activeTempo1 = ScriptableObject.CreateInstance<BossTempoConfig>();
                    activeTempo1.name = "TempoConfig（未配置，默认数值）";
                    Debug.LogError("[BossAIDecider] 没有拖 Phase 1 Tempo Config！先用默认数值。", this);
                }
            }
            return activeTempo1;
        }
    }

    /// <summary>二阶段生效的节奏表：拖了用它，没拖沿用一阶段</summary>
    private BossTempoConfig P2Tempo
    {
        get
        {
            if (activeTempo2 == null) activeTempo2 = phase2TempoConfig != null ? phase2TempoConfig : P1Tempo;
            return activeTempo2;
        }
    }

    /// <summary>当前阶段生效的节奏表</summary>
    private BossTempoConfig TempoConf
        => boss != null && boss.bossState != null && boss.bossState.bossMechanic.isPhase2 ? P2Tempo : P1Tempo;

    /// <summary>当前阶段的动作倍率（招式执行快慢）</summary>
    public float ActionSpeed => Mathf.Max(0.05f, TempoConf.actionSpeed);

    private List<PoolEntry<BulletNode>> phase1Bullets => P1Conf.bullets;
    private List<PoolEntry<MeleeNode>> phase1Melees => P1Conf.melees;
    private List<PoolEntry<ActionNode>> phase1SpecialBullets => P1Conf.specialBullets;
    private List<PoolEntry<ActionNode>> phase1SpecialMelees => P1Conf.specialMelees;
    private List<PoolEntry<BulletNode>> phase2Bullets => P2Conf.bullets;
    private List<PoolEntry<MeleeNode>> phase2Melees => P2Conf.melees;
    private List<PoolEntry<ActionNode>> phase2SpecialBullets => P2Conf.specialBullets;
    private List<PoolEntry<ActionNode>> phase2SpecialMelees => P2Conf.specialMelees;
    private List<RoutineEntry> phase1Routines => P1Conf.routines;
    private List<RoutineEntry> phase2Routines => P2Conf.routines;
    private List<MovementEntry> phase1Movement => P1Conf.movement;
    private List<MovementEntry> phase2Movement => P2Conf.movement;
    private List<ReactionEntry> phase1Reactions => P1Conf.reactions;
    private List<ReactionEntry> phase2Reactions => P2Conf.reactions;
    // 下面四项跟着当前阶段的表走
    private float singleInterleaveChance => CurrentConf.singleInterleaveChance;
    private float reactionCooldown => CurrentConf.reactionCooldown;
    private ActionNode fallbackNode => CurrentConf.fallbackNode;
    private BarrageRuleTable barrageRules => CurrentConf.barrageRules;
    private ModeWeights rangedModeWeights => TempoConf.rangedModeWeights;
    private ModeWeights meleeModeWeights => TempoConf.meleeModeWeights;
    private CombatTempo rangedTempo => TempoConf.rangedTempo;
    private CombatTempo meleeTempo => TempoConf.meleeTempo;

    // ==========================================================
    //  运行时数据
    // ==========================================================

    private readonly List<ActionNode> currentPool = new List<ActionNode>();
    private List<MovementEntry> currentMovement = new List<MovementEntry>();
    private readonly List<MovementEntry> movementBuffer = new List<MovementEntry>(8);
    private List<ReactionEntry> currentReactions = new List<ReactionEntry>();
    private readonly List<ReactionEntry> reactionBuffer = new List<ReactionEntry>(8);
    private float nextReactionTime;   // 全局反应冷却：到这一刻之前不反应
    private ActionNode queuedReaction; // 走位中触发的反应招：下一次 SelectSkill 最先出它

    /// <summary>刚选出的这一招是走位中触发的反应招（打完回移动状态，接着原来的流程）</summary>
    public bool LastPickWasReaction { get; private set; }
    private readonly Dictionary<ActionNode, SkillPhaseTag> phaseTagOf
        = new Dictionary<ActionNode, SkillPhaseTag>();
    private readonly Dictionary<ActionNode, SkillCategory> categoryOf
        = new Dictionary<ActionNode, SkillCategory>();
    private readonly List<ActionNode> validBuffer = new List<ActionNode>(16);
    private readonly Dictionary<ActionNode, float> nextAvailableTime
        = new Dictionary<ActionNode, float>();

    /// <summary>
    /// 当前意图（单招），或套路里正在等靠位的那一步。MoveState 朝它的理想距离走。
    /// </summary>
    public ActionNode PendingNode { get; private set; }

    // 不看射程、下一次出手直接出的牌（靠位超时后的兜底卡）
    private ActionNode forcedNode;

    // ---- 套路运行时 ----
    private readonly List<BossRoutine> currentRoutines = new List<BossRoutine>();
    private readonly Dictionary<BossRoutine, List<WeightModifier>> routineModifiersOf
        = new Dictionary<BossRoutine, List<WeightModifier>>();
    private readonly Dictionary<ActionNode, List<WeightModifier>> modifiersOf
        = new Dictionary<ActionNode, List<WeightModifier>>();
    private readonly List<BossRoutine> routineBuffer = new List<BossRoutine>(8);
    private readonly List<float> routineWeightBuffer = new List<float>(8);
    private readonly Dictionary<BossRoutine, float> routineNextTime = new Dictionary<BossRoutine, float>();
    private BossRoutine activeRoutine;
    private int routineStep;

    // ---- 套路的后续招式间隔（BossRoutine.restAfter）----
    // 套路最后一步发出后先「挂起」，等这一招真正放完（NotifyActionEnded；脱手释放的话就是脱手那一刻）才开始计时；
    // 计时期间不选新套路（单招、位移照常）
    private float routineGateUntil = -1f;
    private bool routineEndPending;
    private float pendingRest;

    /// <summary>此刻不能选新套路（某个套路刚打完、还在它的后续招式间隔里）</summary>
    public bool RoutinesGated => Time.time < routineGateUntil;

    /// <summary>正在执行某个套路（还有后续步骤）</summary>
    public bool IsInRoutine => activeRoutine != null;

    /// <summary>
    /// 套路的下一步可以【马上】接着出（不需要先靠位）。
    /// CombatState 据此决定打完一招后是原地接下一步，还是先回移动状态。
    /// </summary>
    public bool HasImmediateFollowUp => activeRoutine != null && PendingNode == null;

    /// <summary>刚选出的这一招出手前要停顿多久（套路步骤的 delayBefore；单招为 0）</summary>
    public float NextActionDelay { get; private set; }

    /// <summary>刚选出的这一招属于哪个套路（单招为 null）。供调试文字显示</summary>
    public string LastPickRoutineName { get; private set; }

    // 刚刚发出的套路步骤（单招时为 null）。出招前停顿结束后用它重新检查候补招式
    private RoutineStep issuedStep;

    // 已经插入过候补的步骤序号（-1 = 无）。再轮到这一步时不再看候补，直接出原本的招
    private int insertedStepIndex = -1;

    /// <summary>预约的招式现在已经够得着了</summary>
    public bool IsPendingInRange
        => PendingNode != null && boss != null && IsInRange(PendingNode, boss.DistanceToPlayer);

    /// <summary>当前姿态的战斗节奏</summary>
    public CombatTempo CurrentTempo
        => (CurrentMode == BossCombatMode.Ranged ? rangedTempo : meleeTempo) ?? new CombatTempo();

    /// <summary>
    /// 当前实际移动速度 = 基础移速 × 当前阶段节奏表里当前姿态的移速倍率（倍率为负 = 旧表的 -1，当作 1）
    /// </summary>
    public float CurrentMoveSpeed
    {
        get
        {
            CombatTempo t = CurrentTempo;
            float baseSpeed = boss != null ? boss.BaseMoveSpeed : 3f;
            float multiplier = t.moveSpeedMultiplier >= 0f ? t.moveSpeedMultiplier : 1f;
            return baseSpeed * multiplier;
        }
    }

    public bool canAttack => boss != null && boss.DistanceToPlayer <= GetMaxAggroRange();

    void Awake()
    {
        boss = GetComponent<BossController>();
        RebuildPool(false);
    }

    /// <summary>
    /// 演出结束时调用：演出期间（从 startTime 起）还没到点的招式 / 套路 / 反应冷却、套路后续间隔都往后顺延 duration 秒，
    /// 相当于演出期间时间停了
    /// </summary>
    public void ShiftTimers(float startTime, float duration)
    {
        if (duration <= 0f) return;

        ShiftAfter(nextAvailableTime, startTime, duration);
        ShiftAfter(routineNextTime, startTime, duration);
        if (nextReactionTime > startTime) nextReactionTime += duration;
        if (routineGateUntil > startTime && !float.IsInfinity(routineGateUntil)) routineGateUntil += duration;
    }

    private static void ShiftAfter<T>(Dictionary<T, float> table, float startTime, float duration)
    {
        if (table.Count == 0) return;
        List<T> keys = new List<T>(table.Keys);
        foreach (T key in keys)
            if (table[key] > startTime) table[key] += duration;
    }

    public void SwitchToPhase2()
    {
        Debug.Log("[BossAIDecider] 切换到二阶段招式池！");
        ClearIntent("转阶段");
        RebuildPool(true);
        nextAvailableTime.Clear(); // 转阶段重置所有冷却
        routineNextTime.Clear();
        nextReactionTime = 0f;
    }

    /// <summary>护盾状态变了（破盾 / 回盾）→ 姿态切换，旧姿态下预约的意图作废</summary>
    public void OnCombatModeChanged()
    {
        Debug.Log($"[BossAIDecider] 姿态切换为：{(CurrentMode == BossCombatMode.Ranged ? "远程为主" : "近战为主")}");
        ClearIntent("姿态切换");
    }

    private BossCombatMode CurrentMode
        => boss != null && boss.bossState != null
            ? boss.bossState.bossMechanic.CurrentMode
            : BossCombatMode.Ranged;

    /// <summary>
    /// 招式此刻的实际抽卡权重 = 基础权重 × 姿态倍率 × 情境加成（所有满足条件的倍率相乘）
    /// </summary>
    private float EffectiveWeight(ActionNode node)
    {
        if (node == null || node.baseWeight <= 0) return 0f;

        SkillCategory c = categoryOf.TryGetValue(node, out SkillCategory cat) ? cat : SkillCategory.Ranged;
        ModeWeights w = CurrentMode == BossCombatMode.Ranged ? rangedModeWeights : meleeModeWeights;
        float weight = node.baseWeight * (w != null ? w.Get(c) : 1f);

        if (weight > 0f && boss != null && modifiersOf.TryGetValue(node, out List<WeightModifier> mods))
            weight *= WeightModifier.Factor(mods, boss.Context, boss.DistanceToPlayer);

        return weight;
    }

    /// <summary>这张卡的阶段标签是否允许在当前姿态出现</summary>
    private bool IsAllowedInMode(ActionNode node)
    {
        if (node == null) return false;
        SkillPhaseTag tag = phaseTagOf.TryGetValue(node, out SkillPhaseTag t) ? t : SkillPhaseTag.Mix;
        switch (tag)
        {
            case SkillPhaseTag.Ranged: return CurrentMode == BossCombatMode.Ranged;
            case SkillPhaseTag.Melee:  return CurrentMode == BossCombatMode.Melee;
            default:                   return true;
        }
    }

    private void RebuildPool(bool phase2)
    {
        currentPool.Clear();
        categoryOf.Clear();
        phaseTagOf.Clear();
        modifiersOf.Clear();
        routineModifiersOf.Clear();
        currentMovement = (phase2 ? phase2Movement : phase1Movement) ?? new List<MovementEntry>();
        currentReactions = (phase2 ? phase2Reactions : phase1Reactions) ?? new List<ReactionEntry>();

        currentRoutines.Clear();
        List<RoutineEntry> routines = phase2 ? phase2Routines : phase1Routines;
        if (routines != null)
        {
            for (int i = 0; i < routines.Count; i++)
            {
                RoutineEntry e = routines[i];
                if (e == null || e.routine == null || !e.routine.HasAnyStep) continue;
                if (routineModifiersOf.ContainsKey(e.routine)) continue; // 同一套路重复放入，只认第一条

                currentRoutines.Add(e.routine);
                routineModifiersOf[e.routine] = e.weightModifiers;
            }
        }

        if (phase2)
        {
            AddRange(phase2Bullets, SkillCategory.Ranged);
            AddRange(phase2Melees, SkillCategory.Melee);
            AddRange(phase2SpecialBullets, SkillCategory.Ranged);
            AddRange(phase2SpecialMelees, SkillCategory.Melee);
        }
        else
        {
            AddRange(phase1Bullets, SkillCategory.Ranged);
            AddRange(phase1Melees, SkillCategory.Melee);
            AddRange(phase1SpecialBullets, SkillCategory.Ranged);
            AddRange(phase1SpecialMelees, SkillCategory.Melee);
        }

        if (currentPool.Count == 0)
            Debug.LogWarning($"[BossAIDecider] {(phase2 ? "二" : "一")}阶段招式池为空！", this);
    }

    private void AddRange<T>(List<PoolEntry<T>> src, SkillCategory category) where T : ActionNode
    {
        if (src == null) return;
        for (int i = 0; i < src.Count; i++)
        {
            PoolEntry<T> entry = src[i];
            if (entry == null || entry.action == null) continue;

            ActionNode node = entry.action;
            currentPool.Add(node);

            // 同一张牌被放进多个桶时，以第一次出现的类别 / 标签为准
            if (!categoryOf.ContainsKey(node)) categoryOf[node] = category;
            if (!phaseTagOf.ContainsKey(node)) phaseTagOf[node] = entry.phaseTag;
            if (!modifiersOf.ContainsKey(node)) modifiersOf[node] = entry.weightModifiers;
        }
    }

    // ==========================================================
    //  距离建议：供 MoveState 使用
    // ==========================================================

    private float GetMaxAggroRange()
    {
        float maxRange = 0f;
        for (int i = 0; i < currentPool.Count; i++)
        {
            if (currentPool[i].MaxCastDistance > maxRange) maxRange = currentPool[i].MaxCastDistance;
        }

        // 套路里的招式不一定在单招池里，也要算进射程
        for (int r = 0; r < currentRoutines.Count; r++)
        {
            List<RoutineStep> steps = currentRoutines[r].steps;
            for (int s = 0; s < steps.Count; s++)
            {
                ActionNode a = steps[s] != null ? steps[s].action : null;
                if (a != null && a.MaxCastDistance > maxRange) maxRange = a.MaxCastDistance;
            }
        }

        return maxRange > 0f ? maxRange : 5f;
    }

    /// <summary>
    /// 当前应该站在离玩家多远的地方（MoveState 每帧读取）。
    /// = 意图卡的理想距离；没有意图时返回 5。
    /// </summary>
    public float GetOptimalEngagementDistance()
    {
        if (boss == null) return 5f;
        float dist = boss.DistanceToPlayer;

        if (forcedNode != null) return dist; // 马上要出的兜底卡：原地不动

        if (activeRoutine != null)
        {
            // 以套路下一步【实际会出的招】（含候补）的理想距离为目标，和单招意图一致。
            // 旧写法在"下一步已经够得着"时返回当前距离 —— Boss 会以为自己到位了，一步都不走。
            // 被弹幕拦住的那一步也朝它的理想距离走（边等边站位）
            PeekRoutine(dist, out ActionNode blocking, out ActionNode next, out _);
            ActionNode target = blocking ?? next;
            if (target != null) return PreferredDistance(target);
            return dist;
        }

        if (PendingNode != null) return PreferredDistance(PendingNode);
        return 5f;
    }

    /// <summary>
    /// 卡的理想距离：填了 preferredDistance 就用它（夹在射程内），否则取射程中点。
    /// 位移卡（传送 / 冲刺）没填时原地不动：它们射程一般填得很宽（0~99），取中点会让 Boss 先往远处走一大段。
    /// </summary>
    private float PreferredDistance(ActionNode node)
    {
        if (node.preferredDistance < 0f && (node is TeleportNode || node is DashMoveNode))
            return boss != null ? boss.DistanceToPlayer : 5f;

        if (node.preferredDistance >= 0f)
            return Mathf.Clamp(node.preferredDistance, node.minCastDistance, node.MaxCastDistance);
        return MidRange(node);
    }

    private float MidRange(ActionNode node)
        => (node.minCastDistance + node.MaxCastDistance) * 0.5f;

    // ==========================================================
    //  抽卡
    // ==========================================================

    // ==========================================================
    //  意图（MoveState 调用）
    // ==========================================================

    /// <summary>当前有没有意图（单招意图 / 套路进行中 / 兜底卡）</summary>
    public bool HasIntent => forcedNode != null || activeRoutine != null || PendingNode != null;

    /// <summary>
    /// 定下一招（意图）。已经有意图时什么都不做。
    /// 顺序：套路（按 singleInterleaveChance 概率跳过）→ 单招（阶段标签过滤、不看距离）→ 兜底卡。
    /// </summary>
    public void PlanIntent()
    {
        if (HasIntent || boss == null) return;

        float dist = boss.DistanceToPlayer;

        // ---- 1. 选新套路 ----
        if (currentRoutines.Count > 0 && !RoutinesGated && Random.value >= singleInterleaveChance)
        {
            BossRoutine routine = DrawRoutine(dist);
            if (routine != null)
            {
                StartRoutine(routine);
                return;
            }
        }

        // ---- 2. 单招：不看距离，够不着就走过去 ----
        ActionNode wanted = DrawFromPool(node => IsOffCooldown(node) && IsAllowedInMode(node));
        if (wanted != null)
        {
            PendingNode = wanted;
            Debug.Log($"[AI] 意图：{wanted.actionName}（射程 {wanted.minCastDistance}~{wanted.MaxCastDistance}，" +
                      $"理想距离 {PreferredDistance(wanted):F1}，当前 {dist:F1}）");
            return;
        }

        // ---- 3. 兜底卡 ----
        if (fallbackNode != null)
        {
            forcedNode = fallbackNode;
            Debug.Log($"[AI] 没有可用的卡，意图改为兜底：{fallbackNode.actionName}");
        }
    }

    /// <summary>
    /// 意图现在能不能出手了：在射程内（套路则是"下一步能直接出"），
    /// 或者有兜底卡要出。被弹幕在发时的规则表拦住的不算能出（等弹幕发完）。
    /// </summary>
    public bool IsIntentReady
    {
        get
        {
            if (boss == null) return false;
            if (forcedNode != null) return !IsBlockedByBarrage(forcedNode);

            float dist = boss.DistanceToPlayer;
            if (activeRoutine != null) return PeekRoutine(dist, out _, out _, out _);

            return PendingNode != null && IsInRange(PendingNode, dist) && IsOffCooldown(PendingNode)
                   && !IsBlockedByBarrage(PendingNode);
        }
    }

    /// <summary>
    /// 意图够得着、只是被规则表拦住，在等弹幕发完。MoveState 据此暂停靠位超时计时
    /// </summary>
    public bool IsWaitingForBarrage
    {
        get
        {
            if (boss == null || !BarrageActive) return false;
            if (forcedNode != null) return IsBlockedByBarrage(forcedNode);

            float dist = boss.DistanceToPlayer;
            if (activeRoutine != null)
            {
                PeekRoutine(dist, out _, out _, out bool waiting);
                return waiting;
            }

            return PendingNode != null && IsInRange(PendingNode, dist) && IsBlockedByBarrage(PendingNode);
        }
    }

    /// <summary>弹幕层里有没有弹幕在发（已经脱手、不占 Boss 身体的弹幕）</summary>
    public bool BarrageActive
        => boss != null && boss.BulletEmitter != null && boss.BulletEmitter.HasReleasedBarrage;

    /// <summary>
    /// 这张卡此刻被「弹幕在发时」的规则拦住了吗。
    /// 弹幕层没有弹幕 → 不拦；卡上选了允许 / 禁止 → 按卡；否则按规则表里这一类的设置
    /// </summary>
    public bool IsBlockedByBarrage(ActionNode node)
    {
        if (node == null || !BarrageActive) return false;
        return !AllowedWhileBarrage(node);
    }

    /// <summary>弹幕层里有弹幕在发的话，这张卡能不能出（卡上选了允许 / 禁止就按卡，否则按规则表）</summary>
    private bool AllowedWhileBarrage(ActionNode node)
    {
        switch (node.whileBarrage)
        {
            case BarrageRule.Allow:  return true;
            case BarrageRule.Forbid: return false;
            default:                 return barrageRules == null || barrageRules.Allows(node);
        }
    }

    /// <summary>
    /// 靠位超时：放弃当前意图。
    /// 先从射程内的卡里抽一张（变成新意图，马上就能出），没有就用兜底卡，再没有就什么都不定（MoveState 会重新定意图）。
    /// </summary>
    public void OnApproachTimeout()
    {
        if (boss == null) return;

        Debug.Log($"[AI] 意图 {IntentName} 靠位超时，放弃");
        ClearIntent("靠位超时");

        float dist = boss.DistanceToPlayer;
        ActionNode inRange = DrawFromPool(node => IsInRange(node, dist) && IsOffCooldown(node) && IsAllowedInMode(node)
                                                  && !IsBlockedByBarrage(node));
        if (inRange != null)
        {
            PendingNode = inRange;
            Debug.Log($"[AI] 改出射程内的：{inRange.actionName}");
            return;
        }

        if (fallbackNode != null) forcedNode = fallbackNode;
    }

    /// <summary>作废一切意图：单招意图、兜底卡、进行中的套路</summary>
    public void ClearIntent(string reason)
    {
        AbortRoutine(reason);
        ClearPending();
        forcedNode = null;
        queuedReaction = null;
    }

    /// <summary>当前意图的名字，供调试文字显示</summary>
    public string IntentName
    {
        get
        {
            if (forcedNode != null) return $"{forcedNode.actionName}（兜底）";
            if (activeRoutine != null)
            {
                string step = PendingNode != null ? PendingNode.actionName : $"第 {routineStep + 1} 步";
                return $"{activeRoutine.DisplayName} · {step}";
            }
            if (PendingNode != null) return PendingNode.actionName;
            return "无";
        }
    }

    // ==========================================================
    //  出手（CombatState 调用）
    // ==========================================================

    /// <summary>
    /// 兑现意图，返回这一次要出的招。返回 null = 现在没招可出，CombatState 应回到 MoveState。
    /// </summary>
    public ActionNode SelectSkill()
    {
        float dist = boss.DistanceToPlayer;
        NextActionDelay = 0f;
        LastPickRoutineName = null;
        LastPickWasReaction = false;
        issuedStep = null;

        // ---- 0. 走位中触发的近身反应：最先出，不动套路进度和单招意图 ----
        if (queuedReaction != null)
        {
            ActionNode reaction = queuedReaction;
            queuedReaction = null;
            LastPickRoutineName = "近身反应";
            LastPickWasReaction = true;
            return reaction; // 挑的时候已经记过冷却
        }

        // ---- 1. 兜底卡：不看射程直接出（被弹幕拦住就保留着，等弹幕发完）----
        if (forcedNode != null)
        {
            if (IsBlockedByBarrage(forcedNode)) return null;

            ActionNode node = forcedNode;
            forcedNode = null;
            return Commit(node);
        }

        // ---- 2. 套路：推进下一步 ----
        if (activeRoutine != null)
        {
            StepResult r = AdvanceRoutine(dist, out ActionNode step);
            if (r == StepResult.Node) return step;
            return null;   // Waiting：回 MoveState 靠位；Done：回 MoveState 定新意图
        }

        // ---- 3. 单招意图：出手前再校验一次 ----
        if (PendingNode != null)
        {
            ActionNode node = PendingNode;

            // 够得着但被弹幕拦住：意图保留，回移动状态边走位边等
            if (IsInRange(node, dist) && IsBlockedByBarrage(node)) return null;

            ClearPending();

            if (IsInRange(node, dist) && IsOffCooldown(node) && IsAllowedInMode(node))
                return Commit(node);

            Debug.Log($"[AI] 意图 {node.actionName} 出手前校验失败（距离 {dist:F1}），重新定意图");
        }

        return null;
    }

    /// <summary>
    /// 看一眼套路接下来能不能直接出招（不改动任何状态）。
    /// </summary>
    /// <param name="blocking">挡路的那一步：够不着且要求靠位时是它的默认招式，被弹幕拦住时是实际要出的招；能直接出时为 null</param>
    /// <param name="next">下一步此刻能直接出的招（已算上候补）；没有时为 null</param>
    /// <param name="waitingForBarrage">挡路的原因是被弹幕在发时的规则拦住（够得着，在等弹幕发完）</param>
    /// <returns>true = 下一次 SelectSkill 会出招（或套路会立刻结束 / 作废）</returns>
    private bool PeekRoutine(float dist, out ActionNode blocking, out ActionNode next, out bool waitingForBarrage)
    {
        blocking = null;
        next = null;
        waitingForBarrage = false;
        if (activeRoutine == null) return true;

        for (int i = routineStep; i < activeRoutine.steps.Count; i++)
        {
            RoutineStep step = activeRoutine.steps[i];
            if (step == null || step.action == null) continue;

            StepVariant v = ResolveVariant(step, dist, i != insertedStepIndex);
            ActionNode chosen = v != null ? v.action : step.action;
            if (IsInRange(chosen, dist))
            {
                if (IsBlockedByBarrage(chosen))
                {
                    blocking = chosen;
                    waitingForBarrage = true;
                    return false;
                }

                next = chosen;
                return true;
            }

            switch (step.whenOutOfRange)
            {
                case StepOutOfRange.Skip:  continue;
                case StepOutOfRange.Abort: return true;
                default:
                    blocking = step.action;
                    return false;
            }
        }
        return true; // 后面没有步骤了：交给 SelectSkill 收尾
    }

    // ==========================================================
    //  套路
    // ==========================================================

    private enum StepResult { Node, Waiting, Done }

    private void StartRoutine(BossRoutine routine)
    {
        activeRoutine = routine;
        routineStep = 0;
        insertedStepIndex = -1;
        ClearPending();

        if (routine.cooldown > 0f) routineNextTime[routine] = Time.time + routine.cooldown;

        Debug.Log($"[AI] 开始套路：{routine.DisplayName}（{routine.steps.Count} 步）");
    }

    /// <summary>作废正在走的套路（以及靠位预约）。外部打断（例如受击传送）也会调用</summary>
    public void AbortRoutine(string reason)
    {
        if (activeRoutine == null) return;

        Debug.Log($"[AI] 套路 {activeRoutine.DisplayName} 中断：{reason}");
        activeRoutine = null;
        routineStep = 0;
        insertedStepIndex = -1;
        ClearPending();
    }

    /// <summary>
    /// 推进当前套路：找到下一个能出的步骤。
    ///   Node    = 返回了一招，马上出
    ///   Waiting = 下一步够不着，已预约靠位；或者够得着但被弹幕拦住，在等弹幕发完
    ///   Done    = 套路结束或作废
    /// </summary>
    private StepResult AdvanceRoutine(float dist, out ActionNode node)
    {
        node = null;

        while (activeRoutine != null && routineStep < activeRoutine.steps.Count)
        {
            RoutineStep step = activeRoutine.steps[routineStep];
            if (step == null || step.action == null)
            {
                routineStep++;
                continue;
            }

            // 先看候补招式（这一步已经插入过就不再看）
            StepVariant variant = ResolveVariant(step, dist, routineStep != insertedStepIndex);
            ActionNode chosen = variant != null ? variant.action : step.action;

            if (IsInRange(chosen, dist))
            {
                // 被弹幕在发时的规则拦住：不跳过、不作废，回移动状态边走位边等弹幕发完
                if (IsBlockedByBarrage(chosen))
                {
                    PendingNode = chosen;
                    return StepResult.Waiting;
                }

                ClearPending();
                NextActionDelay = step.delayBefore;
                LastPickRoutineName = activeRoutine.DisplayName;

                if (variant != null && variant.mode == VariantMode.Insert)
                {
                    // 插入：出候补，但不前进 —— 下一次还停在这一步，出原本的招
                    Debug.Log($"[AI] 套路 {activeRoutine.DisplayName} 第 {routineStep + 1} 步插入候补：{chosen.actionName}");
                    insertedStepIndex = routineStep;
                    issuedStep = null; // 插入的招不参与停顿后的换招
                    node = Commit(chosen);
                    if (variant.endRoutineAfter) EndRoutineAfterVariant(chosen);
                    return StepResult.Node;
                }

                if (variant != null)
                    Debug.Log($"[AI] 套路 {activeRoutine.DisplayName} 第 {routineStep + 1} 步改出候补：{chosen.actionName}");

                issuedStepAllowsVariants = routineStep != insertedStepIndex;
                routineStep++;
                insertedStepIndex = -1;
                issuedStep = step;
                node = Commit(chosen);

                // 候补勾了「出完结束套路」→ 套路到此结束（算打完），后面的步骤都不出
                if (variant != null && variant.endRoutineAfter)
                {
                    issuedStep = null; // 套路已经收尾：停顿后不再换回默认招式
                    EndRoutineAfterVariant(chosen);
                    return StepResult.Node;
                }

                // 这是最后一步 → 套路到此结束，打完这招后正常回移动状态
                if (routineStep >= activeRoutine.steps.Count)
                {
                    Debug.Log($"[AI] 套路 {activeRoutine.DisplayName} 完成");
                    BeginRoutineRest(activeRoutine, waitForActionEnd: true);
                    activeRoutine = null;
                    routineStep = 0;
                }
                return StepResult.Node;
            }

            switch (step.whenOutOfRange)
            {
                case StepOutOfRange.Skip:
                    Debug.Log($"[AI] 套路 {activeRoutine.DisplayName} 第 {routineStep + 1} 步够不着，跳过");
                    routineStep++;
                    continue;

                case StepOutOfRange.Abort:
                    AbortRoutine($"第 {routineStep + 1} 步够不着");
                    return StepResult.Done;

                default: // Approach：回 MoveState 走过去；走太久由 MoveState 判超时
                    PendingNode = step.action;
                    return StepResult.Waiting;
            }
        }

        if (activeRoutine != null)
        {
            Debug.Log($"[AI] 套路 {activeRoutine.DisplayName} 完成");
            BeginRoutineRest(activeRoutine, waitForActionEnd: false); // 后面的步骤都跳过了：现在就算打完
            activeRoutine = null;
            routineStep = 0;
        }
        insertedStepIndex = -1;
        ClearPending();
        return StepResult.Done;
    }

    /// <summary>
    /// 候补勾了「出完结束套路」：像打完最后一步一样收尾（等这一招放完再开始后续招式间隔），后面的步骤都不出
    /// </summary>
    private void EndRoutineAfterVariant(ActionNode chosen)
    {
        if (activeRoutine == null) return;
        Debug.Log($"[AI] 套路 {activeRoutine.DisplayName} 由候补「{chosen.actionName}」结束（出完这招就算打完）");
        BeginRoutineRest(activeRoutine, waitForActionEnd: true);
        activeRoutine = null;
        routineStep = 0;
        insertedStepIndex = -1;
    }

    /// <summary>
    /// 这一步此刻生效的候补：从上往下找第一个【条件满足且够得着】的；没有返回 null（出默认招式）。
    /// </summary>
    /// <param name="allowVariants">false = 这一步已经插入过候补，不再看候补</param>
    /// <param name="replaceOnly">true = 只看 Replace 候补（停顿后的换招用）</param>
    private StepVariant ResolveVariant(RoutineStep step, float dist, bool allowVariants, bool replaceOnly = false)
    {
        if (!allowVariants || step.variants == null || boss == null) return null;

        BossContext ctx = boss.Context;
        for (int i = 0; i < step.variants.Count; i++)
        {
            StepVariant v = step.variants[i];
            if (v == null || v.action == null || v.condition == null) continue;
            if (replaceOnly && v.mode != VariantMode.Replace) continue;
            if (!IsInRange(v.action, dist)) continue;
            if (v.condition.Evaluate(ctx, dist)) return v;
        }
        return null;
    }

    // 刚发出的那一步当时是否允许看候补（插入过的步骤不允许）
    private bool issuedStepAllowsVariants = true;

    /// <summary>
    /// 出招前停顿结束后由 CombatState 调用：玩家在停顿期间可能换了位置，按此刻情况再选一次。
    /// 结果变了且新招够得着 → 换招（新招照常记冷却）；否则维持原来的招。
    /// 单招（不在套路里）直接原样返回。
    /// </summary>
    public ActionNode RefreshStepChoice(ActionNode current)
    {
        if (issuedStep == null || boss == null) return current;

        float dist = boss.DistanceToPlayer;
        // 停顿后只在 Replace 候补和默认招式之间换：Insert 候补此时这一步已经前进，插不进去了
        StepVariant v = ResolveVariant(issuedStep, dist, issuedStepAllowsVariants, replaceOnly: true);
        ActionNode chosen = v != null ? v.action : issuedStep.action;

        if (chosen == null || chosen == current || !IsInRange(chosen, dist)) return current;

        Debug.Log($"[AI] 停顿期间玩家走位变化，换招：{current.actionName} → {chosen.actionName}");
        StampCooldown(chosen);

        // 换成的候补勾了「出完结束套路」→ 套路在这里收尾（这一步是最后一步时套路已经收过尾，不重复）
        if (v != null && v.endRoutineAfter)
        {
            issuedStep = null;
            EndRoutineAfterVariant(chosen);
        }
        return chosen;
    }

    /// <summary>套路打完：开始（或挂起等这一招放完再开始）它的后续招式间隔</summary>
    private void BeginRoutineRest(BossRoutine routine, bool waitForActionEnd)
    {
        float rest = routine != null ? routine.restAfter : 0f;
        if (rest <= 0f) return;

        if (waitForActionEnd)
        {
            routineEndPending = true;
            pendingRest = rest;
            routineGateUntil = float.PositiveInfinity; // 最后一招还在放：先挂起
        }
        else
        {
            routineGateUntil = Time.time + rest;
            Debug.Log($"[AI] 套路 {routine.DisplayName} 后续间隔 {rest:F1} 秒：期间不选新套路");
        }
    }

    /// <summary>
    /// 一招放完（或被打断）时由出招状态调用。
    /// 刚放完的是套路的最后一招 → 从现在开始它的后续招式间隔；被打断 → 不算打完，不触发间隔
    /// </summary>
    public void NotifyActionEnded(bool completed)
    {
        if (!routineEndPending) return;
        routineEndPending = false;

        if (!completed)
        {
            routineGateUntil = -1f;
            return;
        }

        routineGateUntil = Time.time + pendingRest;
        Debug.Log($"[AI] 套路后续间隔 {pendingRest:F1} 秒：期间不选新套路");
    }

    /// <summary>
    /// 刚选出的这一招要不要脱手释放，用哪份参数。只有弹幕卡能脱手。
    /// 套路步骤选了不脱手 / 脱手就按步骤；否则（跟随弹幕卡 / 单招 / 插入的候补）按弹幕卡上的。
    /// 调用时机同 ResolveFireMove
    /// </summary>
    public bool ResolveRelease(ActionNode node, out ReleaseSettings settings)
    {
        settings = null;
        if (!(node is BulletNode bullet)) return false;

        if (issuedStep != null)
        {
            switch (issuedStep.releaseMode)
            {
                case StepReleaseMode.NoRelease:
                    return false;
                case StepReleaseMode.Release:
                    settings = issuedStep.release;
                    return settings != null;
            }
        }

        if (!bullet.allowRelease) return false;
        settings = bullet.release;
        return settings != null;
    }

    /// <summary>
    /// 刚选出的这张弹幕转到弹幕层后（脱手 / 近身反应托管）跟不跟随 Boss。
    /// 套路步骤选了 Release 就按步骤上的勾选；否则（跟随弹幕卡 / 不脱手 / 单招 / 插入的候补）按弹幕卡上的。
    /// 调用时机同 ResolveRelease
    /// </summary>
    public bool ResolveFollowBoss(ActionNode node)
    {
        if (!(node is BulletNode bullet)) return true;
        if (issuedStep != null && issuedStep.releaseMode == StepReleaseMode.Release)
            return issuedStep.followBossWhenReleased;
        return bullet.followBossWhenReleased;
    }

    /// <summary>
    /// 刚选出的这一招允不允许近身反应、反应后取不取回。只有弹幕卡有。
    /// 套路步骤选了 Off / On 就按步骤；否则（跟随弹幕卡 / 单招 / 插入的候补）按弹幕卡上的。调用时机同 ResolveFireMove
    /// </summary>
    public bool ResolveReaction(ActionNode node, out bool reclaim)
    {
        reclaim = false;
        if (!(node is BulletNode bullet)) return false;

        if (issuedStep != null)
        {
            switch (issuedStep.reactionMode)
            {
                case StepReactionMode.Off:
                    return false;
                case StepReactionMode.On:
                    reclaim = issuedStep.reclaimAfterReaction;
                    return true;
            }
        }

        if (!bullet.allowCloseReaction) return false;
        reclaim = bullet.reclaimAfterReaction;
        return true;
    }

    // ==========================================================
    //  近身反应（CombatState / MoveState 调用）
    // ==========================================================

    /// <summary>
    /// 玩家贴近了吗？从近身反应卡池里挑一张此刻能出的反应招。null = 不反应。
    /// 能出 = 阶段标签允许、条件满足、够得着、冷却好了、规则表允许（反应时弹幕层里一定有弹幕在发）。
    /// 有能出的就按权重挑一张、按它的概率掷骰：不管掷没掷中都进全局反应冷却；掷中就记这张卡的冷却并返回它。
    /// 调用方负责先确认「现在有允许近身反应的弹幕」
    /// </summary>
    public ActionNode TryPickReaction()
    {
        if (boss == null || currentReactions == null || currentReactions.Count == 0) return null;
        if (Time.time < nextReactionTime) return null;

        float dist = boss.DistanceToPlayer;
        BossContext ctx = boss.Context;

        reactionBuffer.Clear();
        int total = 0;
        for (int i = 0; i < currentReactions.Count; i++)
        {
            ReactionEntry e = currentReactions[i];
            if (e == null || e.action == null || e.weight <= 0 || e.condition == null) continue;
            if (e.action is BulletNode) continue; // 弹幕卡不能当反应招
            if (!IsPhaseTagAllowed(e.phaseTag)) continue;
            if (!IsInRange(e.action, dist) || !IsOffCooldown(e.action)) continue;
            if (!AllowedWhileBarrage(e.action)) continue;
            if (!e.condition.Evaluate(ctx, dist)) continue;

            reactionBuffer.Add(e);
            total += e.weight;
        }

        if (reactionBuffer.Count == 0) return null;

        int roll = Random.Range(0, total);
        ReactionEntry pick = reactionBuffer[reactionBuffer.Count - 1];
        int acc = 0;
        for (int i = 0; i < reactionBuffer.Count; i++)
        {
            acc += reactionBuffer[i].weight;
            if (roll < acc) { pick = reactionBuffer[i]; break; }
        }

        nextReactionTime = Time.time + Mathf.Max(0f, reactionCooldown);

        if (Random.value >= pick.chance)
        {
            Debug.Log($"[AI] 近身反应没掷中（{pick.action.actionName}，概率 {pick.chance:F2}），{reactionCooldown:F1} 秒后再看");
            return null;
        }

        Debug.Log($"[AI] 近身反应：{pick.action.actionName}（距离 {dist:F1}）");
        return Commit(pick.action);
    }

    /// <summary>走位中触发的反应招：放进最优先的槽，下一次 SelectSkill 最先出它</summary>
    public void QueueReaction(ActionNode reaction) => queuedReaction = reaction;

    /// <summary>
    /// 刚选出的这一招开火时怎么移动。只有弹幕卡会动，别的卡一律站定。
    /// 套路步骤选了自己的方式就用步骤上的，否则（跟随弹幕卡 / 单招 / 插入的候补）用弹幕卡上的。
    /// 要在 SelectSkill（以及停顿后的 RefreshStepChoice）之后、下一次 SelectSkill 之前调用。
    /// </summary>
    public FireMoveMode ResolveFireMove(ActionNode node, out FireMoveSettings settings)
    {
        settings = null;
        if (!(node is BulletNode bullet)) return FireMoveMode.StandStill;

        if (issuedStep != null)
        {
            switch (issuedStep.fireMoveMode)
            {
                case StepFireMoveMode.StandStill:
                    return FireMoveMode.StandStill;
                case StepFireMoveMode.KeepDistance:
                    settings = issuedStep.fireMove;
                    return FireMoveMode.KeepDistance;
                case StepFireMoveMode.MoveThenHold:
                    settings = issuedStep.fireMove;
                    return FireMoveMode.MoveThenHold;
            }
        }

        settings = bullet.fireMove;
        return bullet.fireMoveMode;
    }

    /// <summary>从当前套路池里按权重挑一个条件满足的套路</summary>
    private BossRoutine DrawRoutine(float dist)
    {
        routineBuffer.Clear();
        routineWeightBuffer.Clear();
        float total = 0f;
        BossCombatMode mode = CurrentMode;
        BossContext ctx = boss.Context;
        ModeWeights modeWeights = mode == BossCombatMode.Ranged ? rangedModeWeights : meleeModeWeights;

        for (int i = 0; i < currentRoutines.Count; i++)
        {
            BossRoutine r = currentRoutines[i];
            if (r.baseWeight <= 0) continue;

            if (r.mode == RoutineMode.Ranged && mode != BossCombatMode.Ranged) continue;
            if (r.mode == RoutineMode.Melee && mode != BossCombatMode.Melee) continue;

            if (routineNextTime.TryGetValue(r, out float readyAt) && Time.time < readyAt) continue;
            if (dist < r.minPlayerDistance || dist > r.maxPlayerDistance) continue;
            if (!PlayerConditionMet(r.playerAirCondition)) continue;

            // 姿态倾向（按套路的招式定位取倍率）× 情境加成
            float w = r.baseWeight * (modeWeights != null ? modeWeights.Get(r.role) : 1f);
            if (w > 0f && routineModifiersOf.TryGetValue(r, out List<WeightModifier> mods))
                w *= WeightModifier.Factor(mods, ctx, dist);
            if (w <= 0f) continue;

            routineBuffer.Add(r);
            routineWeightBuffer.Add(w);
            total += w;
        }

        if (total <= 0f) return null;

        float roll = Random.Range(0f, total);
        float acc = 0f;
        for (int i = 0; i < routineBuffer.Count; i++)
        {
            acc += routineWeightBuffer[i];
            if (roll < acc) return routineBuffer[i];
        }
        return routineBuffer[routineBuffer.Count - 1];
    }

    private bool PlayerConditionMet(PlayerAirCondition condition)
    {
        if (condition == PlayerAirCondition.Any || boss == null) return true;

        BossContext ctx = boss.Context;
        switch (condition)
        {
            case PlayerAirCondition.Grounded: return ctx.IsPlayerGrounded;
            case PlayerAirCondition.Airborne: return !ctx.IsPlayerGrounded;
            case PlayerAirCondition.Flying:   return ctx.IsPlayerFlying;
            default: return true;
        }
    }

    // ==========================================================
    //  单招抽卡（套路都不合适时的兜底）
    // ==========================================================

    private ActionNode DrawFromPool(System.Func<ActionNode, bool> filter)
    {
        validBuffer.Clear();
        float totalWeight = 0f;

        for (int i = 0; i < currentPool.Count; i++)
        {
            ActionNode node = currentPool[i];
            float w = EffectiveWeight(node);   // 基础权重 × 姿态倍率
            if (w <= 0f) continue;
            if (!filter(node)) continue;

            validBuffer.Add(node);
            totalWeight += w;
        }

        if (totalWeight <= 0f) return null;

        float roll = Random.Range(0f, totalWeight);
        float acc = 0f;

        for (int i = 0; i < validBuffer.Count; i++)
        {
            acc += EffectiveWeight(validBuffer[i]);
            if (roll < acc) return validBuffer[i];
        }

        return validBuffer[validBuffer.Count - 1];
    }

    // ==========================================================
    //  位移卡池（MoveState 调用）
    // ==========================================================

    /// <summary>
    /// 问一句"这次要不要闪过去 / 冲过去"。返回要用的位移卡（传送卡或冲刺卡）；null = 走路。
    /// 只做选择、不记冷却 —— 位移真正发动后由调用方 StampCooldown。
    /// </summary>
    /// <param name="cornered">true = 撞墙了，只看 Cornered 条目（按列表顺序）；
    /// false = 移动阶段开始，看 TooFar / TooClose / Random（按权重抽）</param>
    /// <summary>最近一次选中的位移卡，形如"BehindPlayer（TooFar）"。供移动意图文字显示</summary>
    public string LastMovementPickLabel { get; private set; }

    /// <param name="dashOnly">true = 只看冲刺卡（弹幕脱手后、出下一招前的走位允许冲刺时用）</param>
    public ActionNode PickMovement(bool cornered, bool dashOnly = false)
    {
        if (boss == null || currentMovement == null || currentMovement.Count == 0) return null;

        if (cornered)
        {
            for (int i = 0; i < currentMovement.Count; i++)
            {
                MovementEntry e = currentMovement[i];
                if (!IsMovementUsable(e, dashOnly) || e.trigger != MoveTrigger.Cornered) continue;
                if (Random.value >= e.chance) continue;

                Debug.Log($"[AI] 撞墙，位移：{e.action.actionName}");
                LastMovementPickLabel = $"{e.action.actionName}（{MoveTrigger.Cornered}）";
                return e.action;
            }
            return null;
        }

        float dist = boss.DistanceToPlayer;
        float gap = dist - GetOptimalEngagementDistance(); // 正 = 太远，负 = 太近

        movementBuffer.Clear();
        int total = 0;
        for (int i = 0; i < currentMovement.Count; i++)
        {
            MovementEntry e = currentMovement[i];
            if (!IsMovementUsable(e, dashOnly) || e.weight <= 0) continue;

            bool match;
            switch (e.trigger)
            {
                case MoveTrigger.TooFar:   match = gap >= e.minGap && gap <= e.maxGap; break;
                case MoveTrigger.TooClose: match = -gap >= e.minGap && -gap <= e.maxGap; break;
                case MoveTrigger.Random:   match = true; break;
                default:                   match = false; break; // Cornered 只在撞墙时看
            }
            if (!match) continue;

            movementBuffer.Add(e);
            total += e.weight;
        }

        if (movementBuffer.Count == 0) return null;

        int roll = Random.Range(0, total);
        MovementEntry pick = movementBuffer[movementBuffer.Count - 1];
        int acc = 0;
        for (int i = 0; i < movementBuffer.Count; i++)
        {
            acc += movementBuffer[i].weight;
            if (roll < acc) { pick = movementBuffer[i]; break; }
        }

        if (Random.value >= pick.chance) return null; // 抽中了但这次选择走路

        Debug.Log($"[AI] 位移（{pick.trigger}，差距 {gap:F1}）：{pick.action.actionName}");
        LastMovementPickLabel = $"{pick.action.actionName}（{pick.trigger}）";
        return pick.action;
    }

    /// <summary>条目可用：配了传送卡或冲刺卡、阶段标签允许、不在冷却、没被弹幕在发时的规则拦住</summary>
    private bool IsMovementUsable(MovementEntry e, bool dashOnly = false)
    {
        if (e == null || e.action == null) return false;
        if (!(e.action is TeleportNode) && !(e.action is DashMoveNode)) return false;
        if (dashOnly && !(e.action is DashMoveNode)) return false;
        if (!IsOffCooldown(e.action)) return false;
        if (IsBlockedByBarrage(e.action)) return false;
        return IsPhaseTagAllowed(e.phaseTag);
    }

    /// <summary>卡池条目的阶段标签是否允许在当前姿态使用</summary>
    private bool IsPhaseTagAllowed(SkillPhaseTag tag)
    {
        switch (tag)
        {
            case SkillPhaseTag.Ranged: return CurrentMode == BossCombatMode.Ranged;
            case SkillPhaseTag.Melee:  return CurrentMode == BossCombatMode.Melee;
            default:                   return true;
        }
    }

    // ==========================================================
    //  辅助
    // ==========================================================

    private bool IsInRange(ActionNode node, float dist)
        => dist >= node.minCastDistance && dist <= node.MaxCastDistance;

    /// <summary>
    /// 这张卡冷却好了吗。公开给 BAE_Teleporter 等"AI 之外也会用卡"的部门查询，
    /// 保证一张卡只有一份冷却（AI 主动用、受击被动触发共用）。
    /// </summary>
    public bool IsOffCooldown(ActionNode node)
    {
        if (node == null) return false;
        if (node.cooldown <= 0f) return true;
        return !nextAvailableTime.TryGetValue(node, out float readyAt) || Time.time >= readyAt;
    }

    /// <summary>在 AI 之外用掉了一张卡（例如受击传送）→ 照样记一次冷却</summary>
    public void StampCooldown(ActionNode node)
    {
        if (node != null && node.cooldown > 0f) nextAvailableTime[node] = Time.time + node.cooldown;
    }

    private ActionNode Commit(ActionNode node)
    {
        StampCooldown(node);
        Debug.Log($"[AI] 出牌: {node.actionName}（{node.GetType().Name}，" +
                  $"{(CurrentMode == BossCombatMode.Ranged ? "远程为主" : "近战为主")}）");
        return node;
    }

    private void ClearPending()
    {
        PendingNode = null;
    }
}
