using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 跳跃状态。
///
/// 【批次E 改动】
///   空中水平控制 与 变高跳的速度削减 → FixedUpdate
///   动画切换、落地判断、悬停蓄力计时 → 留在 Update
///
/// 【刻意保留在 Enter 的 AddForce】
/// Unity 会把 Update/Enter 里调的 AddForce 攒到下一个物理步再结算，
/// 本来就是固定步长的。挪进 FixedUpdate 反而会引入最多 20ms 输入延迟。
/// </summary>
public class JumpState : PlayerStateBase
{
    private readonly HoverChargeHandler hover;

    private float originalGravity;
    private bool didStartJump;

    public JumpState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        hover = new HoverChargeHandler(stateMachine);
    }

    public override void Enter()
    {
        didStartJump = false;

        // 跳跃次数闸门已由 PlayerCommandRouter 前置校验
        bool isFirstUse = (sm.jumpCount == 0);
        sm.jumpCount++;

        GemActionResult gemResult = sm.loadout != null
            ? sm.loadout.NotifyActionEnter(ActionType.Jump, isFirstUse)
            : GemActionResult.Normal;

        if (gemResult != GemActionResult.Normal)
        {
            // 例如 Relay 的第二跳：已经传送过去了，不需要再给一次向上冲力
            if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
            else sm.ChangeState(sm.idleState);
            return;
        }

        didStartJump = true;

        sm.animDriver.SetBase(PlayerAnimHash.JumpStart);

        sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, 0f);
        sm.rb.AddForce(Vector2.up * sm.jumpForce, ForceMode2D.Impulse);

        originalGravity = sm.defaultGravityScale;   // 读出厂值，不读当前值（见 PlayerStateMachine.defaultGravityScale）
        hover.OnEnter(originalGravity);
    }

    public override void Update()
    {
        sm.loadout?.NotifyActionUpdate(ActionType.Jump, Time.deltaTime);

        // ---- 悬停蓄力（与 FallState 共用同一份实现）----
        var hoverResult = hover.Tick(Time.deltaTime);

        if (hoverResult == HoverChargeHandler.Result.EnterFly)
        {
            sm.ChangeState(sm.flyState);
            return;
        }
        if (hoverResult == HoverChargeHandler.Result.Hovering) return;

        float vy = sm.rb.linearVelocity.y;

        // 动画切换是视觉逻辑，留在渲染帧
        if (vy > 0.5f) sm.animDriver.SetBase(PlayerAnimHash.JumpStart);
        else if (vy >= -0.5f) sm.animDriver.SetBase(PlayerAnimHash.JumpApex);
        else sm.animDriver.SetBase(PlayerAnimHash.JumpFall);

        // 空中蓄力悬停时连朝向也锁住 —— 否则「方向键无响应」只兑现了一半：
        // 人不动，却还能被方向键掰着左右转身，看起来像在抽搐。
        if (!IsAirChargePinned) UpdateFacing(sm.playerController.moveInput.x);

        if (vy <= 0f && sm.IsGrounded())
        {
            HandleLanding();
        }
    }

    public override void FixedUpdate()
    {
        // 悬停中不写速度，否则会把 HoverChargeHandler 钉住的角色重新推走
        if (hover.IsHovering) return;

        // 空中蓄力到顶点/下落段：钉在原地悬停。
        // 想调整位置只能花一次冲刺（Shift 朝鼠标落点冲）——
        // 这让空中蓄力成为一次高风险承诺，而不是可以自由飘着蓄。
        //
        // 【上升段刻意不钉】否则起跳冲量会在生效前被抹掉，角色卡死在起跳点。
        // 详见 PlayerStateBase.IsAirChargePinned。
        if (IsAirChargePinned)
        {
            PinInAirWhileCharging();
            return;
        }

        // 蓄力一结束就立刻把重力还回来。
        // 【为什么每帧都写】蓄力可能在空中任意一帧结束，而 Exit 要等状态切换才跑 ——
        // 只在 Exit 还原的话，松手后到落地前这段时间角色会一直浮着。
        sm.rb.gravityScale = originalGravity;

        // 变高跳：松开跳跃键就把上升速度削到最低值。
        // 蓄力中也照常生效 —— 跳跃的手感不该因为手上攥着东西就变样。
        float vy = sm.rb.linearVelocity.y;
        if (!sm.playerController.isJumpHeld && vy > sm.minJumpVelocity)
        {
            sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, sm.minJumpVelocity);
        }

        // 【只有空中起手的蓄力才锁方向键】
        //
        // 地面起手蓄力再跳起来时，玩家并没有做出"空中蓄力"那个承诺 ——
        // 那只是一次普通的折损跳跃，该有正常的空中操控。
        // 以前用的粗判据（在蓄力 且 在空中）会把这种情况也锁死，
        // 表现成"蓄力时跳起来只能直上直下"。
        if (IsAirChargePinned) return;

        ApplyHorizontalMove();
    }

    public override void Exit()
    {
        sm.loadout?.NotifyActionExit(ActionType.Jump, !didStartJump);

        if (!didStartJump) return;

        hover.OnExit();
        sm.rb.gravityScale = originalGravity;
    }
}