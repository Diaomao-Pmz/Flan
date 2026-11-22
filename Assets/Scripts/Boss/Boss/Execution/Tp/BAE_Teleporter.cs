using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 传送执行器。两份工作：
///   1. 主动传送：认领 TeleportNode，由 AI 抽卡 / 套路调用 Execute
///   2. 受击传送：Boss 挨打时（BossController 转交 OnBossHit），
///      按 Hit Reactions 列表里每张卡的概率、冷却、姿态决定要不要传走
///
/// 【比喻：忍耐槽】每张受击卡有一个忍耐槽（当前概率）。挨一刀没跑 → 槽涨一截；
/// 跑了 → 槽回到基础值，并进入冷却；一段时间没人打 → 槽自己泄掉。
/// </summary>
public class BAE_Teleporter : MonoBehaviour, IBossActionExecutor
{
    [Header("--- 随机点传送 (RandomPoint) ---")]
    [Tooltip("一阶段专用传送点。与二阶段互不共用。")]
    [SerializeField] private List<Transform> phase1Points = new List<Transform>();

    [Tooltip("二阶段专用传送点。与一阶段互不共用。")]
    [SerializeField] private List<Transform> phase2Points = new List<Transform>();

    [Tooltip("小于此距离视为「就在原地」，会被排除，避免传了个寂寞。")]
    [SerializeField] private float samePointThreshold = 0.1f;

    [Header("--- 绕背传送 (BehindPlayer) ---")]
    [Tooltip("玩家 Transform。留空则由 BossController.Init() 自动注入。")]
    [SerializeField] private Transform playerTransform;

    [Tooltip("落点与玩家的水平距离。Boss 会出现在玩家背后这么远的地方。")]
    [SerializeField] private float behindPlayerOffset = 3f;

    [Tooltip("勾选后绕背传送会同时对齐玩家的 Y 坐标；不勾则保持 Boss 当前高度。")]
    [SerializeField] private bool matchPlayerHeight = false;

    [Tooltip("勾选 = 在【发动传送那一刻】记下玩家位置，前摇结束后按这个位置算落点。\n" +
             "不勾 = 前摇结束时才读玩家位置（默认）。\n" +
             "两种模式都会经过下面的落点安检")]
    [SerializeField] private bool snapshotPlayerPosition = false;

    [Header("--- 落点安检（绕背传送用）---")]
    [Tooltip("哪些图层算「不能落」：勾地面、墙。\n" +
             "落点（按 Boss 身体碰撞框大小）压到这些图层就换另一侧；两侧都不行则这次不传。\n" +
             "选 Nothing = 不检查地形（不推荐）")]
    [SerializeField] private LayerMask landingBlockLayers;

    [Tooltip("落点不能和玩家身体重叠")]
    [SerializeField] private bool avoidPlayerOverlap = true;

    [Tooltip("安检时把 Boss 碰撞框缩小这么多（每边，世界单位），\n" +
             "避免 Boss 本来就踩在地上时，脚底和地面「刚好接触」被误判为压到地面")]
    [SerializeField] private float landingCheckInset = 0.05f;

    [Tooltip("勾选 = 落点脚下一定距离内必须有地面（Landing Block Layers 里的物体），否则不能落。\n" +
             "防止传送到地图外的半空。以后 Boss 要传送到空中时可以关掉")]
    [SerializeField] private bool requireGroundBelow = true;

    [Tooltip("从落点身体底部往下找地面的最大距离（格）")]
    [Min(0.1f)]
    [SerializeField] private float groundCheckDistance = 3f;

    [Tooltip("打印绕背传送的详细日志（起点、落点、传送后实际位置），排查卡地底问题用。稳定后可关")]
    [SerializeField] private bool debugTeleport = true;

    [Header("--- 中央传送 (Center) ---")]
    [Tooltip("场地中央锚点。二阶段转场等场合使用。")]
    [SerializeField] private Transform centerPoint;

    [Header("--- 受击传送 (Hit Reactions) ---")]
    [Tooltip("挨打时会检查的传送卡（需勾选卡上的 React To Hit）。\n" +
             "从上往下检查，同一次挨打最多触发一张。\n" +
             "列表留空 = Boss 挨打永远不会被动传送")]
    [SerializeField] private List<TeleportNode> hitReactions = new List<TeleportNode>();

