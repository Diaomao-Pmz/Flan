using UnityEngine;

/// <summary>
/// Boss 移动状态 ——【先想好下一招，再按这一招去站位】。
///
/// ==========================================================
/// 比喻：以前是"先散步，散完再决定打什么"；
///       现在是"先决定打什么，再为这一招找位置"。
///
/// 流程：
///   进入 → 让 AI 定意图（下一张卡 / 下一个套路）
///   每帧 → 朝意图卡的理想距离走
///         → 满足出手条件 → 切 CombatState 出招
///         → 靠位太久（超时）→ 让 AI 放弃这张卡、换一张
///
/// 出手条件（节奏参数在 BossAIDecider 上，远程 / 近战姿态各一套）：
///   · 勾了「进射程立即出手」：意图一进射程就出
///   · 没勾：至少移动完换位时间，且在射程内才出
///
/// 【位移卡池】每个移动阶段开始时问一次 AI"要不要闪过去 / 冲过去"（TooFar / TooClose / Random），
/// 移动途中撞墙再问一次（Cornered）。每个移动阶段最多位移一次（传送和冲刺合起来算）。
/// 传送前摇期间 Boss 原地站住；冲刺期间由冲刺器控制身体。意图保持不变，结束后照常判断出手。
///
/// 【弹幕脱手后】出招状态把「脱手后的走位」交过来（ArmAfterRelease），直到出下一招为止：
///   · 脱手时间内不出手，只走位
///   · 允许冲刺 → 位移卡池只看冲刺卡；不允许 → 不问位移卡池，只走路
/// 【等弹幕】意图够得着、只是被「弹幕在发时」的规则拦住时，照常走位，不算靠位超时。
/// 【近身反应】弹幕层里有「允许近身反应、跟随 Boss」的弹幕时，玩家贴近就先去出反应招，
///   打完回到本状态接着原来的流程（意图、套路进度、脱手后的走位限制都保留）。
/// ==========================================================
/// </summary>
public class BossMoveState : IState
{
    private BossController boss;
    private Rigidbody2D rb;

    private float elapsed;          // 本次移动已经过了多久
    private float repositionTime;   // 本次至少要移动多久（不勾立即出手时生效）
    private string shownIntent;     // 上次显示在调试文字上的意图，变了才刷新
    private bool teleportedThisPhase; // 本移动阶段已经位移过（传送或冲刺，最多一次）
    private bool corneredAsked;       // 本移动阶段撞墙时已经问过位移卡池（只掷一次骰，避免每帧重掷）
    private string teleportLabel;     // 正在进行的位移（传送 / 冲刺），供移动意图文字显示
    private float approachElapsed;    // 靠位超时计时：脱手时间内、等弹幕时不走

    // ---- 弹幕脱手后的走位（到出下一招为止）----
    private bool armedRelease;        // 出招状态刚交过来、还没进入本状态
    private float armedHold;
    private bool armedDash;
    private bool afterRelease;        // 这一段走位是脱手后的走位
    private bool releaseAllowDash;    // 这段走位允许冲刺（只看冲刺卡）；否则不问位移卡池
    private float holdUntil = -1f;    // 脱手时间到这一刻为止：之前不出手
    private bool suspendedForReaction; // 离开本状态是去出走位中的反应招：回来时保留上面的走位限制

