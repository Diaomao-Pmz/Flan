using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 下落状态。
///
/// 【批次E 改动】空中水平控制挪到 FixedUpdate。
/// 【飞行重做】删掉了「长按飞行键悬停蓄力」—— 起飞改由飞行键一键开关。
///
/// 【无敌下坠】飞行中按 Ctrl 解除飞行时，路由器会先调 PrepareInvulnerableDrop 再切进来：
/// 本次下落全程无敌，离开本状态（落地、再起飞、二段跳、攻击、冲刺…）即解除。
/// </summary>
public class FallState : PlayerStateBase
{
    private float originalGravity;

    private bool pendingInvulnerableDrop;
    private bool isInvulnerableDrop;

    /// <summary>
    /// 让【下一次】进入下落时变成无敌下坠。必须在 ChangeState(fallState) 之前调用，用一次就清。
    /// PlayerMovementConfig 里关掉「飞行下坠无敌」时不生效。
    /// </summary>
    public void PrepareInvulnerableDrop()
    {
        var cfg = sm.playerState != null ? sm.playerState.movementConfig : null;
        pendingInvulnerableDrop = cfg == null || cfg.flightDropInvulnerable;
    }

    public FallState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        sm.animDriver.SetBase(PlayerAnimHash.JumpFall);

        originalGravity = sm.defaultGravityScale;   // 读出厂值，不读当前值（见 PlayerStateMachine.defaultGravityScale）

        isInvulnerableDrop = pendingInvulnerableDrop;
        pendingInvulnerableDrop = false;

        if (isInvulnerableDrop)
            sm.playerState?.health.RequestUntargetable(InvulnerableTokens.FlightDrop);
    }

    public override void Update()
    {
        // 空中蓄力悬停时连朝向也锁住（同 JumpState）
        if (!IsAirChargePinned) UpdateFacing(sm.playerController.moveInput.x);

        if (sm.IsGrounded())
        {
            HandleLanding();
        }
    }

    public override void FixedUpdate()
    {
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
        sm.rb.gravityScale = originalGravity;

        if (isInvulnerableDrop)
        {
            sm.playerState?.health.ReleaseUntargetable(InvulnerableTokens.FlightDrop);
            isInvulnerableDrop = false;
        }
    }
}