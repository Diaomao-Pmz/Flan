using UnityEngine;
using Flandre.CombatSystem;
/// <summary>
/// 跑动状态。
///
/// 【批次E 改动】速度写入从 Update 挪到 FixedUpdate。
/// 判断（离地、松开方向键）与朝向翻转留在 Update。
/// </summary>
public class RunState : PlayerStateBase
{
    public RunState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        sm.anim.Play(PlayerAnimHash.Run);
        sm.jumpCount = 0; // 踩地跑动，刷新跳跃次数
    }

    public override void Update()
    {
        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
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
        ApplyHorizontalMove();
    }
}
