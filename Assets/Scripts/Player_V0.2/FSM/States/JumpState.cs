using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 跳跃状态。
/// </summary>
public class JumpState : PlayerStateBase
{
    private float originalGravity;
    private bool didStartJump;

    // 这次起跳的数据（见 Enter）
    private float gravity;          // 重力加速度（格/秒²）
    private float jumpStartY;       // 起跳时的高度
    private float minHeight;        // 这次的最低跳跃高度（已乘跳跃力倍率）
    private bool released;          // 已经松过手（只处理第一次松手）

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

        originalGravity = sm.defaultGravityScale;   // 读出厂值，不读当前值（见 PlayerStateMachine.defaultGravityScale）

        // 起跳给满跳速度 = √(2 × 重力 × 满跳高度)，再乘跳跃力倍率（蓄力折损等）。
        // 最低高度跟着倍率的平方缩放，保持"最低 / 满跳"的比例不变
        gravity = Mathf.Abs(Physics2D.gravity.y) * originalGravity;
        float strength = sm.jumpStrength;
        float maxHeight = Mathf.Max(0.01f, sm.maxJumpHeight);
        float launchSpeed = Mathf.Sqrt(2f * gravity * maxHeight) * strength;
        minHeight = Mathf.Min(sm.minJumpHeight, maxHeight) * strength * strength;

        jumpStartY = sm.rb.position.y;
        released = false;
        sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, launchSpeed);
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
        // 变高跳（按高度为界）：第一次松手时，
        //   还没升到最低高度 → 上升速度设成"刚好惯性升到最低高度"，正好停在最低高度（和手速无关）
        //   已经过了最低高度 → 上升速度削到「松手后剩余上升速度」，很快到顶（按得越久跳得越高）
        // 蓄力中也照常生效 —— 跳跃的手感不该因为手上攥着东西就变样。
        float vy = sm.rb.linearVelocity.y;
        if (!released && !sm.playerController.isJumpHeld)
        {
            released = true;
            if (vy > 0f)
            {
                float risen = sm.rb.position.y - jumpStartY;
                float target = risen < minHeight
                    ? Mathf.Sqrt(2f * gravity * (minHeight - risen))
                    : sm.minJumpVelocity;

                if (vy > target)
                {
                    vy = target;
                    sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, vy);
                }
            }
        }

        // 往下掉时乘下落重力倍率
        sm.rb.gravityScale = originalGravity * (vy < 0f ? sm.fallGravityMultiplier : 1f);

        ApplyHorizontalMove();
    }

    public override void Exit()
    {
        sm.loadout?.NotifyActionExit(ActionType.Jump, !didStartJump);

        if (!didStartJump) return;

        sm.rb.gravityScale = originalGravity;
    }
}