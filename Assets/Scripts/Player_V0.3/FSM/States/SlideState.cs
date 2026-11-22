using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 滑铲状态。跑动中按 C 触发，速度衰减到阈值后自动转入蹲下。
/// </summary>
public class SlideState : PlayerStateBase
{
    private float currentSlideSpeed;

    /// <summary>这次滑铲已经滑了多久。滑满 slideDuration 就转入蹲下（物理帧里计时，和画面帧率无关）</summary>
    private float slideTimer;

    /// <summary>已滑满时长，下一物理帧转入蹲下</summary>
    private bool slideFinished;

    /// <summary>终点裁判：不停在 Boss 身体里（见 DashEndpointGuard）</summary>
    private readonly GuardedMove guard;
    private float slideDirection;
    private bool isFirstHit;
    private bool didStartSlide;

    /// <summary>正在进行常规滑铲（被宝石接管时为 false）。供穿身开关查询，需配合"当前状态是本状态"使用</summary>
    public bool IsSliding => didStartSlide;

    // ==========================================================
    // 【空中 Ctrl 下落 → 落地接滑铲】
    // 由 FallState 在落地时调用。这次滑铲：
    // ==========================================================
    private bool pendingLandingSlide;
    private bool pendingLandingInvulnerable;

    public void PrepareLandingSlide(bool invulnerable)
    {
        pendingLandingSlide = true;
        pendingLandingInvulnerable = invulnerable;
    }

    public SlideState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        guard = new GuardedMove(stateMachine);
    }

    /// <summary>这次滑铲的总距离：匀减速（减到 0 就停）走完 slideDuration</summary>
    private float PlannedSlideDistance()
    {
        float v0 = sm.slideStartSpeed;
        float a = sm.slideDeceleration;
        float T = sm.slideDuration;

        if (a > 0f && v0 / a < T) return v0 * v0 / (2f * a);   // 没到时间就减到 0 了
        return (v0 + Mathf.Max(0f, v0 - a * T)) * 0.5f * T;
    }

    public override void Enter()
    {
        didStartSlide = false;

        // CD 闸门已由路由器前置校验
        bool fromLanding = pendingLandingSlide;
        bool landingInvulnerable = pendingLandingInvulnerable;
        pendingLandingSlide = false;
        pendingLandingInvulnerable = false;

        if (fromLanding)
        {
            // 落地接的滑铲不走冷却：不 Execute，退出时也不开冷却（isFirstHit = false）
            isFirstHit = false;
        }
        else
        {
            isFirstHit = sm.slideSkill.IsComboIdle;
            sm.slideSkill.Execute();
        }

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
        bool configInvulnerable = cfg == null || cfg.slideInvulnerable;

        // 普通滑铲：按配置无敌。落地接的滑铲：还要看那次下落是不是无敌的
        if (configInvulnerable && (!fromLanding || landingInvulnerable))
            sm.playerState?.health.RequestUntargetable(InvulnerableTokens.Slide);

        sm.animDriver.SetBase(PlayerAnimHash.Slide);
        sm.SetColliderHeight(true);
        if (sm.dashTrail != null) sm.dashTrail.emitting = true;

        currentSlideSpeed = sm.slideStartSpeed;
        slideTimer = 0f;
        slideFinished = false;

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

        guard.Begin(new Vector2(slideDirection, 0f), PlannedSlideDistance(), sm.slideDuration,
                    cfg != null && cfg.slidePassThrough);
    }

    public override void Update()
    {
        sm.loadout?.NotifyActionUpdate(ActionType.Slide, Time.deltaTime);

        // 滑满时长 → 转蹲下：改在 FixedUpdate 里判断（原先在这里，低帧率时会多滑一两个物理帧）
        if (!slideFinished && !sm.IsGrounded()) sm.ChangeState(sm.fallState);
    }

    public override void FixedUpdate()
    {
        if (!didStartSlide) return;   // 被宝石接管：不推进

        // 上一物理帧已经滑满：交接给蹲下（速度和原来一样是滑铲末速度，蹲下状态自己接手）
        // 被 Boss 挡住停在它面前的，交接速度为 0
        if (slideFinished)
        {
            float handover = guard.Blocked ? 0f : slideDirection * currentSlideSpeed;
            sm.rb.linearVelocity = new Vector2(handover, sm.rb.linearVelocity.y);
            guard.End();
            sm.ChangeState(sm.crouchState);
            return;
        }

        // 【精确距离】匀减速：这一步的平均速度 =（步初速度 + 步末速度）/ 2。
        // 最后一个物理帧只滑剩下的那一点，总距离正好 = 滑铲距离，和帧率无关。
        float dt = Time.fixedDeltaTime;
        float timeStep = Mathf.Max(0f, Mathf.Min(dt, sm.slideDuration - slideTimer));
        slideTimer += timeStep;

        float startSpeed = currentSlideSpeed;
        currentSlideSpeed = Mathf.Max(0f, currentSlideSpeed - sm.slideDeceleration * timeStep);
        float avgSpeed = (startSpeed + currentSlideSpeed) * 0.5f;

        // 终点裁判可能让这一步变短（停在 Boss 面前），或在时间到了之后继续滑（穿到 Boss 身后）
        float step = guard.Step(avgSpeed * timeStep);

        sm.rb.linearVelocity = new Vector2(
            slideDirection * (step / dt),
            sm.rb.linearVelocity.y);

        // 滑铲时长不变：被挡住就贴着 Boss 停到时间结束；要穿到身后就滑完才结束
        if (slideTimer >= sm.slideDuration - 0.00001f && guard.Reached) slideFinished = true;
    }

    public override void Exit()
    {
        sm.loadout?.NotifyActionExit(ActionType.Slide, !didStartSlide);

        if (!didStartSlide) return;

        guard.End();

        // 没申请过时释放也是安全的（引用计数里本来就没有这把钥匙）
        sm.playerState?.health.ReleaseUntargetable(InvulnerableTokens.Slide);

        sm.slideSkill.StartCooldownIfFirstHit(isFirstHit);
        if (sm.dashTrail != null) sm.dashTrail.emitting = false;

        sm.SetColliderHeight(false);
    }
}