    // 黑板引用，用于读取当前阶段。缓存以避免每次传送都 GetComponent。
    private BossState bossState;
    private BossController controller;

    /// <summary>每张受击卡的运行时数据：当前概率（忍耐槽）、上次挨打时间</summary>
    private class ReactRuntime
    {
        public float chance;
        public float lastHitTime = float.NegativeInfinity;
    }

    private readonly Dictionary<TeleportNode, ReactRuntime> reactStates
        = new Dictionary<TeleportNode, ReactRuntime>();

    // 正在等前摇的受击传送（同一时间只允许一个）
    private Coroutine reactRoutine;

    // 正在等前摇的位移传送（移动阶段用，同一时间只允许一个）
    private Coroutine moveRoutine;

    /// <summary>位移传送正在前摇中（MoveState 据此原地等待）</summary>
    public bool IsMovementTeleporting => moveRoutine != null;

    // 落点安检用的身体碰撞框（Boss 自己 / 玩家），Init 时缓存
    private Rigidbody2D bossRb;
    private Collider2D bossBody;
    private Rigidbody2D playerRb;

    // Gizmos：最近一次绕背传送检查过的落点（绿 = 可落，红 = 被挡）
    private readonly List<Vector2> gizmoCandidates = new List<Vector2>(2);
    private readonly List<bool> gizmoCandidateOk = new List<bool>(2);
    private Vector2 gizmoBodySize;

    // ==========================================================
    //  IBossActionExecutor 实现：认领 TeleportNode
    public System.Type NodeType => typeof(TeleportNode);

    public IEnumerator Execute(ActionNode node, BossContext ctx)
    {
        TeleportNode tpNode = node as TeleportNode;
        if (tpNode == null) yield break;

        // 发动那一刻的玩家位置（只在勾了 snapshotPlayerPosition 时使用）
        Vector2? snapshot = TakePlayerSnapshot();

        // 传送前摇。不想要的话把资产里的 teleportDelay 调成 0。
        if (tpNode.teleportDelay > 0f)
        {
            yield return new WaitForSeconds(tpNode.teleportDelay);
        }

        ExecuteTeleport(tpNode.targetType, snapshot);
    }

    /// <summary>传送本身是瞬时的；需要收摊的是还在等前摇的受击传送 / 位移传送。</summary>
    public void Cancel()
    {
        if (reactRoutine != null)
        {
            StopCoroutine(reactRoutine);
            reactRoutine = null;
        }
        CancelMovementTeleport();
    }

    // ==========================================================
    //  位移传送（位移卡池，由 MoveState 调用）
    // ==========================================================

    /// <summary>
    /// 移动阶段发动一次位移传送：等完卡上的前摇再瞬移。
    /// </summary>
    /// <param name="intentDistance">意图的理想距离（Intent Distance 落点用）</param>
    /// <returns>false = 没发动（例如 Intent Distance 两侧都落不下），调用方应继续走路</returns>
    public bool TryMovementTeleport(TeleportNode node, float intentDistance)
    {
        if (node == null || moveRoutine != null) return false;

        // Intent Distance 先预检一次落点：现在就落不下的话别白等前摇
        if (node.targetType == TeleportTargetType.IntentDistance &&
            !TryPickIntentLanding(PlayerPos(null), intentDistance, out _))
        {
            if (debugTeleport)
                Debug.Log($"[BAE_Teleporter] 位移传送 {node.actionName} 取消：意图距离 {intentDistance:F1} 两侧都落不下", this);
            return false;
        }

        moveRoutine = StartCoroutine(MovementTeleportRoutine(node, intentDistance, TakePlayerSnapshot()));
        return true;
    }

    /// <summary>移动阶段结束（被打断、切去出招）时调用</summary>
    public void CancelMovementTeleport()
    {
        if (moveRoutine == null) return;
        StopCoroutine(moveRoutine);
        moveRoutine = null;
    }

