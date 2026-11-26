using System.Collections;
using UnityEngine;

/// <summary>
/// Boss 的战斗状态。
///
/// 【本次改动】它不再认识任何具体技能。
/// 原先这里是一串 if (node is TeleportNode) / if (node is BulletNode) / else 的分支链，
/// 每加一种技能就要回来改一次。现在只做三件事：抽卡 → 查执行器 → 等它演完。
///
/// 命名说明：本类是 IState（战斗状态），真正干活的是各个 IBossActionExecutor。
/// 若日后重构，建议改名 BossCombatState，把 Executor 一词让给执行器接口。
/// </summary>
public class BossActionExecuter : IState
{
    BossController boss;
    Coroutine attackCoroutine;

    // 记录当前正在演出的执行器，被打断时好通知它收摊
    IBossActionExecutor activeExecutor;

    /// <summary>
    /// 正在放的这一招（含出招前的停顿）。没在出招时为 null。
    /// 受击时用它判断「这一下能不能打断 Boss」（见 BossController.TakeDamage）。
    /// </summary>
    public ActionNode CurrentAction { get; private set; }

    // ---- 开火时移动（只对弹幕卡） ----
    private const float KeepDistanceTolerance = 0.2f; // 保持距离：差这么多以内算到位，避免来回抖
    private bool fireMoving;          // 正在放弹幕且这一招要动
    private FireMoveMode fireMode;
    private float fireSpeed;          // 这一招开火时的移速
    private float keepDistance;       // 保持距离：这一招抽到的距离
    private float moveDir;            // 位移后站定：出招时定好的方向（+1 右 / -1 左）
    private float moveRemaining;      // 位移后站定：还要走多少格
    private float lastX;              // 位移后站定：上一个物理帧的位置，算实际走了多少
    private bool holding;             // 位移后站定：已经站住了
    private bool pushedLastStep;      // 位移后站定：上一个物理帧给过速度（用来判断撞墙）
    private Rigidbody2D body;

    // ---- 脱手释放：到点让发射器接手剩下的弹幕，Boss 身体去做下一步 ----
    private float releaseAt = -1f;          // 到点脱手；-1 = 这一招不脱手
    private ReleaseSettings releaseSettings; // 这一招用的脱手参数
    private bool released;                   // 这一招已经脱手

    // 脱手后还没出下一招：这段走位的限制（脱手时间、能不能冲刺），回移动状态时交给它
    private bool afterRelease;
    private float afterReleaseHold;
    private bool afterReleaseDash;

    // ---- 近身反应（站着放弹幕时玩家贴近：弹幕托管给发射器，身体出反应招，打完按设置取回）----
    private bool reactionEnabled;        // 这一招（弹幕）允许近身反应
    private bool reactionReclaim;        // 反应后取回
    private ActionNode pendingReaction;  // 已经决定要出的反应招（等出招协程把弹幕交出去后出）
    private int hostedComboId = -1;      // 托管出去的那张弹幕
    private bool reacting;               // 正在出反应招
    private bool fireMovePaused;         // 反应期间暂停的开火时移动（取回后接着走）
    private bool returnToMoveAfter;      // 这一招是走位中触发的反应招：打完回移动状态，接着原来的流程

    public BossActionExecuter(BossController bc)
    {
        boss = bc;
    }