    public BossMoveState(BossController bc)
    {
        boss = bc;
        rb = boss.GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// 出招状态在弹幕脱手后、切回本状态之前调用：交过来这段走位的限制
    /// </summary>
    /// <param name="holdTime">脱手时间：从现在起这么多秒内不出手，只走位</param>
    /// <param name="allowDash">允许冲刺：位移卡池只看冲刺卡；false = 不问位移卡池，只走路</param>
    public void ArmAfterRelease(float holdTime, bool allowDash)
    {
        armedRelease = true;
        armedHold = Mathf.Max(0f, holdTime);
        armedDash = allowDash;
    }

    public void Enter()
    {
        boss.bossState.bossMechanic.isCornered = false;

        if (armedRelease)
        {
            afterRelease = true;
            releaseAllowDash = armedDash;
            holdUntil = armedHold > 0f ? boss.ActionTime + armedHold : -1f; // 脱手后的停留按 Boss 时钟（跟动作倍率走）
        }
        else if (!suspendedForReaction)
        {
            afterRelease = false;
            holdUntil = -1f;
        }
        // 走位中的反应招打完回来：保留脱手后的走位限制（脱手时间照常倒数）
        armedRelease = false;
        suspendedForReaction = false;

        BeginPhase();

        boss.AI.PlanIntent();
        RefreshStatusText();
        TryMovementTeleport(false);
    }

    public void Update()
    {
        // 意图在移动途中被作废（姿态切换、受击传送打断、超时……）→ 重新定一个，重新计时。
        // 定不出来（例如套路后续间隔期间又没有单招可出）就先按默认距离走着，
        // 不重开移动阶段 —— 否则每一帧都会重新问一次位移卡池
        if (!boss.AI.HasIntent)
        {
            boss.AI.PlanIntent();
            if (boss.AI.HasIntent)
            {
                BeginPhase();
                TryMovementTeleport(false);
            }
        }
        RefreshStatusText();

        // 位移传送前摇中：原地站住，等它瞬移完再说
        if (boss.Teleporter != null && boss.Teleporter.IsMovementTeleporting)
        {
            if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            boss.SetMovementStatus($"[移动] 传送：{teleportLabel}");
            return;
        }

        // 位移冲刺中：身体归冲刺器管，这里不碰速度
        if (boss.Dasher != null && boss.Dasher.IsMovementDashing)
        {
            boss.SetMovementStatus($"[移动] 冲刺：{teleportLabel}");
            return;
        }

        // ---- 近身反应：弹幕层里有允许近身反应、跟随 Boss 的弹幕，玩家贴近就先出反应招 ----
        if (boss.BulletEmitter != null && boss.BulletEmitter.HasReactiveBarrage)
        {
            ActionNode reaction = boss.AI.TryPickReaction();
            if (reaction != null)
            {
                suspendedForReaction = true;
                boss.AI.QueueReaction(reaction);
                boss.ChangeState(boss.CombatState);
                return;
            }
        }

        elapsed += Time.deltaTime;

        BossAIDecider.CombatTempo tempo = boss.AI.CurrentTempo;
        bool holdingAfterRelease = IsHoldingAfterRelease;
        bool waitingBarrage = boss.AI.IsWaitingForBarrage;

        // 脱手时间内、等弹幕发完时，不算靠位超时
        if (!holdingAfterRelease && !waitingBarrage) approachElapsed += Time.deltaTime;

        // ---- 能不能出手（脱手时间内不出手）----
        if (holdingAfterRelease)
        {
            // 只走位
        }
        else if (boss.AI.IsIntentReady)
        {
            bool waitedEnough = elapsed >= repositionTime;
            if (tempo.fireImmediatelyInRange || waitedEnough)
            {
                boss.ChangeState(boss.CombatState);
                return;
            }
        }
        else if (boss.AI.HasIntent && !waitingBarrage && approachElapsed >= tempo.maxApproachTime)
        {
            // ---- 靠位超时：换一张卡，重新计时 ----
            boss.AI.OnApproachTimeout();
            BeginPhase();
            TryMovementTeleport(false);
            return;
        }

        MoveTowardIntent();
    }

    private void MoveTowardIntent()
    {
        Vector2 playerPos = boss.PlayerTransform.position;
        Vector2 bossPos = boss.transform.position;
        float distance = Vector2.Distance(playerPos, bossPos);
        float target = boss.AI.GetOptimalEngagementDistance();

        // 离理想距离 0.5 格以内算到位
        float dirX = boss.DriveTowardDistance(target - 0.5f, target + 0.5f, boss.AI.CurrentMoveSpeed);

        // ---- 移动意图文字 ----
        bool approaching = (playerPos.x > bossPos.x) == (dirX > 0f);
        string prefix = StatusPrefix();
        if (dirX == 0f)
            boss.SetMovementStatus($"[移动] {prefix}到位，等待出手");
        else
            boss.SetMovementStatus($"[移动] {prefix}{(approaching ? "靠近" : "后退")}  {distance:F1} → {target:F1}");

        // 只有【后退（远离玩家）】时才判定撞墙。
        // 靠近玩家时射线可能越过贴墙的玩家打到他身后的墙 —— 那是在追人，不是被逼到墙角。
        if (dirX != 0 && !approaching)
        {
            RaycastHit2D hit = Physics2D.Raycast(bossPos, new Vector2(dirX, 0), boss.wallCheckDistance, boss.wallLayer);
            boss.bossState.bossMechanic.isCornered = (hit.collider != null);

            // 撞墙：问一次位移卡池的 Cornered 条目
            if (hit.collider != null && !corneredAsked)
            {
                corneredAsked = true;
                TryMovementTeleport(true);
            }
        }
        else
        {
            boss.bossState.bossMechanic.isCornered = false;
        }
    }

    /// <summary>脱手时间还没到（这段时间只走位、不出手）</summary>
    private bool IsHoldingAfterRelease => holdUntil >= 0f && boss.ActionTime < holdUntil;

    /// <summary>移动意图文字的前缀：脱手时间 / 等弹幕</summary>
    private string StatusPrefix()
    {
        if (IsHoldingAfterRelease) return $"脱手 {holdUntil - boss.ActionTime:F1}s · ";
        if (boss.AI.IsWaitingForBarrage) return "等弹幕 · ";
        return afterRelease ? "脱手后 · " : "";
    }

    /// <summary>新的移动阶段：重置计时、允许再传送一次</summary>
    private void BeginPhase()
    {
        elapsed = 0f;
        approachElapsed = 0f;
        repositionTime = boss.AI.CurrentTempo.RollRepositionTime();
        teleportedThisPhase = false;
        corneredAsked = false;
    }

    /// <summary>问位移卡池要不要闪过去 / 冲过去；要的话交给传送器或冲刺器，并记冷却</summary>
    private void TryMovementTeleport(bool cornered)
    {
        if (teleportedThisPhase || !boss.AI.HasIntent) return;
        if (boss.Teleporter != null && boss.Teleporter.IsMovementTeleporting) return;
        if (boss.Dasher != null && boss.Dasher.IsMovementDashing) return;

        // 弹幕脱手后的走位：不允许冲刺就只走路；允许就只看冲刺卡
        if (afterRelease && !releaseAllowDash) return;

        ActionNode node = boss.AI.PickMovement(cornered, dashOnly: afterRelease);
        if (node == null) return;

        float intentDistance = boss.AI.GetOptimalEngagementDistance();
        bool started = false;
        string verb = "传送";

        if (node is TeleportNode tp && boss.Teleporter != null)
        {
            started = boss.Teleporter.TryMovementTeleport(tp, intentDistance);
        }
        else if (node is DashMoveNode dash)
        {
            verb = "冲刺";
            if (boss.Dasher != null)
                started = boss.Dasher.TryMovementDash(dash, intentDistance);
            else
                Debug.LogWarning($"[BossMoveState] 位移卡池抽到冲刺卡 {dash.name}，但 Boss 身上没挂 BAE_Dasher", boss);
        }

        if (started)
        {
            boss.AI.StampCooldown(node);
            teleportedThisPhase = true;
            teleportLabel = boss.AI.LastMovementPickLabel ?? node.actionName;
            boss.SetMovementStatus($"[移动] {verb}：{teleportLabel}");
            if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    private void RefreshStatusText()
    {
        if (boss.bossStatusText == null) return;

        string intent = boss.AI.IntentName;
        if (intent == shownIntent) return;

        shownIntent = intent;
        boss.bossStatusText.SetText($"[意图] {intent}");
    }

    public void FixedUpdate()
    {

    }

    public void Exit()
    {
        // 前摇还没走完 / 冲刺还没冲完就离开移动状态（被打晕、受击打断……）：这次位移作废
        if (boss.Teleporter != null) boss.Teleporter.CancelMovementTeleport();
        if (boss.Dasher != null) boss.Dasher.CancelMovementDash();

        shownIntent = null;
        if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        // 离开本状态 = 出下一招了（或被打晕 / 击退）：脱手后的走位限制到此结束。
        // 去出走位中的反应招除外：打完回来接着用
        if (!suspendedForReaction)
        {
            afterRelease = false;
            holdUntil = -1f;
        }
        armedRelease = false;
    }
}