    private IEnumerator MovementTeleportRoutine(TeleportNode node, float intentDistance, Vector2? snapshot)
    {
        if (node.teleportDelay > 0f) yield return new WaitForSeconds(node.teleportDelay);
        moveRoutine = null;

        if (bossState != null && bossState.health.isDead) yield break;
        if (controller != null && controller.IsStunned) yield break;

        ExecuteTeleport(node.targetType, snapshot, intentDistance);
    }

    //---------------------------------------------------

    public void Init(Transform player)
    {
        // Inspector 里已经拖了就以 Inspector 为准，否则接受外部注入
        if (playerTransform == null) playerTransform = player;
        bossState = GetComponent<BossState>();
        controller = GetComponent<BossController>();
        reactStates.Clear();

        bossRb = GetComponent<Rigidbody2D>();
        bossBody = FindSolidCollider(gameObject);
        playerRb = playerTransform != null ? playerTransform.GetComponentInParent<Rigidbody2D>() : null;

        if (bossBody == null)
            Debug.LogWarning("[BAE_Teleporter] Boss 身上找不到非触发器的 Collider2D，绕背落点安检不可用。", this);
    }

    // ==========================================================
    //  受击传送
    // ==========================================================

    /// <summary>
    /// Boss 挨了一下（由 BossController 转交）。
    /// </summary>
    /// <param name="type">这一下的伤害类型</param>
    /// <param name="modeAtHit">挨打那一刻的作战姿态（刚好打碎护盾的那一刀仍算 Ranged）</param>
    public void OnBossHit(DamageType type, BossCombatMode modeAtHit)
    {
        if (hitReactions == null || hitReactions.Count == 0) return;
        if (controller == null) controller = GetComponent<BossController>();
        if (bossState == null) bossState = GetComponent<BossState>();

        if (bossState != null && bossState.health.isDead) return;
        if (controller != null && controller.IsStunned) return; // 眩晕中一律不传
        if (reactRoutine != null) return;                         // 上一次受击传送还在前摇

        BossAIDecider ai = controller != null ? controller.AI : null;

        for (int i = 0; i < hitReactions.Count; i++)
        {
            TeleportNode node = hitReactions[i];
            if (node == null || !node.reactToHit) continue;
            if (!ModeMatches(node.reactMode, modeAtHit)) continue;
            if (!DamageTypeMatches(node.reactDamageType, type)) continue;

            ReactRuntime rt = GetRuntime(node);

            // 太久没挨打 → 忍耐槽泄掉，回到基础概率
            if (node.chanceResetAfter > 0f && Time.time - rt.lastHitTime > node.chanceResetAfter)
                rt.chance = node.baseChance;
            rt.lastHitTime = Time.time;

            // 冷却中（不论是 AI 主动用过还是刚被动触发过）：不掷骰、不累加
            if (ai != null && !ai.IsOffCooldown(node)) continue;

            if (Random.value >= rt.chance)
            {
                rt.chance = Mathf.Min(1f, rt.chance + node.chanceGainPerHit);
                continue;
            }

            // ---- 触发 ----
            rt.chance = node.baseChance;
            if (ai != null) ai.StampCooldown(node);

            Debug.Log($"[BAE_Teleporter] 受击传送触发：{node.actionName}");

            // 先打断、后开传送协程 —— 打断会调用执行器的 Cancel，顺序反了会把刚开的协程掐掉
            if (node.interruptAction && controller != null)
                controller.InterruptCurrentAction($"受击传送 {node.actionName}");

            if (node.teleportDelay > 0f)
                reactRoutine = StartCoroutine(ReactAfterDelay(node, TakePlayerSnapshot()));
            else
                ExecuteTeleport(node.targetType);

            return; // 同一次挨打最多触发一张
        }
    }

    private IEnumerator ReactAfterDelay(TeleportNode node, Vector2? snapshot)
    {
        yield return new WaitForSeconds(node.teleportDelay);
        reactRoutine = null;

        // 前摇期间被打晕 / 被打死：不传了
        if (bossState != null && bossState.health.isDead) yield break;
        if (controller != null && controller.IsStunned) yield break;

        ExecuteTeleport(node.targetType, snapshot);
    }

    private ReactRuntime GetRuntime(TeleportNode node)
    {
        if (!reactStates.TryGetValue(node, out ReactRuntime rt))
        {
            rt = new ReactRuntime { chance = node.baseChance };
            reactStates[node] = rt;
        }
        return rt;
    }

