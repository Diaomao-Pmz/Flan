using System.Collections.Generic;
using UnityEngine;

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
/// 抽卡权重 = 招式基础权重 × 当前姿态对该类别的倍率：
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
/// 【位移卡池】传送卡不再参与出招抽卡，而是"到位的手段"：
/// 移动阶段开始时（以及撞墙时），MoveState 调 PickMovementTeleport() 问一句"要不要闪过去"。
///   · Cornered（撞墙）：按列表顺序取第一张可用的 —— 保命用，要稳定
///   · TooFar / TooClose / Random：所有满足条件的放一起按权重抽一张，再按它的 Chance 决定闪还是走
///     （多选项随机 + 概率 + 冷却，玩家能感觉到规律，但骗不出固定反应）
/// </summary>
public class BossAIDecider : MonoBehaviour
{
    /// <summary>招式类别 —— 由它被放进哪个桶决定</summary>
    public enum SkillCategory
    {
        Ranged,   // Bullets / SpecialBullets
        Melee     // Melees / SpecialMelees
    }

    /// <summary>某个姿态下，两类招式的权重倍率</summary>
    [System.Serializable]
    public class ModeWeights
    {
        [Min(0f)] public float ranged = 1f;
        [Min(0f)] public float melee = 1f;

        public ModeWeights() { }

        public ModeWeights(float ranged, float melee)
        {
            this.ranged = ranged;
            this.melee = melee;
        }

        public float Get(SkillCategory c) => c == SkillCategory.Ranged ? ranged : melee;
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

        [Tooltip("抽中后真正传送的概率（0~1）。没闪就走路过去")]
        [Range(0f, 1f)]
        public float chance = 1f;

        [Tooltip("传送卡。冷却用卡上的 Cooldown（与受击传送、套路共用）。\n" +
                 "Target Type 推荐 Intent Distance：落在离玩家正好是意图理想距离的位置")]
        public TeleportNode action;
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

        [Tooltip("这个姿态下 Boss 的移动速度（格/秒）")]
        [Min(0f)]
        public float moveSpeed = 3f;

        [Tooltip("进入二阶段后，上面的移动速度再乘这个倍率。1 = 不变")]
        [Min(0f)]
        public float phase2SpeedMultiplier = 1f;

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

    private BossController boss;

    // ==========================================================
    //  Inspector：按语义分列表，仅为调试方便
    // ==========================================================

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

    [Header("--- 一阶段招式池（每个元素：阶段标签 + 招式）---")]
    [SerializeField] private List<PoolEntry<BulletNode>> phase1Bullets = new List<PoolEntry<BulletNode>>();
    [SerializeField] private List<PoolEntry<MeleeNode>> phase1Melees = new List<PoolEntry<MeleeNode>>();

    [Tooltip("远程系特殊技能（激光等）。放任何 ActionNode 派生资产即可。")]
    [SerializeField] private List<PoolEntry<ActionNode>> phase1SpecialBullets = new List<PoolEntry<ActionNode>>();

    [Tooltip("近战系特殊技能（突进斩等）。放任何 ActionNode 派生资产即可。")]
    [SerializeField] private List<PoolEntry<ActionNode>> phase1SpecialMelees = new List<PoolEntry<ActionNode>>();

    [Header("--- 二阶段招式池（每个元素：阶段标签 + 招式）---")]
    [SerializeField] private List<PoolEntry<BulletNode>> phase2Bullets = new List<PoolEntry<BulletNode>>();
    [SerializeField] private List<PoolEntry<MeleeNode>> phase2Melees = new List<PoolEntry<MeleeNode>>();
    [SerializeField] private List<PoolEntry<ActionNode>> phase2SpecialBullets = new List<PoolEntry<ActionNode>>();
    [SerializeField] private List<PoolEntry<ActionNode>> phase2SpecialMelees = new List<PoolEntry<ActionNode>>();

    [Header("--- 套路池（每个元素：套路 + 情境加成）---")]
    [Tooltip("一阶段可用的套路。会按套路自己的姿态 / 距离 / 玩家状态条件筛选")]
    [SerializeField] private List<RoutineEntry> phase1Routines = new List<RoutineEntry>();

    [Tooltip("二阶段可用的套路")]
    [SerializeField] private List<RoutineEntry> phase2Routines = new List<RoutineEntry>();

