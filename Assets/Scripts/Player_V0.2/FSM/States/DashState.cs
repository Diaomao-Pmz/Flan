using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 冲刺状态。
/// </summary>
public class DashState : PlayerStateBase
{
    // ---- 冲刺推进：在物理帧里计时，匀速走完「距离」，最后一个物理帧只走剩下的那一点 ----
    // （原先在画面帧里倒计时，停下前物理引擎会多跑几步，距离随帧率变化：30 帧时平均多 12%）
    private float dashTimer;            // 已经冲了多久
    private float dashTotalTime;        // 这次冲刺要用多久
    private Vector2 dashVelocity;       // 冲刺速度（方向 × 速度）
    private bool dashFinished;          // 已走完，下一物理帧交接给后续状态
    private float originalGravity;
    private bool isFirstHit;

    /// <summary>常规冲刺是否真的启动了（被宝石接管时为 false）</summary>
    private bool didStartDash;

    /// <summary>正在进行常规冲刺（被宝石接管时为 false）。供穿身开关查询，需配合"当前状态是本状态"使用</summary>
    public bool IsDashing => didStartDash;

    /// <summary>
    /// 本次冲刺的指定方向。由路由器在切状态【之前】写入，用一次就清。
    ///
    /// 不指定时沿角色朝向水平冲；八方向冲刺由路由器在这里指定方向。
    /// </summary>
    private Vector2? overrideDirection;

    /// <summary>指定下一次冲刺的方向。必须在 ChangeState(dashState) 之前调用</summary>
    public void SetNextDashDirection(Vector2 dir)
    {
        if (dir.sqrMagnitude > 0.0001f) overrideDirection = dir.normalized;
    }

    /// <summary>
    /// 下一次冲刺原地不动（地面按着正下方冲刺）：
    /// 位移上不做任何改变，但冲刺本身照常发生 —— 动画、冷却、穿人、宝石效果都在。
    /// </summary>
    private bool overrideStationary;

    /// <summary>本次冲刺是原地冲刺（退出时不存入冲劲）</summary>
    private bool isStationaryDash;

    public void SetNextDashStationary()
    {
        overrideStationary = true;
        overrideDirection = null;
    }

    public DashState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        didStartDash = false;
        isStationaryDash = false;

        // CD / 硬直闸门已由 PlayerCommandRouter 前置校验
        isFirstHit = sm.dashSkill.IsComboIdle;
        sm.dashSkill.Execute();

        // ---- 问宝石：你要接管吗？ ----
        GemActionResult gemResult = sm.loadout != null
            ? sm.loadout.NotifyActionEnter(ActionType.Dash, isFirstHit)
            : GemActionResult.Normal;

        if (gemResult != GemActionResult.Normal)
        {
            // 一次性的方向 / 距离设定用不上了，清掉，别留给下一次冲刺
            overrideStationary = false;
            overrideDirection = null;

            // 这里调 ChangeState 是安全的 —— 状态机会排队，等本 Enter() 跑完再执行
            HandleFallback();
            return;
        }

        // ---- 常规冲刺 ----
        didStartDash = true;

        if (sm.dashTrail != null) sm.dashTrail.emitting = true;

        // 【2026-11-16】冲刺接入动画。目前没有专门的冲刺动画，PlayerAnimHash.Dash 先指向滑铲动画
        sm.animDriver.SetBase(PlayerAnimHash.Dash, restart: true);

        originalGravity = sm.defaultGravityScale;   // 读出厂值，不读当前值（见 PlayerStateMachine.defaultGravityScale）
        sm.rb.gravityScale = 0f;

        // 距离和时长都是配置给定的，速度 = 距离 / 时长（见 PlayerStateMachine.dashSpeed）
        dashTimer = 0f;
        dashTotalTime = Mathf.Max(Time.fixedDeltaTime, sm.dashDuration);
        dashFinished = false;
        dashVelocity = Vector2.zero;

        if (overrideStationary)
        {
            // 原地冲刺：一次性，用完即清
            overrideStationary = false;
            overrideDirection = null;
            isStationaryDash = true;
            dashVelocity = Vector2.zero;
            sm.rb.linearVelocity = Vector2.zero;
        }
        else if (overrideDirection.HasValue)
        {
            Vector2 dir = overrideDirection.Value;
            overrideDirection = null;   // 一次性，用完即清

            dashVelocity = dir * sm.dashSpeed;
            sm.rb.linearVelocity = dashVelocity;

            // 朝向跟着冲刺方向走，避免"往左冲却面朝右"
            if (Mathf.Abs(dir.x) > 0.01f)
                sm.playerController.SetFacingDirection(dir.x > 0f ? 1 : -1);
        }
        else
        {
            float direction = sm.playerController.facingDirection;
            dashVelocity = new Vector2(direction * sm.dashSpeed, 0f);
            sm.rb.linearVelocity = dashVelocity;
        }
    }

    public override void Update()
    {
        sm.loadout?.NotifyActionUpdate(ActionType.Dash, Time.deltaTime);
        // 冲刺的计时和结束都在 FixedUpdate 里（物理帧），和画面帧率无关
    }

    public override void FixedUpdate()
    {
        if (!didStartDash) return;   // 被宝石接管：不推进

        // 上一物理帧已经走完：恢复重力，交接给后续状态。
        // 交接时速度和原来一样是冲刺速度，后续状态（下落 / 跑动 / 待机）照旧自己接手。
        if (dashFinished)
        {
            sm.rb.linearVelocity = dashVelocity;
            sm.rb.gravityScale = originalGravity;
            HandleFallback();
            return;
        }

        float dt = Time.fixedDeltaTime;
        float step = Mathf.Min(dt, dashTotalTime - dashTimer);
        dashTimer += step;

        // 匀速；最后一个物理帧按剩余比例缩小速度，保证总距离正好 = 冲刺距离
        sm.rb.linearVelocity = dashVelocity * (step / dt);

        if (dashTimer >= dashTotalTime - 0.00001f) dashFinished = true;
    }

    public override void Exit()
    {
        // 即使被宝石接管，也要通知它收尾（释放无敌、还原图层等）
        sm.loadout?.NotifyActionExit(ActionType.Dash, !didStartDash);

        if (!didStartDash) return;

        sm.dashSkill.StartCooldownIfFirstHit(isFirstHit);

        if (sm.dashTrail != null) sm.dashTrail.emitting = false;
        sm.rb.gravityScale = originalGravity;
    }
}