using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 待机状态。
///
/// 【本次改动】后摇期间不再转入跑动。
///
/// 后摇发生在攻击动画【结束之后】，那时状态已经切回 Idle/Run 了 ——
/// 所以原先玩家在后摇里按 a/d 是能走的，违反「攻击时不能走动」的基础规则。
/// 后摇属于攻击的一部分，这段时间也该站住。
/// </summary>
public class IdleState : PlayerStateBase
{
    public IdleState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        // 落地静止，保留 Y 轴物理速度（防止下落瞬间微弱回弹Bug）
        sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);
        sm.animDriver.SetBase(PlayerAnimHash.Idle, restart: true);
    }

    public override void Update()
    {
        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
            return;
        }

        // 后摇中：按方向键也不转入跑动，站着把后摇走完
        if (IsMovementLockedByAttack) return;

        if (Mathf.Abs(sm.playerController.moveInput.x) > 0.1f)
        {
            sm.ChangeState(sm.runState);
        }
    }
}