    [Tooltip("有合适套路时，仍有这个概率改出单招（节奏穿插，让 Boss 不那么好预判）。0 = 有套路就一定走套路")]
    [Range(0f, 1f)]
    [SerializeField] private float singleInterleaveChance = 0.2f;

    [Header("--- 位移卡池（传送：到位的手段，不参与出招抽卡）---")]
    [Tooltip("一阶段的位移卡")]
    [SerializeField] private List<MovementEntry> phase1Movement = new List<MovementEntry>();

    [Tooltip("二阶段的位移卡")]
    [SerializeField] private List<MovementEntry> phase2Movement = new List<MovementEntry>();

    [Header("--- 姿态倾向（权重倍率）---")]
    [Tooltip("护盾在（远程为主）时，两类招式的权重倍率。0 = 这一类完全不出")]
    [SerializeField] private ModeWeights rangedModeWeights = new ModeWeights(3f, 1f);

    [Tooltip("护盾碎（近战为主）时，两类招式的权重倍率。0 = 这一类完全不出")]
    [SerializeField] private ModeWeights meleeModeWeights = new ModeWeights(1f, 3f);

    [Header("--- 战斗节奏（每个作战姿态一套）---")]
    [Tooltip("远程为主（护盾在）时的节奏")]
    [SerializeField] private CombatTempo rangedTempo = new CombatTempo(0.8f, 1.8f, true, 4f);

    [Tooltip("近战为主（护盾碎）时的节奏")]
    [SerializeField] private CombatTempo meleeTempo = new CombatTempo(0.2f, 0.5f, true, 4f);

    [Tooltip("卡池全空或全在冷却时的兜底牌。强烈建议配一张，否则会出现状态抖动。")]
    [SerializeField] private ActionNode fallbackNode;

    // ==========================================================
    //  运行时数据
    // ==========================================================

    private readonly List<ActionNode> currentPool = new List<ActionNode>();
    private List<MovementEntry> currentMovement = new List<MovementEntry>();
    private readonly List<MovementEntry> movementBuffer = new List<MovementEntry>(8);
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
    /// 当前实际移动速度 = 当前姿态的移动速度 × （二阶段 ? 该姿态的二阶段倍率 : 1）
    /// </summary>
    public float CurrentMoveSpeed
    {
        get
        {
            CombatTempo t = CurrentTempo;
            bool phase2 = boss != null && boss.bossState != null && boss.bossState.bossMechanic.isPhase2;
            return t.moveSpeed * (phase2 ? t.phase2SpeedMultiplier : 1f);
        }
    }

    public bool canAttack => boss != null && boss.DistanceToPlayer <= GetMaxAggroRange();

    void Awake()
    {
        boss = GetComponent<BossController>();
        RebuildPool(false);
    }

    public void SwitchToPhase2()
    {
        Debug.Log("[BossAIDecider] 切换到二阶段招式池！");
        ClearIntent("转阶段");
        RebuildPool(true);
        nextAvailableTime.Clear(); // 转阶段重置所有冷却
        routineNextTime.Clear();
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
            PeekRoutine(dist, out ActionNode blocking, out ActionNode next);
            ActionNode target = blocking ?? next;
            if (target != null) return PreferredDistance(target);
            return dist;
        }

        if (PendingNode != null) return PreferredDistance(PendingNode);
        return 5f;
    }

