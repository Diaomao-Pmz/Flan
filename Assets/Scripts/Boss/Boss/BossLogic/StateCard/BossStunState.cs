using UnityEngine;

/// <summary>
/// 破盾眩晕。
///
/// 【改动】只晕固定的 stunDuration 秒，结束后回到移动状态 —— 此时护盾仍是碎的，
/// Boss 进入「近战为主阶段」，按近战姿态的权重出招。
/// 回盾不再由本状态负责：倒计时在 BossMechanic.Tick 里，从破盾那一刻开始算。
/// </summary>
public class BossStunState : IState
{
    private BossController boss;
    private float stunTimer;
    private float stunDuration;
    private Rigidbody2D rb;

    public BossStunState(BossController bc)
    {
        boss = bc;
        rb = boss.GetComponent<Rigidbody2D>();
    }

    public void Enter()
    {
        BossMechanic mech = boss.bossState.bossMechanic;

        stunDuration = mech.stunDuration;
        stunTimer = 0f;

        Debug.Log($"[BossStunState] 护盾破碎，眩晕 {stunDuration:F1} 秒（{mech.EffectiveRecoverTime:F1} 秒后回盾）");

        // 直接赋予绝对速度，不受质量影响
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(0f, mech.stunKnockupSpeed);
        }

        // 强制打断正在发射的弹幕
        if (boss.BulletEmitter != null) boss.BulletEmitter.StopAttack();
    }

    public void Update()
    {
        stunTimer += Time.deltaTime;

        if (stunTimer >= stunDuration)
        {
            boss.ChangeState(boss.MoveState);
        }
    }

    public void FixedUpdate()
    {

    }

    public void Exit()
    {
        Debug.Log("[BossStunState] 眩晕结束" +
                  (boss.bossState.bossMechanic.isShieldBroken ? "，进入近战为主阶段" : ""));
    }
}
