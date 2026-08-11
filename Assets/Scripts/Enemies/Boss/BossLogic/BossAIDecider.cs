using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss 的决策大脑。
///
/// Inspector 按语义分列表，方便调试；内部仍合并成一个统一卡池抽卡，
/// 抽卡逻辑与技能类型完全无关（只读基类的 baseWeight / min / maxCastDistance / cooldown）。
///
/// 【本次改动】Specials 拆成 SpecialBullets / SpecialMelees 两个桶。
/// 注意它们的类型仍是 List&lt;ActionNode&gt; 而不是 List&lt;LaserNode&gt; —— 这是有意的：
/// 按语义分桶而非按类型分桶，以后再加特殊技能只要丢进对应的桶，本文件一行都不用改。
/// </summary>
public class BossAIDecider : MonoBehaviour
{
    private BossController boss;

    // ==========================================================
    //  Inspector：按语义分列表，仅为调试方便
    // ==========================================================

    [Header("--- 一阶段招式池 ---")]
    [SerializeField] private List<BulletNode> phase1Bullets = new List<BulletNode>();
    [SerializeField] private List<MeleeNode> phase1Melees = new List<MeleeNode>();
    [SerializeField] private List<TeleportNode> phase1Teleports = new List<TeleportNode>();

    [Tooltip("远程系特殊技能（激光等）。放任何 ActionNode 派生资产即可。")]
    [SerializeField] private List<ActionNode> phase1SpecialBullets = new List<ActionNode>();

    [Tooltip("近战系特殊技能（突进斩等）。放任何 ActionNode 派生资产即可。")]
    [SerializeField] private List<ActionNode> phase1SpecialMelees = new List<ActionNode>();

    [Header("--- 二阶段招式池 ---")]
    [SerializeField] private List<BulletNode> phase2Bullets = new List<BulletNode>();
    [SerializeField] private List<MeleeNode> phase2Melees = new List<MeleeNode>();
    [SerializeField] private List<TeleportNode> phase2Teleports = new List<TeleportNode>();
    [SerializeField] private List<ActionNode> phase2SpecialBullets = new List<ActionNode>();
    [SerializeField] private List<ActionNode> phase2SpecialMelees = new List<ActionNode>();

    [Header("--- 紧急传送（被逼死角时的保命底牌）---")]
    [Tooltip("被逼入墙角时从这里随机抽一张。留空则不做死角特判。")]
    [SerializeField] private List<TeleportNode> emergencyTeleports = new List<TeleportNode>();

    [Header("--- 意图预约参数 ---")]
    [Tooltip("抽到够不着的牌时，最多允许连续几轮为它靠位。超过则放弃这张牌，" +
             "避免玩家站在够不到的地方时 Boss 原地罚站。")]
    [SerializeField] private int maxPendingAttempts = 3;

    [Tooltip("卡池全空或全在冷却时的兜底牌。强烈建议配一张，否则会出现状态抖动。")]
    [SerializeField] private ActionNode fallbackNode;

    // ==========================================================
    //  运行时数据
    // ==========================================================

    private readonly List<ActionNode> currentPool = new List<ActionNode>();
    private readonly List<ActionNode> validBuffer = new List<ActionNode>(16);
    private readonly Dictionary<ActionNode, float> nextAvailableTime
        = new Dictionary<ActionNode, float>();

    /// <summary>当前预约的意图：想用但暂时够不着的牌。MoveState 会朝它的射程靠位。</summary>
    public ActionNode PendingNode { get; private set; }
    private int pendingAttempts;

    public bool canAttack => boss != null && boss.DistanceToPlayer <= GetMaxAggroRange();

    void Awake()
    {
        boss = GetComponent<BossController>();
        RebuildPool(false);
    }

    public void SwitchToPhase2()
    {
        Debug.Log("[BossAIDecider] 切换到二阶段招式池！");
        RebuildPool(true);
        ClearPending();
        nextAvailableTime.Clear(); // 转阶段重置所有冷却
    }

    private void RebuildPool(bool phase2)
    {
        currentPool.Clear();

        if (phase2)
        {
            AddRange(phase2Bullets);
            AddRange(phase2Melees);
            AddRange(phase2Teleports);
            AddRange(phase2SpecialBullets);
            AddRange(phase2SpecialMelees);
        }
        else
        {
            AddRange(phase1Bullets);
            AddRange(phase1Melees);
            AddRange(phase1Teleports);
            AddRange(phase1SpecialBullets);
            AddRange(phase1SpecialMelees);
        }

        if (currentPool.Count == 0)
            Debug.LogWarning($"[BossAIDecider] {(phase2 ? "二" : "一")}阶段招式池为空！", this);
    }

    private void AddRange<T>(List<T> src) where T : ActionNode
    {
        if (src == null) return;
        for (int i = 0; i < src.Count; i++)
        {
            if (src[i] != null) currentPool.Add(src[i]);
        }
    }

    // ==========================================================
    //  距离建议：供 MoveState 使用
    // ==========================================================

