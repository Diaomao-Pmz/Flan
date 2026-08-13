using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 下落状态。
///
/// 【批次E 改动】空中水平控制挪到 FixedUpdate。
/// 悬停蓄力逻辑与 JumpState 共用 HoverChargeHandler。
/// </summary>
public class FallState : PlayerStateBase
{
    private readonly HoverChargeHandler hover;
    private float originalGravity;

    public FallState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        hover = new HoverChargeHandler(stateMachine);
    }

    public override void Enter()
    {
        sm.anim.Play(PlayerAnimHash.JumpFall);

        originalGravity = sm.rb.gravityScale;
        hover.OnEnter(originalGravity);
    }

    public override void Update()
    {
        var hoverResult = hover.Tick(Time.deltaTime);

        if (hoverResult == HoverChargeHandler.Result.EnterFly)
        {
            sm.ChangeState(sm.flyState);
            return;
        }
        if (hoverResult == HoverChargeHandler.Result.Hovering) return;

        UpdateFacing(sm.playerController.moveInput.x);

        if (sm.IsGrounded())
        {
            HandleLanding();
        }
    }

    public override void FixedUpdate()
    {
        if (hover.IsHovering) return;

        ApplyHorizontalMove();
    }

    public override void Exit()
    {
        hover.OnExit();
        sm.rb.gravityScale = originalGravity;
    }
}
