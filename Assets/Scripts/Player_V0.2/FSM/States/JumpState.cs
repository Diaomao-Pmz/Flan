using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 跳跃状态。
/// </summary>
public class JumpState : PlayerStateBase
{
    private float originalGravity;
    private bool didStartJump;

    public JumpState(PlayerStateMachine stateMachine) : base(stateMachine) { }

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
    }

    public override void Update()
    {
        sm.loadout?.NotifyActionUpdate(ActionType.Jump, Time.deltaTime);

        float vy = sm.rb.linearVelocity.y;

        // 动画切换是视觉逻辑，留在渲染帧
        if (vy > 0.5f) sm.animDriver.SetBase(PlayerAnimHash.JumpStart);
        else if (vy >= -0.5f) sm.animDriver.SetBase(PlayerAnimHash.JumpApex);
        else sm.animDriver.SetBase(PlayerAnimHash.JumpFall);

        UpdateFacing(sm.playerController.moveInput.x);

        if (vy <= 0f && sm.IsGrounded())
        {
            HandleLanding();
        }
    }

    public override void FixedUpdate()
    {
        sm.rb.gravityScale = originalGravity;

        // 变高跳：松开跳跃键就把上升速度削到最低值。
        // 蓄力中也照常生效 —— 跳跃的手感不该因为手上攥着东西就变样。
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

        sm.rb.gravityScale = originalGravity;
    }
}