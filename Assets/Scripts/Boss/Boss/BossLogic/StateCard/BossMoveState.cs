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
/// 【位移卡池】每个移动阶段开始时问一次 AI"要不要闪过去"（TooFar / TooClose / Random），
/// 移动途中撞墙再问一次（Cornered）。每个移动阶段最多传送一次。
/// 传送前摇期间 Boss 原地站住，意图保持不变；落地后照常判断出手。
/// ==========================================================
/// </summary>
public class BossMoveState : IState
{
    private BossController boss;
    private Rigidbody2D rb;

    private float elapsed;          // 本次移动已经过了多久
    private float repositionTime;   // 本次至少要移动多久（不勾立即出手时生效）
    private string shownIntent;     // 上次显示在调试文字上的意图，变了才刷新
    private bool teleportedThisPhase; // 本移动阶段已经传送过（最多一次）
    private bool corneredAsked;       // 本移动阶段撞墙时已经问过位移卡池（只掷一次骰，避免每帧重掷）
    private string teleportLabel;     // 正在前摇的位移传送，供移动意图文字显示

    public BossMoveState(BossController bc)
    {
        boss = bc;
        rb = boss.GetComponent<Rigidbody2D>();
    }

    public void Enter()
    {
        boss.bossState.bossMechanic.isCornered = false;
        BeginPhase();

        boss.AI.PlanIntent();
        RefreshStatusText();
        TryMovementTeleport(false);
    }

    public void Update()
    {
        // 意图在移动途中被作废（姿态切换、受击传送打断、超时……）→ 重新定一个，重新计时
        if (!boss.AI.HasIntent)
        {
            boss.AI.PlanIntent();
            BeginPhase();
            RefreshStatusText();
            TryMovementTeleport(false);
        }
        RefreshStatusText();

        // 位移传送前摇中：原地站住，等它瞬移完再说
        if (boss.Teleporter != null && boss.Teleporter.IsMovementTeleporting)
        {
            if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            boss.SetMovementStatus($"[移动] 传送：{teleportLabel}");
            return;
        }

        elapsed += Time.deltaTime;

        BossAIDecider.CombatTempo tempo = boss.AI.CurrentTempo;

        // ---- 能不能出手 ----
        if (boss.AI.IsIntentReady)
        {
            bool waitedEnough = elapsed >= repositionTime;
            if (tempo.fireImmediatelyInRange || waitedEnough)
            {
                boss.ChangeState(boss.CombatState);
                return;
            }
        }
        else if (elapsed >= tempo.maxApproachTime)
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
        float dirX = 0f;

        if (distance < target - 0.5f)
        {
            dirX = (bossPos.x > playerPos.x) ? 1f : -1f;   // 太近：远离玩家
        }
        else if (distance > target + 0.5f)
        {
            dirX = (playerPos.x > bossPos.x) ? 1f : -1f;   // 太远：靠近玩家
        }

        if (rb != null)
        {
            rb.linearVelocity = new Vector2(dirX * boss.AI.CurrentMoveSpeed, rb.linearVelocity.y);
        }

        // ---- 移动意图文字 ----
        bool approaching = (playerPos.x > bossPos.x) == (dirX > 0f);
        if (dirX == 0f)
            boss.SetMovementStatus("[移动] 到位，等待出手");
        else
            boss.SetMovementStatus($"[移动] {(approaching ? "靠近" : "后退")}  {distance:F1} → {target:F1}");

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

    /// <summary>新的移动阶段：重置计时、允许再传送一次</summary>
    private void BeginPhase()
    {
        elapsed = 0f;
        repositionTime = boss.AI.CurrentTempo.RollRepositionTime();
        teleportedThisPhase = false;
        corneredAsked = false;
    }

    /// <summary>问位移卡池要不要闪过去；要的话交给传送器，并记冷却</summary>
    private void TryMovementTeleport(bool cornered)
    {
        if (teleportedThisPhase || boss.Teleporter == null) return;
        if (boss.Teleporter.IsMovementTeleporting) return;

        TeleportNode node = boss.AI.PickMovementTeleport(cornered);
        if (node == null) return;

        if (boss.Teleporter.TryMovementTeleport(node, boss.AI.GetOptimalEngagementDistance()))
        {
            boss.AI.StampCooldown(node);
            teleportedThisPhase = true;
            teleportLabel = boss.AI.LastMovementPickLabel ?? node.actionName;
            boss.SetMovementStatus($"[移动] 传送：{teleportLabel}");
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
        // 前摇还没走完就离开移动状态（被打晕、受击打断……）：这次位移作废
        if (boss.Teleporter != null) boss.Teleporter.CancelMovementTeleport();

        shownIntent = null;
        if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }
}