    private float GetMaxAggroRange()
    {
        if (currentPool.Count == 0) return 5f;

        float maxRange = 0f;
        for (int i = 0; i < currentPool.Count; i++)
        {
            if (currentPool[i].maxCastDistance > maxRange) maxRange = currentPool[i].maxCastDistance;
        }
        return maxRange;
    }

    /// <summary>
    /// 推算当前应该保持的接敌距离，供 MoveState 使用。
    /// 优先返回「预约意图」的理想距离 —— 这是让 Boss 能主动冲上去近战的关键。
    /// </summary>
    public float GetOptimalEngagementDistance()
    {
        if (PendingNode != null) return MidRange(PendingNode);

        ActionNode best = null;
        int maxWeight = -1;

        for (int i = 0; i < currentPool.Count; i++)
        {
            ActionNode node = currentPool[i];
            if (node.baseWeight > maxWeight)
            {
                maxWeight = node.baseWeight;
                best = node;
            }
        }

        return best != null ? MidRange(best) : 5f;
    }

    private float MidRange(ActionNode node)
        => (node.minCastDistance + node.maxCastDistance) * 0.5f;

    // ==========================================================
    //  抽卡
    // ==========================================================

    public ActionNode SelectSkill()
    {
        float dist = boss.DistanceToPlayer;

        // ---- 第一层：强制打断（死角逃生）----
        ActionNode emergency = TrySelectEmergency();
        if (emergency != null)
        {
            ClearPending();
            return Commit(emergency);
        }

        // ---- 第二层：兑现预约 ----
        if (PendingNode != null)
        {
            if (IsInRange(PendingNode, dist) && IsOffCooldown(PendingNode))
            {
                ActionNode node = PendingNode;
                ClearPending();
                return Commit(node);
            }

            pendingAttempts++;
            if (pendingAttempts > maxPendingAttempts)
            {
                Debug.Log($"[AI] 预约 {PendingNode.actionName} 靠位失败 {pendingAttempts} 次，放弃。");
                ClearPending();
            }
            else
            {
                return null; // 回 MoveState 继续靠位
            }
        }

        // ---- 第三层：常规加权抽卡 ----
        ActionNode picked = DrawFromPool(node => IsInRange(node, dist) && IsOffCooldown(node));
        if (picked != null) return Commit(picked);

        // ---- 第四层：够不着就预约 ----
        ActionNode wanted = DrawFromPool(node => IsOffCooldown(node));
        if (wanted != null)
        {
            PendingNode = wanted;
            pendingAttempts = 0;
            Debug.Log($"[AI] 想用 {wanted.actionName} 但距离不合适（当前 {dist:F1}，" +
                      $"需要 {wanted.minCastDistance}~{wanted.maxCastDistance}），先去靠位。");
            return null;
        }

        // ---- 第五层：兜底 ----
        if (fallbackNode != null) return Commit(fallbackNode);

        return null;
    }

    private ActionNode DrawFromPool(System.Func<ActionNode, bool> filter)
    {
        validBuffer.Clear();
        int totalWeight = 0;

        for (int i = 0; i < currentPool.Count; i++)
        {
            ActionNode node = currentPool[i];
            if (node.baseWeight <= 0) continue;
            if (!filter(node)) continue;

            validBuffer.Add(node);
            totalWeight += node.baseWeight;
        }

        if (totalWeight <= 0) return null;

        int roll = Random.Range(1, totalWeight + 1);
        int acc = 0;

        for (int i = 0; i < validBuffer.Count; i++)
        {
            acc += validBuffer[i].baseWeight;
            if (roll <= acc) return validBuffer[i];
        }

        return null;
    }

    private ActionNode TrySelectEmergency()
    {
        BossMechanic mech = boss.bossState.bossMechanic;

        if (!mech.isCornered) return null;
        if (mech.currentTeleportTimer > 0) return null;
        if (emergencyTeleports == null || emergencyTeleports.Count == 0) return null;

        validBuffer.Clear();
        for (int i = 0; i < emergencyTeleports.Count; i++)
        {
            if (emergencyTeleports[i] != null) validBuffer.Add(emergencyTeleports[i]);
        }
        if (validBuffer.Count == 0) return null;

        mech.currentTeleportTimer = mech.teleportCooldown;

        ActionNode pick = validBuffer[Random.Range(0, validBuffer.Count)];
        Debug.Log($"[AI] 被逼入死角！强行切牌出老千：{pick.actionName}");
        return pick;
    }

    // ==========================================================
    //  辅助
    // ==========================================================

    private bool IsInRange(ActionNode node, float dist)
        => dist >= node.minCastDistance && dist <= node.maxCastDistance;

    private bool IsOffCooldown(ActionNode node)
    {
        if (node.cooldown <= 0f) return true;
        return !nextAvailableTime.TryGetValue(node, out float readyAt) || Time.time >= readyAt;
    }

    private ActionNode Commit(ActionNode node)
    {
        if (node.cooldown > 0f) nextAvailableTime[node] = Time.time + node.cooldown;
        Debug.Log($"[AI] 出牌: {node.actionName}（{node.GetType().Name}）");
        return node;
    }

    private void ClearPending()
    {
        PendingNode = null;
        pendingAttempts = 0;
    }
}
