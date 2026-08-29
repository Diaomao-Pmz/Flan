using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 冲刺状态。
///
/// 【批次D 改动】HandleFallback 提到 PlayerStateBase，与 SlideState 共用。
/// 行为完全不变。
/// </summary>
public class DashState : PlayerStateBase
{
    private float dashTimer;
    private float originalGravity;
    private bool isFirstHit;

    /// <summary>常规冲刺是否真的启动了（被宝石接管时为 false）</summary>
    private bool didStartDash;

    /// <summary>
    /// 本次冲刺的指定方向。由路由器在切状态【之前】写入，用一次就清。
    ///
    /// 常规冲刺是纯水平的（沿角色朝向），
    /// 但「空中蓄力时朝鼠标冲」需要任意角度 —— 所以留这个覆盖入口。
    /// </summary>
    private Vector2? overrideDirection;

    /// <summary>指定下一次冲刺的方向。必须在 ChangeState(dashState) 之前调用</summary>
    public void SetNextDashDirection(Vector2 dir)
    {
        if (dir.sqrMagnitude > 0.0001f) overrideDirection = dir.normalized;
    }

    public DashState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        didStartDash = false;

        // CD / 硬直闸门已由 PlayerCommandRouter 前置校验
        isFirstHit = sm.dashSkill.IsComboIdle;
        sm.dashSkill.Execute();

        // ---- 问宝石：你要接管吗？ ----
        GemActionResult gemResult = sm.loadout != null
            ? sm.loadout.NotifyActionEnter(ActionType.Dash, isFirstHit)
            : GemActionResult.Normal;

        if (gemResult != GemActionResult.Normal)
        {
            // 这里调 ChangeState 是安全的 —— 状态机会排队，等本 Enter() 跑完再执行
            HandleFallback();
            return;
        }

        // ---- 常规冲刺 ----
        didStartDash = true;

        if (sm.dashTrail != null) sm.dashTrail.emitting = true;

        dashTimer = sm.dashDuration;
        originalGravity = sm.rb.gravityScale;
        sm.rb.gravityScale = 0f;

        if (overrideDirection.HasValue)
        {
            Vector2 dir = overrideDirection.Value;
            overrideDirection = null;   // 一次性，用完即清

            sm.rb.linearVelocity = dir * sm.dashSpeed;

            // 朝向跟着冲刺方向走，避免"往左冲却面朝右"
            if (Mathf.Abs(dir.x) > 0.01f)
                sm.playerController.SetFacingDirection(dir.x > 0f ? 1 : -1);
        }
        else
        {
            float direction = sm.playerController.facingDirection;
            sm.rb.linearVelocity = new Vector2(direction * sm.dashSpeed, 0f);
        }
    }

    public override void Update()
    {
        sm.loadout?.NotifyActionUpdate(ActionType.Dash, Time.deltaTime);

        dashTimer -= Time.deltaTime;
        if (dashTimer <= 0)
        {
            sm.rb.gravityScale = originalGravity;
            HandleFallback();
        }
    }

    public override void Exit()
    {
        // 即使被宝石接管，也要通知它收尾（释放无敌、还原图层等）
        sm.loadout?.NotifyActionExit(ActionType.Dash, !didStartDash);

        // 【动量池】把这次位移的速度存进去，供蓄力一段的位移距离取用。
        // 存的是"刚才最快跑到多快"，不是累加 —— 连续冲刺不该把动量叠到天上。
        sm.GetComponent<PlayerMomentum>()?.Deposit(sm.dashSpeed);

        if (!didStartDash) return;

        sm.dashSkill.StartCooldownIfFirstHit(isFirstHit);

        if (sm.dashTrail != null) sm.dashTrail.emitting = false;
        sm.rb.gravityScale = originalGravity;
    }
}