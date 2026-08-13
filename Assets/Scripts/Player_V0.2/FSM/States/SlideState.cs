using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 滑铲状态。跑动中按 C 触发，速度衰减到阈值后自动转入蹲下。
///
/// 【批次E 改动】减速积分与速度写入挪到 FixedUpdate，用 fixedDeltaTime。
///
/// 这一处是本批次里最值得挪的：
/// 滑铲距离 = 初速 与 减速率 的积分结果。
/// 减速写在 Update 里时，帧率越高积分步长越细、每步误差越小，
/// 高低帧下滑出去的距离会实打实地差一截。
/// 挪到固定步长后，30fps 和 144fps 滑一样远。
///
/// 阈值判断留在 Update，保证切蹲下的时机跟手。
/// </summary>
public class SlideState : PlayerStateBase
{
    private float currentSlideSpeed;
    private float slideDirection;
    private bool isFirstHit;
    private bool didStartSlide;

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

        sm.anim.Play(PlayerAnimHash.Slide);
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
    }

    public override void Exit()
    {
        sm.loadout?.NotifyActionExit(ActionType.Slide, !didStartSlide);

        if (!didStartSlide) return;

        sm.slideSkill.StartCooldownIfFirstHit(isFirstHit);
        if (sm.dashTrail != null) sm.dashTrail.emitting = false;

        sm.SetColliderHeight(false);
    }
}
