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

        sm.anim.Play("Flandre_Jump_Start");

        sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, 0f);
        sm.rb.AddForce(Vector2.up * sm.jumpForce, ForceMode2D.Impulse);

        originalGravity = sm.rb.gravityScale;
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
        if (vy > 0.5f) sm.anim.Play(PlayerAnimHash.JumpStart);
        else if (vy >= -0.5f) sm.anim.Play(PlayerAnimHash.JumpApex);
        else sm.anim.Play(PlayerAnimHash.JumpFall);

        UpdateFacing(sm.playerController.moveInput.x);

        if (vy <= 0f && sm.IsGrounded())
        {
            HandleLanding();
        }
    }

    public override void FixedUpdate()
    {
        // 悬停中不写速度，否则会把 HoverChargeHandler 钉住的角色重新推走
        if (hover.IsHovering) return;

        // 变高跳：松开跳跃键就把上升速度削到最低值
        float vy = sm.rb.linearVelocity.y;
        if (!sm.playerController.isJumpHeld && vy > sm.minJumpVelocity)
        {
            sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, sm.minJumpVelocity);
        }

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
