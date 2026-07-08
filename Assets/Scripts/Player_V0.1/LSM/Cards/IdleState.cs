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
        sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);

        sm.anim.Play("Flandre_Idle", 0, 0f);
    }

    public void Update()
    {
        //idlestate
        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
            return;
        }

        if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f)
        {
            sm.ChangeState(sm.runState);
        }

    }

    public void Exit()
    {
        Debug.Log("离开了：待机状态");
    }
}