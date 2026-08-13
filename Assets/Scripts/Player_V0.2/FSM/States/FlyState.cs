using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 飞行状态。
///
/// 【批次E 改动】八向移动的速度写入挪到 FixedUpdate。
/// 耗蓝、取消飞行、朝向留在 Update。
///
/// 【释放锁机制】requireFlyRelease 用来模拟旧输入系统的 GetKeyDown：
/// 一切入飞行就上锁，要求玩家先松开 Q 键，
/// 否则从悬停蓄力进来的那一瞬间会立刻被判定为「再次按下 → 取消飞行」。
/// </summary>
public class FlyState : PlayerStateBase
{
    private float originalGravity;
    private float manaAccumulator;
    private bool requireFlyRelease = false;

    public FlyState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        originalGravity = sm.rb.gravityScale;
        sm.rb.gravityScale = 0f;
        sm.rb.linearVelocity = Vector2.zero;
        manaAccumulator = 0f;
        sm.anim.Play(PlayerAnimHash.Fly);

        requireFlyRelease = sm.playerController.isFlyHeld;
    }

    public override void Update()
    {
        // ---- 耗蓝 ----
        manaAccumulator += sm.flyManaCostPerSecond * Time.deltaTime;
        if (manaAccumulator >= 1f)
        {
            int cost = Mathf.FloorToInt(manaAccumulator);
            manaAccumulator -= cost;

            if (!sm.playerState.health.ConsumeMP(cost))
            {
                sm.ChangeState(sm.fallState);
                return;
            }
        }

        // ---- 主动取消飞行 ----
        if (requireFlyRelease)
        {
            if (!sm.playerController.isFlyHeld) requireFlyRelease = false;
        }
        else if (sm.playerController.isFlyHeld)
        {
            sm.ChangeState(sm.fallState);
            sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, sm.flyCancelJumpForce);
            return;
        }

        UpdateFacing(sm.playerController.moveInput.x);
    }

    public override void FixedUpdate()
    {
        // 八向移动。飞行时 X/Y 都由输入完全接管，不保留原速度
        Vector2 moveDir = sm.playerController.moveInput.normalized;
        sm.rb.linearVelocity = moveDir * sm.flySpeed;
    }

    public override void Exit()
    {
        sm.rb.gravityScale = originalGravity;
    }
}
