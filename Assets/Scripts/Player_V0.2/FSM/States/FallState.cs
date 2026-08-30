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

        originalGravity = sm.defaultGravityScale;   // 读出厂值，不读当前值（见 PlayerStateMachine.defaultGravityScale）
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

        // 空中蓄力悬停时连朝向也锁住（同 JumpState）
        if (!IsAirChargePinned) UpdateFacing(sm.playerController.moveInput.x);

        if (sm.IsGrounded())
        {
            HandleLanding();
        }
    }

    public override void FixedUpdate()
    {
        if (hover.IsHovering) return;

        // 空中蓄力：钉在原地悬停（详见 PlayerStateBase.PinInAirWhileCharging）。
        // 下落状态里 y 速度本来就 <= 0，所以这里几乎总是成立 ——
        // 但仍然走同一个判据，免得两张卡带的规则各写一套、日后走偏。
        if (IsAirChargePinned)
        {
            PinInAirWhileCharging();
            return;
        }

        // 蓄力一结束立刻还原重力，否则松手后角色会一直浮到状态切换为止
        sm.rb.gravityScale = originalGravity;

        // 只有空中起手的蓄力才锁方向键（同 JumpState）
        if (IsAirChargePinned) return;

        ApplyHorizontalMove();
    }

    public override void Exit()
    {
        hover.OnExit();
        sm.rb.gravityScale = originalGravity;
    }
}