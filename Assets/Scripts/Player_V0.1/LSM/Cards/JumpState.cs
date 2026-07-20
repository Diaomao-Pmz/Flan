using UnityEngine;
using Flandre.CombatSystem;

public class JumpState : IState
{
    private PlayerStateMachine sm;

    private readonly int playerLayer = LayerMask.NameToLayer("Player");
    private readonly int invincibleLayer = LayerMask.NameToLayer("Invincible");

    private float qHoldTimer = 0f;
    private float originalGravity;
    private bool isHovering = false;
    private bool requireQRelease = false;

    public JumpState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        sm.playerController.loadoutManager.ExecuteActionModifier(ActionType.Jump);
        sm.jumpCount++;

        if (sm.isJumpRelay)
        {
            sm.jumpAnchorPos = sm.transform.position;
            sm.hasJumpAnchor = true;
            sm.gameObject.layer = invincibleLayer;

            if (sm.anchorPrefab != null)
            {
                sm.activeJumpAnchor = GameObject.Instantiate(sm.anchorPrefab, sm.jumpAnchorPos, Quaternion.identity);
            }
        }

        sm.anim.Play("Flandre_Jump_Start");

        sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, 0f);
        sm.rb.AddForce(Vector2.up * sm.jumpForce, ForceMode2D.Impulse);

        qHoldTimer = 0f;
        isHovering = false;
        originalGravity = sm.rb.gravityScale;

        // 【修改】：向虚拟手柄读取 Fly(Q) 键状态
        requireQRelease = sm.playerController.isFlyHeld;
    }

    public void Update()
    {
        if (requireQRelease)
        {
            if (!sm.playerController.isFlyHeld) requireQRelease = false;
        }

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

        float vy = sm.rb.linearVelocity.y;

        // 【修改】：读取虚拟手柄的 isJumpHeld，实现按键长短决定跳跃高度
        if (!sm.playerController.isJumpHeld && vy > sm.minJumpVelocity)
        {
            sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, sm.minJumpVelocity);
            vy = sm.minJumpVelocity;
        }

        if (vy > 0.5f) sm.anim.Play("Flandre_Jump_Start");
        else if (vy >= -0.5f) sm.anim.Play("Flandre_Jump_Apex");
        else sm.anim.Play("Flandre_Jump_Fall");

        // 【修改】：读取虚拟手柄的 moveInput.x，替代 Input.GetKey(A/D)
        float moveDir = sm.playerController.moveInput.x;
        sm.rb.linearVelocity = new Vector2(moveDir * sm.moveSpeed, sm.rb.linearVelocity.y);

        if (moveDir < 0) sm.playerController.SetFacingDirection(-1);
        else if (moveDir > 0) sm.playerController.SetFacingDirection(1);

        if (vy <= 0f && sm.IsGrounded())
        {
            if (Mathf.Abs(moveDir) > 0.1f) sm.ChangeState(sm.runState);
            else sm.ChangeState(sm.idleState);
        }
    }

    public void Exit()
    {
        if (sm.isJumpRelay) sm.gameObject.layer = playerLayer;
        sm.rb.gravityScale = originalGravity;
    }
}