    private static bool ModeMatches(RoutineMode filter, BossCombatMode mode)
    {
        switch (filter)
        {
            case RoutineMode.Ranged: return mode == BossCombatMode.Ranged;
            case RoutineMode.Melee:  return mode == BossCombatMode.Melee;
            default:                 return true;
        }
    }

    private static bool DamageTypeMatches(HitReactDamageType filter, DamageType type)
    {
        switch (filter)
        {
            case HitReactDamageType.Melee:  return type == DamageType.Melee;
            case HitReactDamageType.Ranged: return type == DamageType.Ranged;
            default:                        return true;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (hitReactions == null) return;
        foreach (TeleportNode node in hitReactions)
        {
            if (node != null && !node.reactToHit)
            {
                Debug.LogWarning(
                    $"[BAE_Teleporter] 「{node.name}」在 Hit Reactions 列表里，但卡上没勾 React To Hit，不会触发。", this);
            }
        }
    }
#endif

    // ==========================================================
    //  具体位移逻辑
    /// <param name="playerSnapshot">发动时记下的玩家位置；为 null 则按此刻的玩家位置</param>
    /// <param name="intentDistance">Intent Distance 落点用的距离；为 null 则问 AI 当前意图的理想距离</param>
    public void ExecuteTeleport(TeleportTargetType strategy, Vector2? playerSnapshot = null, float? intentDistance = null)
    {
        switch (strategy)
        {
            case TeleportTargetType.RandomPoint:
                TeleportToRandomPoint();
                break;
            case TeleportTargetType.BehindPlayer:
                TeleportBehindPlayer(playerSnapshot);
                break;
            case TeleportTargetType.Center:
                TeleportToCenter();
                break;
            case TeleportTargetType.IntentDistance:
                TeleportToIntentDistance(playerSnapshot, intentDistance);
                break;
        }
    }

    /// <summary>
    /// 按意图距离落点：落在离玩家正好 distance 的位置（水平方向）。
    /// 优先 Boss 当前所在的一侧，被挡就换另一侧，两侧都不行这次不传。
    /// </summary>
    private void TeleportToIntentDistance(Vector2? playerSnapshot, float? intentDistance)
    {
        if (playerTransform == null)
        {
            Debug.LogWarning("[BAE_Teleporter] 未设置玩家引用，意图距离传送已跳过。", this);
            return;
        }

        float distance = intentDistance
            ?? (controller != null && controller.AI != null ? controller.AI.GetOptimalEngagementDistance() : behindPlayerOffset);

        Vector2 playerPos = PlayerPos(playerSnapshot);
        Vector2 from = transform.position;

        if (!TryPickIntentLanding(playerPos, distance, out Vector2 landing))
        {
            if (debugTeleport)
                Debug.Log($"[BAE_Teleporter] 意图距离传送取消：玩家两侧 {distance:F1} 格处都落不下", this);
            return;
        }

        if (debugTeleport)
            Debug.Log($"[BAE_Teleporter] 意图距离传送：Boss {from} → {landing}（距玩家 {distance:F1}）", this);

        MoveTo(landing);
    }

    /// <summary>在玩家两侧 distance 处找一个能落的点：先 Boss 所在一侧，再另一侧</summary>
    private bool TryPickIntentLanding(Vector2 playerPos, float distance, out Vector2 landing)
    {
        float side = transform.position.x >= playerPos.x ? 1f : -1f;
        float y = transform.position.y;

        gizmoCandidates.Clear();
        gizmoCandidateOk.Clear();

        Vector2 near = new Vector2(playerPos.x + side * distance, y);
        Vector2 far  = new Vector2(playerPos.x - side * distance, y);

        if (IsLandingClear(near, playerPos)) { landing = near; return true; }
        if (IsLandingClear(far, playerPos))  { landing = far;  return true; }

        landing = default;
        return false;
    }

    private Vector2 PlayerPos(Vector2? snapshot)
        => snapshot ?? (playerTransform != null ? (Vector2)playerTransform.position : (Vector2)transform.position);

    /// <summary>
    /// 按当前阶段选取对应的传送点列表，随机挑一个不在原地的点。
    /// 一阶段与二阶段的点位互不共用。
    /// </summary>
    private void TeleportToRandomPoint()
    {
        List<Transform> pool = GetCurrentPhasePoints();

        if (pool == null || pool.Count == 0)
        {
            Debug.LogWarning($"[BAE_Teleporter] 当前阶段的传送点列表为空，传送已跳过。", this);
            return;
        }

        // 先排除「就在原地」和空槽位，再从剩下的里面随机
        List<Transform> candidates = new List<Transform>(pool.Count);
        for (int i = 0; i < pool.Count; i++)
        {
            Transform p = pool[i];
            if (p == null) continue;
            if (Vector2.Distance(transform.position, p.position) < samePointThreshold) continue;
            candidates.Add(p);
        }

        // 全都被排除了（例如列表里只配了一个点，而 Boss 正好站在上面）
        if (candidates.Count == 0)
        {
            Debug.LogWarning("[BAE_Teleporter] 没有可用的落点（可能只配了一个点且 Boss 就在其上）。", this);
            return;
        }

        Transform target = candidates[Random.Range(0, candidates.Count)];
        MoveTo(target.position);
    }

    /// <summary>
    /// 绕背传送：优先落在玩家「远离 Boss 的那一侧」；那里被地形或玩家挡住就换另一侧；
    /// 两侧都不行则这次不传（Boss 留在原地）。
    /// </summary>
    private void TeleportBehindPlayer(Vector2? playerSnapshot)
    {
        if (playerTransform == null)
        {
            Debug.LogWarning("[BAE_Teleporter] 未设置玩家引用，绕背传送已跳过。", this);
            return;
        }

        Vector2 playerPos = playerSnapshot ?? (Vector2)playerTransform.position;
        Vector2 from = transform.position;

        // 玩家在 Boss 右边 → 优先传到玩家更右边；反之更左边
        float sign = playerPos.x > from.x ? 1f : -1f;
        float targetY = matchPlayerHeight ? playerPos.y : from.y;

        Vector2 behind = new Vector2(playerPos.x + sign * behindPlayerOffset, targetY);
        Vector2 front  = new Vector2(playerPos.x - sign * behindPlayerOffset, targetY);

        gizmoCandidates.Clear();
        gizmoCandidateOk.Clear();

        Vector2? landing = null;
        if (IsLandingClear(behind, playerPos)) landing = behind;
        else if (IsLandingClear(front, playerPos)) landing = front;

        if (landing == null)
        {
            if (debugTeleport)
                Debug.Log($"[BAE_Teleporter] 绕背传送取消：玩家两侧都落不下（玩家 {playerPos}，Boss {from}）", this);
            return;
        }

        if (debugTeleport)
        {
            string side = landing.Value == behind ? "玩家背后" : "玩家面前（背后被挡）";
            Debug.Log($"[BAE_Teleporter] 绕背传送：Boss {from} → {landing.Value}（{side}），" +
                      $"玩家 {playerPos}{(playerSnapshot.HasValue ? "（发动时快照）" : "")}", this);
        }

        MoveTo(landing.Value);
    }

    /// <summary>
    /// 落点安检：按 Boss 身体碰撞框的大小，看这个位置会不会压到地面 / 墙 / 玩家。
    /// </summary>
    /// <param name="anchor">参照点（玩家位置）：落点和它之间不能隔着墙</param>
    private bool IsLandingClear(Vector2 pivot, Vector2 anchor)
    {
        bool ok = CheckLanding(pivot, anchor);
        gizmoCandidates.Add(pivot);
        gizmoCandidateOk.Add(ok);
        return ok;
    }

    private bool CheckLanding(Vector2 pivot, Vector2 anchor)
    {
        if (bossBody == null) return true; // 没有身体碰撞框就没法检查，按可落处理

        // 碰撞框中心相对轴心的偏移（Boss 轴心不一定在身体正中）
        Bounds b = bossBody.bounds;
        Vector2 center = pivot + ((Vector2)b.center - (Vector2)transform.position);
        Vector2 size = new Vector2(
            Mathf.Max(0.01f, b.size.x - landingCheckInset * 2f),
            Mathf.Max(0.01f, b.size.y - landingCheckInset * 2f));
        gizmoBodySize = size;

        if (landingBlockLayers.value != 0)
        {
            // 1. 落点本身压到地形
            if (Physics2D.OverlapBox(center, size, 0f, landingBlockLayers) != null)
                return false;

            // 2. 隔墙：在 Boss 身体中心的高度，从玩家画一条线到落点，碰到地形就说明隔着墙
            //    （空气墙很薄，墙外的落点本身是空的，只靠第 1 条会被骗过去）
            Vector2 from = new Vector2(anchor.x, center.y);
            if (Physics2D.Linecast(from, center, landingBlockLayers).collider != null)
                return false;

            // 3. 脚下有地：从身体底部往下找，找不到地面就是悬空 / 地图外
            if (requireGroundBelow)
            {
                Vector2 feet = new Vector2(center.x, center.y - size.y * 0.5f);
                if (Physics2D.Raycast(feet, Vector2.down, groundCheckDistance, landingBlockLayers).collider == null)
                    return false;
            }
        }

        // 4. 玩家身体（只看玩家刚体上的实体碰撞框，不管受击小框等触发器）
        if (avoidPlayerOverlap && playerRb != null)
        {
            Collider2D[] hits = Physics2D.OverlapBoxAll(center, size, 0f);
            for (int i = 0; i < hits.Length; i++)
            {
                if (!hits[i].isTrigger && hits[i].attachedRigidbody == playerRb) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 真正挪位置。连刚体一起挪，并清掉残余速度 ——
    /// 只改 transform 时，刚体可能在下一个物理帧带着旧速度 / 旧位置与地形做碰撞修正。
    /// </summary>
    private void MoveTo(Vector2 pos)
    {
        Vector3 target = new Vector3(pos.x, pos.y, transform.position.z);

        if (bossRb != null)
        {
            bossRb.position = pos;
            bossRb.linearVelocity = Vector2.zero;
        }
        transform.position = target;

        if (debugTeleport) StartCoroutine(ReportSettledPosition(pos));
    }

    /// <summary>【排查用】传送后等两个物理帧，看 Boss 有没有被物理推离落点</summary>
    private IEnumerator ReportSettledPosition(Vector2 intended)
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        Vector2 actual = transform.position;
        if (Vector2.Distance(actual, intended) > 0.3f)
        {
            Debug.LogWarning(
                $"[BAE_Teleporter] 传送后被物理推走了：目标 {intended} → 实际 {actual}。" +
                "如果是往下沉，说明落点和地形 / 玩家重叠了，请检查 Landing Block Layers 是否勾了地面", this);
        }
    }

    /// <summary>勾了 snapshotPlayerPosition 时返回此刻玩家位置，否则返回 null</summary>
    private Vector2? TakePlayerSnapshot()
    {
        if (!snapshotPlayerPosition || playerTransform == null) return null;
        return playerTransform.position;
    }

    private void TeleportToCenter()
    {
        if (centerPoint == null)
        {
            Debug.LogWarning("[BAE_Teleporter] 未设置中央锚点，中央传送已跳过。", this);
            return;
        }

        MoveTo(centerPoint.position);
    }

    /// <summary>找物体自身上第一个非触发器的 Collider2D（身体碰撞框）</summary>
    private static Collider2D FindSolidCollider(GameObject go)
    {
        foreach (Collider2D c in go.GetComponents<Collider2D>())
        {
            if (c != null && !c.isTrigger) return c;
        }
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        for (int i = 0; i < gizmoCandidates.Count; i++)
        {
            Gizmos.color = gizmoCandidateOk[i] ? Color.green : Color.red;
            Vector2 center = gizmoCandidates[i];
            if (bossBody != null)
                center += (Vector2)bossBody.bounds.center - (Vector2)transform.position;
            Gizmos.DrawWireCube(center, gizmoBodySize);
        }
    }

    /// <summary>读取黑板判断当前阶段，返回对应的点位列表。</summary>
    private List<Transform> GetCurrentPhasePoints()
    {
        if (bossState == null) bossState = GetComponent<BossState>();

        bool isPhase2 = bossState != null && bossState.bossMechanic.isPhase2;
        return isPhase2 ? phase2Points : phase1Points;
    }
}