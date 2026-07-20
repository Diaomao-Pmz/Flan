using UnityEngine;
using Flandre.CombatSystem;

public class ChargeState : IState
{
    private PlayerStateMachine sm;
    private float originalGravity;

    // 用于缓存本次蓄力到底在监听哪个按键
    private InputCmd currentChargingCmd;

    public ChargeState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        Debug.Log("进入了：架势蓄力状态");
        sm.anim.Play("Flandre_Charge");
        originalGravity = sm.rb.gravityScale;

        // 【核心修复】：在进入状态的瞬间，拍下“快照”。
        // 只查一次案，查完就把结果记在自己身上，不再依赖外部的 currentNode！
        ComboNode lastNode = sm.GetComponent<ComboInputBuffer>().currentNode;

        // 给个默认值兜底
        currentChargingCmd = InputCmd.MainAttack;

        if (lastNode != null)
        {
            if (lastNode.inputSequence.Contains(InputCmd.MainAttack))
                currentChargingCmd = InputCmd.MainAttack;
            else if (lastNode.inputSequence.Contains(InputCmd.SubAttack))
                currentChargingCmd = InputCmd.SubAttack;
        }
    }

    public void Update()
    {
        bool isCurrentButtonHeld = false;

        // 1. 每帧只向 Controller 询问底层的物理按压状态，彻底无视连招系统是否清空了 Node
        if (currentChargingCmd == InputCmd.MainAttack)
            isCurrentButtonHeld = sm.playerController.isMainAttackHeld;
        else if (currentChargingCmd == InputCmd.SubAttack)
            isCurrentButtonHeld = sm.playerController.isSubAttackHeld;

        // 2. 核心流转：如果玩家真的松手了
        if (!isCurrentButtonHeld)
        {
            if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
            else sm.ChangeState(sm.idleState);
            return;
        }

        // 3. 蓄力时的物理表现 (维持你原来写的微速移动逻辑)
        if (sm.IsGrounded())
        {
            sm.rb.gravityScale = originalGravity;
            float moveDir = sm.playerController.moveInput.x;
            sm.rb.linearVelocity = new Vector2(moveDir * sm.moveSpeed * 0.2f, sm.rb.linearVelocity.y);

            if (moveDir < 0) sm.playerController.SetFacingDirection(-1);
            else if (moveDir > 0) sm.playerController.SetFacingDirection(1);
        }
        else
        {
            sm.rb.gravityScale = 0f;
            sm.rb.linearVelocity = Vector2.zero;
        }
    }

    public void Exit()
    {
        Debug.Log("退出了：架势蓄力状态");
        sm.rb.gravityScale = originalGravity;
    }
}