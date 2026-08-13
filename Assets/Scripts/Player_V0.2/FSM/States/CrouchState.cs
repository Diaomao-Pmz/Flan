using Flandre.CombatSystem;
using UnityEngine;

/// <summary>
/// 蹲下状态。
///
/// 蹲【不是】独立入口，而是滑铲的自然终点：
///   跑动中按 C → 滑铲 → 速度衰减到阈值 → 蹲下 → 松开 C 且头顶无障碍 → 站起
///
/// 【批次E 改动】蹲行速度写入挪到 FixedUpdate。
/// </summary>
public class CrouchState : PlayerStateBase
{
    public CrouchState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        sm.anim.Play(PlayerAnimHash.Crouch);
        sm.SetColliderHeight(true);
    }

    public override void Update()
    {
        bool isCrouchHeld = sm.playerController.isCrouchHeld;
        float moveDir = sm.playerController.moveInput.x;

        // 站起条件：没按住蹲键 且 头顶没有障碍物
        if (!isCrouchHeld && sm.CanStand())
        {
            HandleLanding();
            return;
        }

        UpdateFacing(moveDir);

        // 悬崖防掉落
        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
        }
    }

    public override void FixedUpdate()
    {
        ApplyHorizontalMove(sm.crouchMoveSpeedMultiplier);
    }

    public override void Exit()
    {
        sm.SetColliderHeight(false);
    }
}
