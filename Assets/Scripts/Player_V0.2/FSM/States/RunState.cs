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

        // ==========================================================
        // 【把"跳跃次数只能在地面重置"这个不变量写出来】
        //
        // 原先是无条件 jumpCount = 0，靠"RunState 应该只在地面进入"
        // 这个【假设】隐式成立。而假设是会被打破的 ——
        // OnAttackAnimationEnd 漏查地面时，空中招式演完就会切进这里，
        // 二段跳于是在半空被还了回来（那个根因已经在状态机那边治了）。
        //
        // 比喻：原先是"进了这扇门就发一张新门票"，全靠"这扇门只开在一楼"
        //       这个默认前提。现在改成进门先看一眼自己在几楼。
        //
        // 这一句是【加固】不是治根：即使以后又有别的路径错误地在空中
        // 切进 RunState，跳跃次数也不会再凭空多出来。
        // ==========================================================
        if (sm.IsGrounded()) sm.jumpCount = 0;
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