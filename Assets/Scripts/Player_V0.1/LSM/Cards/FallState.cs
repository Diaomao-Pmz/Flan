using UnityEngine;

public class FallState : IState
{
    private PlayerStateMachine sm;

    public FallState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        Debug.Log("进入了：下落状态");
        // 直接播放下坠动画，没有任何向上的加力代码！
        sm.anim.Play("Flandre_Jump_Fall");
    }

    public void Update()
    {
        // 1. 空中左右移动逻辑
        float moveDir = 0f;
        if (Input.GetKey(KeyCode.A)) moveDir = -1f;
        if (Input.GetKey(KeyCode.D)) moveDir = 1f;

        sm.rb.linearVelocity = new Vector2(moveDir * sm.moveSpeed, sm.rb.linearVelocity.y);

        if (moveDir < 0) sm.GetComponent<SpriteRenderer>().flipX = true;
        else if (moveDir > 0) sm.GetComponent<SpriteRenderer>().flipX = false;
        //fallstate
        /*if (Input.GetKeyDown(KeyCode.Space))
        {
            if (sm.isJumpRelay && sm.hasJumpAnchor)
            {
                sm.transform.position = sm.jumpAnchorPos;
                sm.rb.linearVelocity = Vector2.zero;
                sm.hasJumpAnchor = false;            // 传送完毕，消耗掉锚点
                sm.ChangeState(sm.fallState);
                return;
            }
            else if (sm.jumpCount < sm.maxJumps)
            {
                sm.ChangeState(sm.jumpState);
                return;
            }
        }

        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            sm.ChangeState(sm.dashState);
            return;
        }*/

        // 3. 落地检测
        if (sm.IsGrounded())
{
    if (moveDir != 0) sm.ChangeState(sm.runState);
    else sm.ChangeState(sm.idleState);
}



}

public void Exit()
{
}
}