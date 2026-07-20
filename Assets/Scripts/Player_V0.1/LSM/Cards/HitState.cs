using UnityEngine;

public class HitState : IState
{
    private PlayerStateMachine sm;
    private float hitStunTimer;
    private Vector2 hitDirection;

    public HitState(PlayerStateMachine stateMachine)
    {
        this.sm = stateMachine;
    }

    public void SetKnockbackForce(Vector2 forceDir)
    {
        this.hitDirection = forceDir;
    }

    public void Enter()
    {
        sm.anim.Play("Flandre_Hit");

        var inputBuffer = sm.GetComponent<ComboInputBuffer>();
        if (inputBuffer != null)
        {
            inputBuffer.ResetCombo();
        }

        hitStunTimer = sm.hitStunDuration;

        Vector2 finalKnockback = sm.hitKnockbackForce;

        // ==========================================
        // 【核心修复】：防止 Mathf.Sign(0) 永远向右弹的 Bug
        // ==========================================
        if (Mathf.Abs(hitDirection.x) > 0.01f)
        {
            // 1. 如果子弹老老实实传了相对方向（比如 FormationBullet），听子弹的！
            finalKnockback.x *= Mathf.Sign(hitDirection.x);
        }
        else
        {
            // 2. 如果子弹偷懒传了 0 (Vector2.zero)，强行根据玩家当前朝向向后击飞！
            // 读取 PlayerController 里的 facingDirection (1为右，-1为左)
            int facing = sm.playerController.facingDirection;
            finalKnockback.x *= -facing;
        }

        if (sm.playerController.rb != null)
        {
            sm.playerController.rb.linearVelocity = Vector2.zero;

            if (sm.IsGrounded())
            {
                sm.transform.position += new Vector3(0, 0.1f, 0);
            }
                
            sm.playerController.rb.linearVelocity = finalKnockback;
        }   
    }

    public void Update()
    {
        hitStunTimer -= Time.deltaTime;

        //受击允许a,d移动
        /*
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(h) > 0.1f)
        {
            // 强行覆盖 X 轴速度，但保留 Y 轴的击飞抛物线
            // 你可以把 stateMachine.moveSpeed 乘以一个系数（如 0.5f），来做成“微弱的空中控制”
            stateMachine.playerController.rb.linearVelocity = new Vector2(
                h * stateMachine.moveSpeed,
                stateMachine.playerController.rb.linearVelocity.y
            );
        }*/

        if (hitStunTimer <= 0f)
        {
            if (sm.IsGrounded())
                sm.ChangeState(sm.idleState);
            else
                sm.ChangeState(sm.fallState);
        }
    }

    public void Exit()
    {
        hitDirection = Vector2.zero;
    }
}