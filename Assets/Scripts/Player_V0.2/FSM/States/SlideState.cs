using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 滑铲状态。跑动中按 C 触发，速度衰减到阈值后自动转入蹲下。
///
/// 【批次O 改动】每帧把当前速度同步给 sm.slideMomentum。
///
/// 这样玩家在滑铲途中出招时，连招/蓄力状态可以接手这股冲劲继续滑 ——
/// 而滑铲的减速逻辑仍然只有这一份，没有被复制到别的状态里去。
///
/// 【滑铲无敌】正常滑铲（没被宝石接管）全程无敌，离开本状态即解除。
/// 开关在 PlayerMovementConfig.slideInvulnerable。蹲下不无敌。
/// </summary>
public class SlideState : PlayerStateBase
{
    private float currentSlideSpeed;
    private float slideDirection;
    private bool isFirstHit;
    private bool didStartSlide;

    /// <summary>正在进行常规滑铲（被宝石接管时为 false）。供穿身开关查询，需配合"当前状态是本状态"使用</summary>
    public bool IsSliding => didStartSlide;

    public SlideState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        didStartSlide = false;

        // CD 闸门已由路由器前置校验
        isFirstHit = sm.slideSkill.IsComboIdle;
        sm.slideSkill.Execute();

        GemActionResult gemResult = sm.loadout != null
            ? sm.loadout.NotifyActionEnter(ActionType.Slide, isFirstHit)
            : GemActionResult.Normal;

        if (gemResult != GemActionResult.Normal)
        {
            HandleFallback();
            return;
        }

        didStartSlide = true;

        var cfg = sm.playerState != null ? sm.playerState.movementConfig : null;
        if (cfg == null || cfg.slideInvulnerable)
            sm.playerState?.health.RequestUntargetable(InvulnerableTokens.Slide);

        sm.animDriver.SetBase(PlayerAnimHash.Slide);
        sm.SetColliderHeight(true);
        if (sm.dashTrail != null) sm.dashTrail.emitting = true;

        currentSlideSpeed = sm.moveSpeed * sm.slideStartSpeedMultiplier;

        float inputX = sm.playerController.moveInput.x;

        if (Mathf.Abs(inputX) > 0.1f)
        {
            slideDirection = Mathf.Sign(inputX);
            // 防「倒着滑」的视觉 Bug
            sm.playerController.SetFacingDirection(slideDirection > 0 ? 1 : -1);
        }
        else
        {
            slideDirection = sm.playerController.facingDirection;
        }

        // 立刻登记动量，这样出招那一帧接手的状态就能拿到正确的速度
        sm.slideMomentum.Set(currentSlideSpeed, slideDirection);
    }

    public override void Update()
    {
        sm.loadout?.NotifyActionUpdate(ActionType.Slide, Time.deltaTime);

        // 速度衰减到阈值 → 自然转入蹲下（「蹲不是独立入口」的实现处）
        float targetCrouchSpeed = sm.moveSpeed * sm.crouchSpeedMultiplier;

        if (currentSlideSpeed <= targetCrouchSpeed) sm.ChangeState(sm.crouchState);
        else if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
    }

    public override void FixedUpdate()
    {
        currentSlideSpeed -= sm.slideDeceleration * Time.fixedDeltaTime;
        sm.rb.linearVelocity = new Vector2(
            slideDirection * currentSlideSpeed,
            sm.rb.linearVelocity.y);

        // 同步给携带动量：玩家随时可能在这一帧出招接手
        sm.slideMomentum.Set(currentSlideSpeed, slideDirection);
    }

    public override void Exit()
    {
        sm.loadout?.NotifyActionExit(ActionType.Slide, !didStartSlide);

        // 【动量池】把这次位移的速度存进去，供蓄力一段的位移距离取用。
        // 存的是"刚才最快跑到多快"，不是累加 —— 连续冲刺不该把动量叠到天上。
        sm.GetComponent<PlayerMomentum>()?.Deposit(currentSlideSpeed);

        if (!didStartSlide) return;

        // 没申请过时释放也是安全的（引用计数里本来就没有这把钥匙）
        sm.playerState?.health.ReleaseUntargetable(InvulnerableTokens.Slide);

        sm.slideSkill.StartCooldownIfFirstHit(isFirstHit);
        if (sm.dashTrail != null) sm.dashTrail.emitting = false;

        sm.SetColliderHeight(false);

        // 注意：这里【不】清空 slideMomentum ——
        // 是否保留由 PlayerStateMachine.ChangeState 按目标状态统一裁决。
        // 切去连招/蓄力时保留，切去别的地方自动清空。
    }
}