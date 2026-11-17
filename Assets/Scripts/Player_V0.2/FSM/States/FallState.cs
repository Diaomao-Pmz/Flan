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

    // 【2026-11-16】空中按 Ctrl 的下落（空中滑铲）：无论有没有无敌，落地都切进蹲下姿态
    private bool pendingCtrlDrop;
    private bool isCtrlDrop;

    /// <summary>当前这次下落是空中按 Ctrl 触发的（落地会切进蹲下）</summary>
    public bool IsCtrlDrop => isCtrlDrop;

    /// <summary>
    /// 让【下一次】进入下落变成"空中 Ctrl 下落"。必须在 ChangeState(fallState) 之前调用，用一次就清。
    /// </summary>
    /// <param name="invulnerable">
    /// 这次下落是否无敌。由路由器按滑铲冷却决定：冷却好了 = 无敌（并消耗冷却），没好 = 普通下落。
    /// PlayerMovementConfig 里关掉无敌下落时，传 true 也不无敌。
    /// </param>
    public void PrepareCtrlDrop(bool invulnerable)
    {
        pendingCtrlDrop = true;

        if (invulnerable) PrepareInvulnerableDrop();
        else pendingInvulnerableDrop = false;
    }

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

        isCtrlDrop = pendingCtrlDrop;
        pendingCtrlDrop = false;

        if (isInvulnerableDrop)
            sm.playerState?.health.RequestUntargetable(InvulnerableTokens.FlightDrop);
    }

    public override void Update()
    {
        // 空中蓄力悬停时连朝向也锁住（同 JumpState）
        if (!IsAirChargePinned) UpdateFacing(sm.playerController.moveInput.x);

        if (sm.IsGrounded())
        {
            // 空中 Ctrl 下落：落地切进蹲下姿态（没按着 Ctrl 的话 CrouchState 下一帧自己站起来）
            if (isCtrlDrop) sm.ChangeState(sm.crouchState);
            else HandleLanding();
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

        // 空中 Ctrl 下落：重力乘以配置的倍率（默认 1 = 不加速）
        float dropMultiplier = 1f;
        if (isCtrlDrop)
        {
            var cfg = sm.playerState != null ? sm.playerState.movementConfig : null;
            if (cfg != null) dropMultiplier = Mathf.Max(0f, cfg.ctrlDropSpeedMultiplier);
        }

        // 蓄力一结束立刻还原重力，否则松手后角色会一直浮到状态切换为止
        sm.rb.gravityScale = originalGravity * dropMultiplier;

        // 只有空中起手的蓄力才锁方向键（同 JumpState）
        if (IsAirChargePinned) return;

        ApplyHorizontalMove();
    }

    public override void Exit()
    {
        sm.rb.gravityScale = originalGravity;

        isCtrlDrop = false;

        if (isInvulnerableDrop)
        {
            sm.playerState?.health.ReleaseUntargetable(InvulnerableTokens.FlightDrop);
            isInvulnerableDrop = false;
        }
    }
}