using UnityEngine;
using Flandre.CombatSystem;
/// <summary>
/// 待机状态。
///
/// 【批次D 改动】继承 PlayerStateBase，省去 sm 字段与构造函数样板。行为完全不变。
/// </summary>
public class IdleState : PlayerStateBase
{
    public IdleState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        // 落地静止，保留 Y 轴物理速度（防止下落瞬间微弱回弹Bug）
        sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);
        sm.anim.Play(PlayerAnimHash.Idle, 0, 0f);
    }

    public override void Update()
    {
        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
            return;
        }

        if (Mathf.Abs(sm.playerController.moveInput.x) > 0.1f)
        {
            sm.ChangeState(sm.runState);
        }
    }
}
