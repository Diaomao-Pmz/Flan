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

    public BossActionExecuter(BossController bc)
    {
        boss = bc;
    }

    public void Enter()
    {
        // 每次进入战斗状态时，确保物理速度归零，专心出招（实现"射击时不移动"）
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

        // 被打断/破盾/转阶段离场时也要清掉登记，
        // 否则 Boss 已经不在出招了，打断判定却还以为它在演上一张卡。
        boss.SetCurrentActionNode(null);
    }

    public void FixedUpdate()
    {

    }

    public void Update()
    {
        // 刚被打断 → 硬直期间不出招，给玩家留追击窗口
        if (boss.IsStaggered) return;

        // 当满足攻击条件，且当前没有正在执行的攻击协程时，启动攻击
        if (boss.AI.canAttack && attackCoroutine == null)
        {
            attackCoroutine = boss.StartCoroutine(DoCombat());
        }
    }

    IEnumerator DoCombat()
    {
        // 1. 让 AI 抽盲盒（抽出来的是基类 ActionNode）
        ActionNode actionNode = boss.AI.SelectSkill();

        if (actionNode == null)
        {
            Finish();
            yield break;
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

        if (boss.bossStatusText != null)
        {
            string label = string.IsNullOrEmpty(actionNode.actionName)
                ? actionNode.name
                : actionNode.actionName;
            boss.bossStatusText.SetText(label);
        }

        // 3. 交给执行器演完。
        //
        // 【注意】这里是 yield return executor.Execute(...)，而不是
        // yield return boss.StartCoroutine(executor.Execute(...))。
        // 后者会派生出一个独立的子协程，父协程被 StopCoroutine 时子协程**不会**被连带停止，
        // 打断时就会残留一个还在跑的动作。直接 yield return IEnumerator
        // 相当于把它内联进本协程，父停子必停。
        activeExecutor = executor;

        // 登记当前动作卡：打断判定需要知道"现在做的是哪一类动作"。
        // 这里是唯一知道"正在演哪张卡"的地方，所以由它负责告知 Controller。
        boss.SetCurrentActionNode(actionNode);

        yield return executor.Execute(actionNode, boss.Context);

        boss.SetCurrentActionNode(null);
        activeExecutor = null;

        Finish();
    }

    /// <summary>收尾：先清协程句柄再切状态，避免 Exit() 把自己给停了。</summary>
    private void Finish()
    {
        attackCoroutine = null;
        activeExecutor = null;
        boss.SetCurrentActionNode(null);
        boss.ChangeState(boss.MoveState);
    }
}