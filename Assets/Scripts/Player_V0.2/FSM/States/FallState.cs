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
        sm.animDriver.SetBase(PlayerAnimHash.JumpFall);

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

        // 空中蓄力时禁止方向键移动 —— 想调整位置只能花一次冲刺（Shift 朝鼠标方向冲）。
        // 这让空中蓄力成为一次高风险承诺，而不是可以自由飘着蓄。
        if (IsAirMoveLockedByCharge)
        {
            // 空中蓄力：方向键锁死，但限制下落速度，
            // 否则蓄力还没蓄满人就落地了
            ClampAirChargeFall();
            return;
        }

        ApplyHorizontalMove();
    }

    public override void Exit()
    {
        hover.OnExit();
        sm.rb.gravityScale = originalGravity;
    }
}