    public void Enter()
    {
        // 防御：上一次 DoCombat 若没 yield 就直接结束（SelectSkill 返回 null），
        // StartCoroutine 返回后句柄会被赋成一个已结束的协程，导致这里永远以为"还在出招"
        attackCoroutine = null;
        activeExecutor = null;
        CurrentAction = null;

        boss.SetMovementStatus("[移动] 出招中");

        // 每次进入战斗状态时，先把水平速度归零（弹幕卡设了「开火时移动」的，出招时再按设置走）
        Rigidbody2D rb = boss.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    public void Exit()
    {
        // ========================================================
        // 【核心安全保护】：Boss 在出招期间被破盾（进入 StunState）或转阶段时，
        // 强制掐断协程并通知执行器收摊，防止"晕了还在射击/激光还在照"的 Bug。
        // ========================================================
        if (attackCoroutine != null)
        {
            boss.StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        // 【改动】不再写死 BulletEmitter.StopAttack()，改为通知当前执行器。
        // 这样以后加激光、冲撞，打断逻辑不需要再改这里。
        if (activeExecutor != null)
        {
            activeExecutor.Cancel();
            activeExecutor = null;
        }

        // 正常放完时 Finish 已经通知过；走到这里还没通知 = 这一招被打断了
        boss.AI.NotifyActionEnded(completed: false);

        CurrentAction = null;
        releaseAt = -1f;
        releaseSettings = null;
        released = false;
        afterRelease = false; // 被打断：脱手后的走位限制作废（正常回移动状态时 Finish 已经交出去了）
        ResetReaction();
        returnToMoveAfter = false;
        EndFireMove();
        boss.TryConsumeClashStagger(out _); // 被打断的招：拼刀留下的停顿作废，不带到下一招
    }

    public void FixedUpdate()
    {
        if (fireMoving) TickFireMove();
    }

    // ==========================================================
    //  脱手释放
    // ==========================================================

    /// <summary>出这一招之前调用：弹幕卡（或套路步骤）设了脱手，就算好什么时候脱手</summary>
    private void SetupRelease(ActionNode node, IBossActionExecutor executor)
    {
        releaseAt = -1f;
        releaseSettings = null;
        released = false;

        if (!(executor is IBackgroundableExecutor)) return;
        if (!boss.AI.ResolveRelease(node, out ReleaseSettings set)) return;

        // 结束前 X 秒脱手；X 比整张弹幕还长 = 一开火就脱手
        float total = ((BulletNode)node).TotalDuration;
        releaseAt = boss.ActionTime + Mathf.Max(0f, total - Mathf.Max(0f, set.beforeEnd)); // Boss 时钟：跟动作倍率走
        releaseSettings = set;
    }

    /// <summary>到点：让发射器接手剩下的弹幕。出招协程随后结束，Boss 去做下一步</summary>
    private void TryRelease()
    {
        releaseAt = -1f;
        if (releaseSettings == null) return;

        // 允许近身反应的弹幕脱手后，Boss 走位时玩家贴近也会反应（只认跟随 Boss 的，由发射器判断）
        if (reactionEnabled && activeExecutor is BAE_BulletEmitter emitter)
            emitter.SetComboReactive(emitter.ForegroundComboId, true);

        if (activeExecutor is IBackgroundableExecutor bg && bg.ReleaseToBackground())
        {
            released = true;
            Debug.Log($"[Boss] 弹幕脱手：脱手时间 {releaseSettings.holdTime:F1} 秒，" +
                      $"{(releaseSettings.allowDash ? "允许冲刺" : "只走路")}");
        }
    }

    // ==========================================================
    //  近身反应
    // ==========================================================

    /// <summary>出这一招之前调用：弹幕卡（或套路步骤）允许近身反应就开始每帧检查</summary>
    private void SetupReaction(ActionNode node, IBossActionExecutor executor)
    {
        ResetReaction();
        reactionEnabled = executor is BAE_BulletEmitter && boss.AI.ResolveReaction(node, out reactionReclaim);
    }

    private void ResetReaction()
    {
        reactionEnabled = false;
        reactionReclaim = false;
        pendingReaction = null;
        hostedComboId = -1;
        reacting = false;
    }

    /// <summary>站着放弹幕时每帧调用：玩家贴近、AI 挑出了反应招 → 把弹幕托管给发射器，出招协程随后去出反应招</summary>
    private void TryStartReaction()
    {
        if (!(activeExecutor is BAE_BulletEmitter emitter) || emitter.ForegroundComboId < 0) return;

        ActionNode reaction = boss.AI.TryPickReaction();
        if (reaction == null) return;

        int id = emitter.ForegroundComboId;
        if (!emitter.ReleaseToBackground()) return;

        hostedComboId = id;
        pendingReaction = reaction;
        emitter.SetComboReactive(id, true); // 留在弹幕层的话（不取回 / 反应被打断），Boss 走位时还能再反应
        Debug.Log($"[Boss] 近身反应：弹幕先交给发射器发，身体出「{reaction.actionName}」");
    }

    /// <summary>出反应招（内联在出招协程里，打断时一起停）。这期间玩家打断的是反应招</summary>
    private IEnumerator RunReaction(ActionNode reaction, ActionNode bulletNode)
    {
        reacting = true;
        PauseFireMove();
        CurrentAction = reaction;
        ShowActionLabel(reaction, "近身反应");

        IBossActionExecutor executor = boss.GetExecutorFor(reaction);
        if (executor == null)
        {
            Debug.LogError($"[BossActionExecuter] 没有执行器认领反应招 {reaction.GetType().Name}（卡片: {reaction.name}）");
        }
        else
        {
            activeExecutor = executor;
            yield return executor.Execute(reaction, boss.Context);
        }

        activeExecutor = null;
        CurrentAction = bulletNode;
        boss.StopHorizontal();
        reacting = false;
    }

    /// <summary>调试文字：显示正在出的招（套路名 / 近身反应 + 招式名）</summary>
    private void ShowActionLabel(ActionNode node, string prefix)
    {
        if (boss.bossStatusText == null) return;
        string label = string.IsNullOrEmpty(node.actionName) ? node.name : node.actionName;
        if (!string.IsNullOrEmpty(prefix)) label = $"[{prefix}] {label}";
        boss.bossStatusText.SetText(label);
    }

    // ==========================================================
    //  开火时移动
    // ==========================================================

    /// <summary>出这一招之前调用：弹幕卡按设置决定这一招怎么动，其他卡站定</summary>
    private void BeginFireMove(ActionNode node)
    {
        fireMoving = false;
        holding = false;

        FireMoveMode mode = boss.AI.ResolveFireMove(node, out FireMoveSettings set);
        if (mode == FireMoveMode.StandStill || set == null) return;

        fireMode = mode;
        fireSpeed = boss.AI.CurrentMoveSpeed * Mathf.Max(0f, set.speedMultiplier);

        if (mode == FireMoveMode.KeepDistance)
        {
            keepDistance = set.RollKeepDistance();
        }
        else
        {
            float playerX = boss.PlayerTransform.position.x;
            float bossX = boss.transform.position.x;
            float awaySign = bossX >= playerX ? 1f : -1f;
            moveDir = set.direction == FireMoveDirection.AwayFromPlayer ? awaySign : -awaySign;
            moveRemaining = Mathf.Max(0f, set.moveDistance);
            lastX = bossX;
            pushedLastStep = false;
            if (moveRemaining <= 0f) holding = true;
        }

        fireMoving = true;
    }

    /// <summary>近身反应期间：开火时移动先停下（取回后 ResumeFireMove 接着走）</summary>
    private void PauseFireMove()
    {
        fireMovePaused = fireMoving;
        if (!fireMoving) return;
        fireMoving = false;
        boss.StopHorizontal();
    }

    /// <summary>弹幕取回：接着反应前的开火时移动走（保持距离用原来抽到的距离；位移后站定走完剩下的格数）</summary>
    private void ResumeFireMove()
    {
        if (!fireMovePaused) return;
        fireMovePaused = false;
        lastX = boss.transform.position.x; // 反应招期间挪动的不算进「已经走了多少」
        pushedLastStep = false;
        fireMoving = true;
    }

    /// <summary>这一招放完 / 被打断：不再动，水平速度清零</summary>
    private void EndFireMove()
    {
        fireMovePaused = false;
        if (!fireMoving) return;
        fireMoving = false;
        boss.StopHorizontal();
    }

    private void TickFireMove()
    {
        if (fireMode == FireMoveMode.KeepDistance)
        {
            float dirX = boss.DriveTowardDistance(
                keepDistance - KeepDistanceTolerance, keepDistance + KeepDistanceTolerance, fireSpeed);
            boss.SetMovementStatus(dirX == 0f
                ? $"[移动] 开火·保持 {keepDistance:F1}（到位）"
                : $"[移动] 开火·保持 {keepDistance:F1}（当前 {boss.DistanceToPlayer:F1}）");
            return;
        }

        // ---- 位移后站定 ----
        if (holding) return;

        if (body == null && !boss.TryGetComponent(out body)) { holding = true; return; }
        Rigidbody2D rb = body;

        float x = rb.position.x;
        float moved = Mathf.Abs(x - lastX);
        lastX = x;
        moveRemaining -= moved;

        // 走够了，或者上一个物理帧给了速度却没挪动（撞墙）→ 站住
        bool stuck = pushedLastStep && moved < 0.0005f;
        if (moveRemaining <= 0.001f || stuck)
        {
            holding = true;
            pushedLastStep = false;
            boss.StopHorizontal();
            boss.SetMovementStatus("[移动] 开火·站定");
            return;
        }

        // 最后一个物理帧只走剩下的那一点
        float speed = Mathf.Min(fireSpeed, moveRemaining / Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(moveDir * speed, rb.linearVelocity.y);
        pushedLastStep = true;
        boss.SetMovementStatus($"[移动] 开火·位移 还剩 {moveRemaining:F1}");
    }

    public void Update()
    {
        if (attackCoroutine != null)
        {
            // 正在出反应招，或者已经决定反应、等弹幕交出去：这期间不脱手、不再检查反应
            if (reacting || pendingReaction != null) return;

            // 到了脱手的时间：让发射器接手剩下的弹幕，出招协程随后结束、Boss 去做下一步
            if (releaseAt >= 0f && boss.ActionTime >= releaseAt)
            {
                TryRelease();
                return;
            }

            // 站着放允许近身反应的弹幕：玩家贴近就先出反应招
            if (reactionEnabled && !released) TryStartReaction();
            return;
        }

        // 进入战斗状态时意图已经就位（MoveState 判断过射程），直接出招。
        // SelectSkill 出手前还会再校验一次，不通过就返回 null → 回移动状态重新定意图。
        attackCoroutine = boss.StartCoroutine(DoCombat());
    }

    IEnumerator DoCombat()
    {
        // 1. 兑现 AI 在移动阶段定好的意图（抽出来的是基类 ActionNode）
        ActionNode actionNode = boss.AI.SelectSkill();

        if (actionNode == null)
        {
            Finish();
            yield break;
        }

        CurrentAction = actionNode;
        afterRelease = false; // 出下一招了：上一张弹幕脱手后的走位限制到此结束
        returnToMoveAfter = boss.AI.LastPickWasReaction;
        ResetReaction();

        // 套路步骤的"出招前停顿"（单招为 0）。停顿期间被破盾等打断时，Exit 会掐掉本协程
        float delay = boss.AI.NextActionDelay;
        if (delay > 0f)
        {
            yield return BossTime.Wait(boss, delay);

            // 停顿期间玩家可能绕到背后 / 起飞了：按此刻情况重新检查这一步的候补招式
            actionNode = boss.AI.RefreshStepChoice(actionNode);
            CurrentAction = actionNode;
        }

        // 2. 查表找到认领这种卡的执行器。大脑到此为止，不关心它是什么技能。
        IBossActionExecutor executor = boss.GetExecutorFor(actionNode);

        if (executor == null)
        {
            Debug.LogError(
                $"[BossActionExecuter] 没有执行器认领 {actionNode.GetType().Name}" +
                $"（卡片: {actionNode.name}）。请检查对应 Executor 是否挂在 Boss 身上。");
            yield return new WaitForSeconds(1f); // 停顿一下防止空转刷屏
            Finish();
            yield break;
        }

        ShowActionLabel(actionNode, boss.AI.LastPickRoutineName);

        // 3. 交给执行器演完。
        //
        // 【注意】这里是 yield return executor.Execute(...)，而不是
        // yield return boss.StartCoroutine(executor.Execute(...))。
        // 后者会派生出一个独立的子协程，父协程被 StopCoroutine 时子协程**不会**被连带停止，
        // 打断时就会残留一个还在跑的动作。直接 yield return IEnumerator
        // 相当于把它内联进本协程，父停子必停。
        activeExecutor = executor;
        BeginFireMove(actionNode);
        SetupRelease(actionNode, executor);
        SetupReaction(actionNode, executor);
        if (executor is BAE_BulletEmitter followEmitter)
            followEmitter.SetNextFollow(boss.AI.ResolveFollowBoss(actionNode)); // 交出去后跟不跟随 Boss（套路步骤可覆盖）
        yield return executor.Execute(actionNode, boss.Context);

        // 4. 近身反应：弹幕已经托管给发射器，身体出反应招；打完按设置把弹幕取回、接着放完这一步。
        //    取回后玩家再贴近（过了全局反应冷却）可以再反应一次，所以是循环
        while (pendingReaction != null)
        {
            ActionNode reaction = pendingReaction;
            pendingReaction = null;
            yield return RunReaction(reaction, actionNode);

            BAE_BulletEmitter emitter = boss.BulletEmitter;
            if (!reactionReclaim || emitter == null || !emitter.IsComboAlive(hostedComboId))
                break; // 不取回 / 弹幕已经发完：直接做下一步

            if (releaseAt >= 0f && boss.ActionTime >= releaseAt)
            {
                // 反应期间已经过了脱手的时间点：不取回，当作已经脱手（按脱手参数去做下一步）
                releaseAt = -1f;
                released = releaseSettings != null;
                Debug.Log("[Boss] 近身反应期间已过脱手时间，弹幕不取回，按脱手处理");
                break;
            }

            emitter.Reclaim(hostedComboId);
            activeExecutor = emitter;
            CurrentAction = actionNode;
            ShowActionLabel(actionNode, boss.AI.LastPickRoutineName);
            ResumeFireMove();
            yield return emitter.WaitForeground((BulletNode)actionNode, hostedComboId);
        }

        releaseAt = -1f;
        EndFireMove();
        activeExecutor = null;

        Finish();
    }

    /// <summary>收尾：先清协程句柄再切状态，避免 Exit() 把自己给停了。</summary>
    private void Finish()
    {
        ActionNode finishedNode = CurrentAction;
        attackCoroutine = null;
        activeExecutor = null;
        CurrentAction = null;

        // 这一招脱手了：记下脱手后的走位限制，直到出下一招为止
        if (released && releaseSettings != null)
        {
            afterRelease = true;
            afterReleaseHold = Mathf.Max(0f, releaseSettings.holdTime);
            afterReleaseDash = releaseSettings.allowDash;
        }
        released = false;
        releaseSettings = null;
        ResetReaction();

        // 这一招正常放完（或脱手）：如果是套路最后一招，从现在开始它的后续招式间隔
        boss.AI.NotifyActionEnded(completed: true);

        // 这一招被玩家拼刀、拼刀那招填了停顿：先原地停顿，停完回移动状态接着套路（套路进度保留）
        if (boss.TryConsumeClashStagger(out float stagger))
        {
            returnToMoveAfter = false;
            afterRelease = false;
            boss.EnterClashStagger(stagger);
            return;
        }

        // 招式卡配了「放完后播放演出」：交给演出导演，演完回移动状态接着原来的流程（套路进度保留）
        if (finishedNode != null && finishedNode.cinematicAfter != null && boss.Cinematics != null)
        {
            returnToMoveAfter = false;
            afterRelease = false;
            boss.Cinematics.Play(finishedNode.cinematicAfter);
            return;
        }

        // 走位中触发的反应招打完：回移动状态，接着原来的流程（套路进度、脱手后的走位限制都还在）
        if (returnToMoveAfter)
        {
            returnToMoveAfter = false;
            afterRelease = false;
            boss.ChangeState(boss.MoveState);
            return;
        }

        // 套路的下一步可以马上接 → 留在战斗状态，下一帧 Update 直接出下一招。
        // 不这么做的话，每一步之间都会先回移动状态走一段，套路就连不起来了。
        // 脱手且填了脱手时间的除外：脱手时间内不出下一招，回移动状态走位。
        // 留下来时不会再走 Enter，这里先把上一招留下的水平速度清掉（比如开火时在走）
        bool holdAfterRelease = afterRelease && afterReleaseHold > 0f;
        if (boss.AI.HasImmediateFollowUp && !holdAfterRelease)
        {
            boss.StopHorizontal();
            return;
        }

        // 回移动状态：脱手后还没出下一招的话，把这段走位的限制交给移动状态
        if (afterRelease) boss.MoveState.ArmAfterRelease(afterReleaseHold, afterReleaseDash);
        afterRelease = false;
        boss.ChangeState(boss.MoveState);
    }
}