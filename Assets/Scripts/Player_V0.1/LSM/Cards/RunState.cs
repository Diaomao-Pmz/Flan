using UnityEngine;

public class RunState : IState
{
    private PlayerStateMachine sm;

    public RunState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        Debug.Log("进入了：跑动状态");
        sm.anim.Play("Flandre_Run");
        sm.jumpCount = 0; // 踩地跑动，刷新跳跃次数
    }

    public void Update()
    {
        // 1. 离地检测：跑着跑着踩空了（比如从平台边缘掉落），切入下落
        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
            return;
        }

        // 2. 【核心新增】：上下文按键检测（情景：移动中）
        // 跑步时如果按下了 Ctrl 键，并且滑铲技能 CD 好了，瞬间突进滑铲！
        if (sm.playerController.isCrouchHeld && sm.slideSkill.CanExecute())
        {
            sm.ChangeState(sm.slideState);
            return; // 成功拦截，跳过下方的常规跑步位移
        }

        // 3. 从新输入系统安全读取极其丝滑的 X 轴方向输入 (-1左，1右，0没按)
        float moveDir = sm.playerController.moveInput.x;

        // 4. 赋予真实的物理速度
        sm.rb.linearVelocity = new Vector2(moveDir * sm.moveSpeed, sm.rb.linearVelocity.y);

        // 5. 翻转角色朝向并同步给控制器 (通过 facingDirection 让战斗判定框知道往哪打)
        if (moveDir < 0) sm.playerController.SetFacingDirection(-1);
        else if (moveDir > 0) sm.playerController.SetFacingDirection(1);

        // 6. 核心流转：如果玩家松开了方向键（按键归零/摇杆回中），切回待机
        if (Mathf.Abs(moveDir) < 0.1f)
        {
            sm.ChangeState(sm.idleState);
        }
    }

    public void Exit()
    {
        Debug.Log("离开了：跑动状态");
    }
}