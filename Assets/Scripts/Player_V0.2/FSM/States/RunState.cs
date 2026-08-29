using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 跑动状态。
///
/// 【本次改动】后摇期间立刻转回待机。
///
/// 会走到这里，是因为玩家在攻击【之前】就在跑，
/// 攻击结束后又回到了 RunState —— 此时后摇可能还没走完。
/// 让它转回 Idle，而不是原地播跑动动画却不位移（那样看起来像卡住）。
/// </summary>
public class RunState : PlayerStateBase
{
    public RunState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        sm.animDriver.SetBase(PlayerAnimHash.Run);
        sm.jumpCount = 0; // 踩地跑动，刷新跳跃次数
    }

    public override void Update()
    {
        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
            return;
        }

        // 后摇中不许跑 —— 转回待机，避免"播着跑动动画却原地不动"
        if (IsMovementLockedByAttack)
        {
            sm.ChangeState(sm.idleState);
            return;
        }

        float moveDir = sm.playerController.moveInput.x;

        // 朝向翻转是视觉逻辑，留在渲染帧更跟手
        UpdateFacing(moveDir);

        if (Mathf.Abs(moveDir) < 0.1f)
        {
            sm.ChangeState(sm.idleState);
        }
    }

    public override void FixedUpdate()
    {
        // 双保险：即使这一帧还没来得及切回 Idle，也不写移动速度
        if (IsMovementLockedByAttack) return;

        ApplyHorizontalMove();
    }
}