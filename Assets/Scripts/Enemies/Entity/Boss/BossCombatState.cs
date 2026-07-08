using System.Collections;
using UnityEngine;
using TMPro;

public class BossCombatState : IState
{
    BossController boss;
    Coroutine attackCoroutine;

    public BossCombatState(BossController bc)
    {
        boss = bc;
    }

    public void Enter()
    {
    }

    public void Exit()
    {
        
    }

    public void Update()
    {
        if (boss.AI.canAttack && attackCoroutine == null)
        {
            attackCoroutine = boss.StartCoroutine(DoCombat());
        }
    }

    IEnumerator DoCombat()
    {
        AttackNode attackNode = boss.AI.SelectSkill();

        if (attackNode == null)
        {
            boss.AI.StartResting();
            attackCoroutine = null;
            yield break;
        }

        if (boss.bossStatusText != null)
        {
            boss.bossStatusText.SetText($"{attackNode.AttackType}!");
        }

        //在下令开火时，把 Node 里的formation时间一并传给 Emitter
        boss.BulletEmitter.StartAttack(attackNode.AttackType, attackNode.formationDuration);

        yield return new WaitForSeconds(3f);

        boss.BulletEmitter.StopAttack();
        boss.AI.StartResting();
        attackCoroutine = null;
    }
}
