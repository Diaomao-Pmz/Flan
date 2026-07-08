using UnityEngine;
using Flandre.CombatSystem;

public class JumpState : IState
{
    private PlayerStateMachine sm;

    private readonly int playerLayer = LayerMask.NameToLayer("Player");
    private readonly int invincibleLayer = LayerMask.NameToLayer("Invincible");

    public JumpState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        sm.playerController.loadoutManager.ExecuteActionModifier(ActionType.Jump);

        Debug.Log("进入了：跳跃状态，当前跳跃次数：" + sm.jumpCount);

        // 1. 每次进入跳跃，计步器 +1
        sm.jumpCount++;

        if (sm.isJumpRelay)
        {
            sm.jumpAnchorPos = sm.transform.position; // 记录锚点位置
            sm.hasJumpAnchor = true;                  
            sm.gameObject.layer = invincibleLayer;    // 开启第一段跳跃无敌！

            if (sm.anchorPrefab != null)
            {
                sm.activeJumpAnchor = GameObject.Instantiate(sm.anchorPrefab, sm.jumpAnchorPos, Quaternion.identity);
            }
        }

        // 2. 播放起跳动画
        sm.anim.Play("Flandre_Jump_Start");

        // 3. 物理爆发：强制清零Y轴速度，确保二段跳也能跳得一样高
        sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, 0f);
        sm.rb.AddForce(Vector2.up * sm.jumpForce, ForceMode2D.Impulse);
    }

    public void Update()
    {
        float vy = sm.rb.linearVelocity.y;

        // ==========================================
        // 【新增核心逻辑】：长短按跳跃高度控制 (Velocity Cut)
        // 如果玩家没有按住跳跃键，且角色还在往上飞，且速度大于我们允许的最小值
        if (!Input.GetKey(KeyCode.Space) && vy > sm.minJumpVelocity)
        {
            // 强制截断Y轴向上的速度，实现“提前坠落”
            sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, sm.minJumpVelocity);
            vy = sm.minJumpVelocity; // 同步更新局部变量，保证下方动画状态机能正确读取最新的速度
        }

        // ==========================================

        if (vy > 0.5f) sm.anim.Play("Flandre_Jump_Start"); // 向上飞
        else if (vy >= -0.5f) sm.anim.Play("Flandre_Jump_Apex");  // 顶点滞空
        else sm.anim.Play("Flandre_Jump_Fall");  // 下坠

        // 空中左右移动逻辑
        float moveDir = 0f;
        if (Input.GetKey(KeyCode.A)) moveDir = -1f;
        if (Input.GetKey(KeyCode.D)) moveDir = 1f;

        sm.rb.linearVelocity = new Vector2(moveDir * sm.moveSpeed, sm.rb.linearVelocity.y);

        // 翻转逻辑
        if (moveDir < 0) sm.GetComponent<SpriteRenderer>().flipX = true;
        else if (moveDir > 0) sm.GetComponent<SpriteRenderer>().flipX = false;

        // 落地检测 (只有往下掉且踩地时才算落地)
        if (vy <= 0f && sm.IsGrounded())
        {
            if (moveDir != 0) sm.ChangeState(sm.runState);
            else sm.ChangeState(sm.idleState);
        }
    }

    public void Exit()
    {
        //移除无敌
        if (sm.isJumpRelay) sm.gameObject.layer = playerLayer;

        Debug.Log("离开了：跳跃状态");
    }
}