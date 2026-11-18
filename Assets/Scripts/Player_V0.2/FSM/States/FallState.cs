using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 下落状态。
/// </summary>
public class FallState : PlayerStateBase
{
    private float originalGravity;

    private bool pendingInvulnerableDrop;
    private bool isInvulnerableDrop;

<<<<<<< Updated upstream
=======
    // 【2026-11-16】空中按 Ctrl 的下落（空中滑铲）：无论有没有无敌，落地都切进蹲下姿态
    private bool pendingCtrlDrop;
    private bool isCtrlDrop;

    // 这次 Ctrl 下落开始时滑铲冷却是不是好的（好 = 下落无敌，落地接的滑铲也无敌）
    private bool pendingCtrlDropCooldownReady;
    private bool ctrlDropCooldownReady;

    // 最近一次按着左右方向键的时刻。落地时用它判断"落地前 n 秒内有没有方向键输入"
    private float lastHorizontalInputTime = -10f;

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
        pendingCtrlDropCooldownReady = invulnerable;

        if (invulnerable) PrepareInvulnerableDrop();
        else pendingInvulnerableDrop = false;
    }

>>>>>>> Stashed changes
    /// <summary>
    /// 让【下一次】进入下落时变成无敌下坠。必须在 ChangeState(fallState) 之前调用，用一次就清。
    /// PlayerMovementConfig 里关掉「空中 Ctrl 下落无敌」时不生效。
    /// </summary>
    public void PrepareInvulnerableDrop()
    {
        var cfg = sm.playerState != null ? sm.playerState.movementConfig : null;
        pendingInvulnerableDrop = cfg == null || cfg.airDropInvulnerable;
    }

    public FallState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        sm.animDriver.SetBase(PlayerAnimHash.JumpFall);

        originalGravity = sm.defaultGravityScale;   // 读出厂值，不读当前值（见 PlayerStateMachine.defaultGravityScale）

        isInvulnerableDrop = pendingInvulnerableDrop;
        pendingInvulnerableDrop = false;

<<<<<<< Updated upstream
=======
        isCtrlDrop = pendingCtrlDrop;
        pendingCtrlDrop = false;

        ctrlDropCooldownReady = pendingCtrlDropCooldownReady;
        pendingCtrlDropCooldownReady = false;
        lastHorizontalInputTime = -10f;

>>>>>>> Stashed changes
        if (isInvulnerableDrop)
            sm.playerState?.health.RequestUntargetable(InvulnerableTokens.FlightDrop);
    }

    public override void Update()
    {
        UpdateFacing(sm.playerController.moveInput.x);

        if (Mathf.Abs(sm.playerController.moveInput.x) > 0.1f) lastHorizontalInputTime = Time.time;

        if (sm.IsGrounded())
        {
<<<<<<< Updated upstream
            HandleLanding();
=======
            if (!isCtrlDrop)
            {
                HandleLanding();
                return;
            }

            // 空中 Ctrl 下落的落地：
            //   落地前 n 秒内按过左右方向键 → 接滑铲（不看、不消耗滑铲冷却；无敌与否跟着这次下落走）
            //   没按                       → 切进蹲下姿态（没按着 Ctrl 的话 CrouchState 下一帧自己站起来）
            var cfg = sm.playerState != null ? sm.playerState.movementConfig : null;
            float window = cfg != null ? cfg.landingSlideInputWindow : 0.1f;

            if (Time.time - lastHorizontalInputTime <= window)
            {
                sm.slideState.PrepareLandingSlide(ctrlDropCooldownReady);
                sm.ChangeState(sm.slideState);
            }
            else
            {
                sm.ChangeState(sm.crouchState);
            }
>>>>>>> Stashed changes
        }
    }

    public override void FixedUpdate()
    {
<<<<<<< Updated upstream
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
=======
        // 空中 Ctrl 下落：重力乘以配置的倍率（默认 1 = 不加速）
        float dropMultiplier = 1f;
        if (isCtrlDrop)
        {
            var cfg = sm.playerState != null ? sm.playerState.movementConfig : null;
            if (cfg != null) dropMultiplier = Mathf.Max(0f, cfg.ctrlDropSpeedMultiplier);
        }

        sm.rb.gravityScale = originalGravity * dropMultiplier;
>>>>>>> Stashed changes

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