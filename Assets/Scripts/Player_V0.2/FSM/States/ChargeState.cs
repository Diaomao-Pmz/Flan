using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 架势蓄力状态。
///
/// 【批次E 改动】地面微移与空中锁死挪到 FixedUpdate。
/// 松手判定、朝向留在 Update。
///
/// 【快照机制】进入状态的瞬间拍下"本次在蓄哪个键"的快照，
/// 之后每帧只向 Controller 问物理按压状态，不再依赖 currentNode ——
/// 因为连招系统可能在蓄力途中把 currentNode 清空。
/// </summary>
public class ChargeState : PlayerStateBase
{
    private const float ChargeMoveSpeedMultiplier = 0.2f;

    private float originalGravity;
    private InputCmd currentChargingCmd;

    private ComboInputBuffer buffer;
    private ComboInputBuffer Buffer
        => buffer != null ? buffer : (buffer = sm.GetComponent<ComboInputBuffer>());

    public ChargeState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        sm.anim.Play(PlayerAnimHash.Charge);
        originalGravity = sm.rb.gravityScale;

        ComboNode lastNode = Buffer.currentNode;

        currentChargingCmd = InputCmd.MainAttack;   // 默认值兜底

        if (lastNode != null)
        {
            if (lastNode.inputSequence.Contains(InputCmd.MainAttack))
                currentChargingCmd = InputCmd.MainAttack;
            else if (lastNode.inputSequence.Contains(InputCmd.SubAttack))
                currentChargingCmd = InputCmd.SubAttack;
        }
    }

    public override void Update()
    {
        bool isCurrentButtonHeld =
            (currentChargingCmd == InputCmd.MainAttack)
                ? sm.playerController.isMainAttackHeld
                : sm.playerController.isSubAttackHeld;

        // 松手 → 结束蓄力架势
        if (!isCurrentButtonHeld)
        {
            if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
            else sm.ChangeState(sm.idleState);
            return;
        }

        if (sm.IsGrounded())
        {
            UpdateFacing(sm.playerController.moveInput.x);
        }
    }

    public override void FixedUpdate()
    {
        if (sm.IsGrounded())
        {
            sm.rb.gravityScale = originalGravity;
            ApplyHorizontalMove(ChargeMoveSpeedMultiplier);
        }
        else
        {
            // 空中蓄力：反重力钉在原地
            sm.rb.gravityScale = 0f;
            sm.rb.linearVelocity = Vector2.zero;
        }
    }

    public override void Exit()
    {
        sm.rb.gravityScale = originalGravity;
    }
}