    /// <summary>
    /// 卡的理想距离：填了 preferredDistance 就用它（夹在射程内），否则取射程中点。
    /// </summary>
    private float PreferredDistance(ActionNode node)
    {
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
        if (currentRoutines.Count > 0 && Random.value >= singleInterleaveChance)
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
    /// 或者有兜底卡要出。
    /// </summary>
    public bool IsIntentReady
    {
        get
        {
            if (boss == null) return false;
            if (forcedNode != null) return true;

            float dist = boss.DistanceToPlayer;
            if (activeRoutine != null) return PeekRoutine(dist, out _, out _);

            return PendingNode != null && IsInRange(PendingNode, dist) && IsOffCooldown(PendingNode);
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
        ActionNode inRange = DrawFromPool(node => IsInRange(node, dist) && IsOffCooldown(node) && IsAllowedInMode(node));
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
        issuedStep = null;

        // ---- 1. 兜底卡：不看射程直接出 ----
        if (forcedNode != null)
        {
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
    /// <param name="blocking">挡路的那一步（够不着且要求靠位）的默认招式；能直接出时为 null</param>
    /// <param name="next">下一步此刻能直接出的招（已算上候补）；没有时为 null</param>
    /// <returns>true = 下一次 SelectSkill 会出招（或套路会立刻结束 / 作废）</returns>
    private bool PeekRoutine(float dist, out ActionNode blocking, out ActionNode next)
    {
        blocking = null;
        next = null;
        if (activeRoutine == null) return true;

        for (int i = routineStep; i < activeRoutine.steps.Count; i++)
        {
            RoutineStep step = activeRoutine.steps[i];
            if (step == null || step.action == null) continue;

            StepVariant v = ResolveVariant(step, dist, i != insertedStepIndex);
            ActionNode chosen = v != null ? v.action : step.action;
            if (IsInRange(chosen, dist))
            {
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
    ///   Waiting = 下一步够不着，已预约靠位
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
                    return StepResult.Node;
                }

                if (variant != null)
                    Debug.Log($"[AI] 套路 {activeRoutine.DisplayName} 第 {routineStep + 1} 步改出候补：{chosen.actionName}");

                issuedStepAllowsVariants = routineStep != insertedStepIndex;
                routineStep++;
                insertedStepIndex = -1;
                issuedStep = step;
                node = Commit(chosen);

                // 这是最后一步 → 套路到此结束，打完这招后正常回移动状态
                if (routineStep >= activeRoutine.steps.Count)
                {
                    Debug.Log($"[AI] 套路 {activeRoutine.DisplayName} 完成");
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
            activeRoutine = null;
            routineStep = 0;
        }
        insertedStepIndex = -1;
        ClearPending();
        return StepResult.Done;
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
        return chosen;
    }

    /// <summary>从当前套路池里按权重挑一个条件满足的套路</summary>
    private BossRoutine DrawRoutine(float dist)
    {
        routineBuffer.Clear();
        routineWeightBuffer.Clear();
        float total = 0f;
        BossCombatMode mode = CurrentMode;
        BossContext ctx = boss.Context;

        for (int i = 0; i < currentRoutines.Count; i++)
        {
            BossRoutine r = currentRoutines[i];
            if (r.baseWeight <= 0) continue;

            if (r.mode == RoutineMode.Ranged && mode != BossCombatMode.Ranged) continue;
            if (r.mode == RoutineMode.Melee && mode != BossCombatMode.Melee) continue;

            if (routineNextTime.TryGetValue(r, out float readyAt) && Time.time < readyAt) continue;
            if (dist < r.minPlayerDistance || dist > r.maxPlayerDistance) continue;
            if (!PlayerConditionMet(r.playerAirCondition)) continue;

            // 情境加成
            float w = r.baseWeight;
            if (routineModifiersOf.TryGetValue(r, out List<WeightModifier> mods))
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
    /// 问一句"这次要不要闪过去"。返回要用的传送卡；null = 走路。
    /// 只做选择、不记冷却 —— 传送真正发动后由调用方 StampCooldown。
    /// </summary>
    /// <param name="cornered">true = 撞墙了，只看 Cornered 条目（按列表顺序）；
    /// false = 移动阶段开始，看 TooFar / TooClose / Random（按权重抽）</param>
    /// <summary>最近一次选中的位移卡，形如"BehindPlayer（TooFar）"。供移动意图文字显示</summary>
    public string LastMovementPickLabel { get; private set; }

    public TeleportNode PickMovementTeleport(bool cornered)
    {
        if (boss == null || currentMovement == null || currentMovement.Count == 0) return null;

        if (cornered)
        {
            for (int i = 0; i < currentMovement.Count; i++)
            {
                MovementEntry e = currentMovement[i];
                if (!IsMovementUsable(e) || e.trigger != MoveTrigger.Cornered) continue;
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
            if (!IsMovementUsable(e) || e.weight <= 0) continue;

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

    /// <summary>条目可用：配了卡、阶段标签允许、不在冷却</summary>
    private bool IsMovementUsable(MovementEntry e)
    {
        if (e == null || e.action == null) return false;
        if (!IsOffCooldown(e.action)) return false;

        switch (e.phaseTag)
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
