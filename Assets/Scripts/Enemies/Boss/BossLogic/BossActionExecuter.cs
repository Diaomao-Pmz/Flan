using System.Collections;
using UnityEngine;
using TMPro;

public class BossActionExecuter : IState
{
    BossController boss;
    Coroutine attackCoroutine;

    public BossActionExecuter(BossController bc)
    {
        boss = bc;
    }

    public void Enter()
    {
        // 每次进入战斗状态时，确保物理速度归零，专心出招（实现“射击时不移动”）
        Rigidbody2D rb = boss.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    public void Exit()
    {
        // ========================================================
        // 【核心安全保护】：如果 Boss 在射击期间被破盾（进入 StunState）或转阶段
        // 强制掐断当前正在运行的开火协程，并关闭弹幕发射器，防止出现“晕了还在射击”的 Bug
        // ========================================================
        if (attackCoroutine != null)
        {
            boss.StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        if (boss.BulletEmitter != null)
        {
            boss.BulletEmitter.StopAttack();
        }
    }

    public void Update()
    {
        // 当满足攻击条件，且当前没有正在执行的攻击协程时，启动攻击
        if (boss.AI.canAttack && attackCoroutine == null)
        {
            attackCoroutine = boss.StartCoroutine(DoCombat());
        }
    }

    IEnumerator DoCombat()
    {
        // 1. 让 AI 抽盲盒 (抽出来的是基类 ActionNode)
        ActionNode actionNode = boss.AI.SelectSkill();

        if (actionNode == null)
        {
            boss.ChangeState(boss.MoveState);
            attackCoroutine = null;
            yield break;
        }

        // 2. 盲盒是【传送卡】吗？
        if (actionNode is TeleportNode tpNode)
        {
            if (boss.bossStatusText != null) boss.bossStatusText.SetText("Teleport!");

            // 【关键修改】：把 Node 里的战术策略传递给专属执行器
            boss.Teleporter.ExecuteTeleport(tpNode.targetType);

            boss.ChangeState(boss.MoveState);
            attackCoroutine = null;
            yield break;
        }

        // 3. 盲盒是【弹幕卡】吗？
        if (actionNode is BulletNode bulletNode)
        {
            if (boss.bossStatusText != null) boss.bossStatusText.SetText($"{bulletNode.AttackName}!");

            // 【极其重要的防呆】：检查你有没有在 Inspector 里忘填 AttackType！
            if (!string.IsNullOrEmpty(bulletNode.AttackName))
            {
                boss.BulletEmitter.StartAttack(bulletNode.AttackName, bulletNode.formationDuration);
                yield return new WaitForSeconds(3f);
                boss.BulletEmitter.StopAttack();
            }
            else
            {
                Debug.LogError($"[警告] 卡片 {bulletNode.name} 的 AttackType 是空的！请去 Inspector 里填写！");
                yield return new WaitForSeconds(1f); // 停顿1秒防卡死
            }
        }
        else
        {
            // 如果以后有近战卡，在这里接着写 else if (actionNode is MeleeNode)
            yield return new WaitForSeconds(1f);
        }

        boss.ChangeState(boss.MoveState);
        attackCoroutine = null;
    }
}