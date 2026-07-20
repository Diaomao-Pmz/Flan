using UnityEngine;

public class FallState : IState
{
    private PlayerStateMachine sm;

    private float qHoldTimer = 0f;
    private float originalGravity;
    private bool isHovering = false;
    private bool requireQRelease = false;

    public FallState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        Debug.Log("进入了：下落状态");
        sm.anim.Play("Flandre_Jump_Fall");

        qHoldTimer = 0f;
        isHovering = false;
        originalGravity = sm.rb.gravityScale;

        // 【修改】
        requireQRelease = sm.playerController.isFlyHeld;
    }

    public void Update()
    {
        if (requireQRelease)
        {
            if (!sm.playerController.isFlyHeld) requireQRelease = false;
        }

        // 【修改】
        if (!requireQRelease && sm.playerController.isFlyHeld)
        {
            if (!isHovering)
            {
                isHovering = true;
                sm.rb.gravityScale = 0f;
                sm.rb.linearVelocity = Vector2.zero;
            }

            qHoldTimer += Time.deltaTime;

            if (qHoldTimer >= sm.hoverChargeTime)
            {
                sm.ChangeState(sm.flyState);
                return;
            }
        }
        else
        {
            if (isHovering)
            {
                isHovering = false;
                sm.rb.gravityScale = originalGravity;
                qHoldTimer = 0f;
            }
        }

        if (isHovering) return;

        // 【修改】：读取虚拟手柄的移动信号
        float moveDir = sm.playerController.moveInput.x;
        sm.rb.linearVelocity = new Vector2(moveDir * sm.moveSpeed, sm.rb.linearVelocity.y);

        if (moveDir < 0) sm.playerController.SetFacingDirection(-1);
        else if (moveDir > 0) sm.playerController.SetFacingDirection(1);

        if (sm.IsGrounded())
        {
            if (Mathf.Abs(moveDir) > 0.1f) sm.ChangeState(sm.runState);
            else sm.ChangeState(sm.idleState);
        }
    }

    public void Exit()
    {
        sm.rb.gravityScale = originalGravity;
    }
}