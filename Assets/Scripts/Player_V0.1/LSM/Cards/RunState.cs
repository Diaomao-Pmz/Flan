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
        Debug.Log("执行跑动动画");
        sm.jumpCount = 0;
    }

    public void Update()
    {
        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
            return;
        }

        // 2. 获取极其丝滑的方向输入 (-1表示纯左，1表示纯右，0表示没按)
        float moveDir = Input.GetAxisRaw("Horizontal");

        // 3. 赋予物理速度
        sm.rb.linearVelocity = new Vector2(moveDir * sm.moveSpeed, sm.rb.linearVelocity.y);

        // 4. 控制角色翻转（极其简洁的写法）
        if (moveDir < 0) sm.GetComponent<SpriteRenderer>().flipX = true;
        else if (moveDir > 0) sm.GetComponent<SpriteRenderer>().flipX = false;

        // 5. 核心流转：如果玩家松开了方向键（摇杆回中）
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