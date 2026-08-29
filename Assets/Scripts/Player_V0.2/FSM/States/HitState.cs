using UnityEngine;
using Flandre.CombatSystem;
/// <summary>
/// 受击状态。
///
/// 【批次D 改动】继承 PlayerStateBase，缓存 ComboInputBuffer 引用，
/// 删掉了那段早已注释掉的旧输入系统代码（Input.GetAxisRaw）。
/// 行为完全不变。
/// </summary>
public class HitState : PlayerStateBase
{
    private float hitStunTimer;
    private Vector2 hitDirection;

    private ComboInputBuffer buffer;
    private ComboInputBuffer Buffer
        => buffer != null ? buffer : (buffer = sm.GetComponent<ComboInputBuffer>());

    public HitState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public void SetKnockbackForce(Vector2 forceDir)
    {
        hitDirection = forceDir;
    }

    public override void Enter()
    {
        sm.animDriver.SetBase(PlayerAnimHash.Hit);

        Buffer?.ResetCombo();

        hitStunTimer = sm.hitStunDuration;

        Vector2 finalKnockback = sm.hitKnockbackForce;

        // 防 Mathf.Sign(0) 永远向右弹的 Bug
        if (Mathf.Abs(hitDirection.x) > 0.01f)
        {
            // 攻击方老实传了相对方向 → 听它的
            finalKnockback.x *= Mathf.Sign(hitDirection.x);
        }
        else
        {
            // 攻击方传了 Vector2.zero → 按玩家当前朝向向后击飞
            finalKnockback.x *= -sm.playerController.facingDirection;
        }

        if (sm.rb != null)
        {
            sm.rb.linearVelocity = Vector2.zero;

            // 贴地时微微抬起，避免击飞被地面摩擦吃掉
            if (sm.IsGrounded())
            {
                sm.transform.position += new Vector3(0, 0.1f, 0);
            }

            sm.rb.linearVelocity = finalKnockback;
        }
    }

    public override void Update()
    {
        hitStunTimer -= Time.deltaTime;

        if (hitStunTimer <= 0f)
        {
            if (sm.IsGrounded()) sm.ChangeState(sm.idleState);
            else sm.ChangeState(sm.fallState);
        }
    }

    public override void Exit()
    {
        hitDirection = Vector2.zero;
    }
}