//有动画后取消注释 line18

using UnityEngine;

public class CrouchState : IState
{
    private PlayerStateMachine sm;

    public CrouchState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        Debug.Log("进入了：蹲下状态");
        // 修改这里！加上 0 和 0f，意思是：强制在第0层，从第0秒立刻重置播放！
        sm.anim.Play("Flandre_Crouch");

        // 缩小碰撞体，贴合地面
        sm.SetColliderHeight(true);
    }

    public void Update()
    {
        // 1. 核心判定：什么时候才能站起来？
        // 条件必须同时满足：【没有按住 Ctrl 键】 并且 【头顶没有任何障碍物】
        if (!Input.GetKey(KeyCode.LeftControl) && sm.CanStand())
        {
            // 极其注重手感的细节：站起来的瞬间，如果玩家正按着方向键，直接切到跑动状态，无缝衔接！
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D))
            {
                sm.ChangeState(sm.runState);
            }
            else
            {
                sm.ChangeState(sm.idleState);
            }
            return; // 必须 return，打断后续的蹲下代码
        }

        // ==========================================
        // 只要代码能走到这里，说明玩家要么【还在按Ctrl】，要么【头顶有墙被迫蹲着】
        // 2. 蹲行逻辑 (无需按住 Ctrl，只要在蹲下状态就能走！)
        // ==========================================

        float moveDir = 0f;
        if (Input.GetKey(KeyCode.A)) moveDir = -1f;
        if (Input.GetKey(KeyCode.D)) moveDir = 1f;

        // 蹲下的移动速度通常比跑步慢，这里默认取跑速的一半（你也可以在状态机里单加一个 crouchSpeed 变量）
        float crouchSpeed = sm.moveSpeed * 0.5f;
        sm.rb.linearVelocity = new Vector2(moveDir * crouchSpeed, sm.rb.linearVelocity.y);

        // 控制翻转
        if (moveDir < 0) sm.GetComponent<SpriteRenderer>().flipX = true;
        else if (moveDir > 0) sm.GetComponent<SpriteRenderer>().flipX = false;

        // 3. 悬崖防掉落 (蹲着走到悬崖边掉下去，也要切到 FallState)
        if (!sm.IsGrounded())
        {
            sm.ChangeState(sm.fallState);
        }
    }

    public void Exit()
    {
        Debug.Log("离开了：蹲下状态");
        // 恢复原始碰撞体大小
        sm.SetColliderHeight(false);
    }
}