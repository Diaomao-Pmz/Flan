using UnityEngine;

public class IdleState : IState
{
    private PlayerStateMachine sm;

    public IdleState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        Debug.Log("进入了：待机状态");
        // 落地静止，保留 Y 轴物理速度（防止下落瞬间微弱回弹Bug）
        sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);

        sm.anim.Play("Flandre_Idle", 0, 0f);
    }

    public void Update()
    {
        // 1. 离地检测：如果没有踩着地，自动切入下落卡带
        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
            return;
        }

        // 2. 【核心新增】：上下文按键检测（情景：站立中）
        // 站立时按住 Ctrl 键，顺滑切入下蹲状态
        if (sm.playerController.isCrouchHeld)
        {
            sm.ChangeState(sm.crouchState);
            return; // 成功拦截，不走下方的跑步逻辑
        }

        // 3. 移动检测：改用新输入系统的 moveInput 数据！彻底消灭 GetAxisRaw 屎山
        float currentInputX = sm.playerController.moveInput.x;
        if (Mathf.Abs(currentInputX) > 0.1f)
        {
            sm.ChangeState(sm.runState);
        }
    }

    public void Exit()
    {
        Debug.Log("离开了：待机状态");
    }
}