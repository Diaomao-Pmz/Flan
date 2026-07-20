using UnityEngine;

public class ComboState : IState
{
    private PlayerStateMachine sm;
    public bool isCancelable = false;

    // 【新增】：用于暂存原始重力，并在收招时完美归还
    private float originalGravity;

    public ComboState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        isCancelable = false;

        // 1. 记录角色的原始重力
        originalGravity = sm.rb.gravityScale;

        // 2. 【工业级 ACT 核心增幅】：空中连段反重力悬停 (滞空控制)
        if (!sm.IsGrounded())
        {
            sm.rb.gravityScale = 0f;
            // 瞬间没收所有物理惯性，将角色牢牢“钉”在空中，防止开枪或挥剑时产生诡异下滑
            sm.rb.linearVelocity = Vector2.zero;
        }

        ComboNode activeNode = sm.GetComponent<ComboInputBuffer>().currentNode;

        if (activeNode != null)
        {
            Debug.Log($"[ComboState] 执行连招: {activeNode.nodeName}");
            sm.anim.Play(activeNode.animName, 0, 0f);

            // 如果地面招式带有 forwardThrust，且人在地上，则赋予突进位移
            if (sm.IsGrounded())
            {
                float dir = sm.playerController.facingDirection;
                sm.rb.linearVelocity = new Vector2(dir * activeNode.forwardThrust.x, sm.rb.linearVelocity.y);
            }
        }
    }

    public void Update()
    {
        // 动画后摇取消检测
        if (isCancelable)
        {
            bool advanced = sm.GetComponent<ComboInputBuffer>().TryAdvanceCombo();
            if (advanced) return;
        }

        // 允许在攻击窗口内输入微弱的方向调整朝向（瞄准/修招式朝向）
        float moveDir = sm.playerController.moveInput.x;
        if (Mathf.Abs(moveDir) > 0.1f)
        {
            int newDir = moveDir > 0 ? 1 : -1;
            sm.playerController.SetFacingDirection(newDir);
        }

        // 【新增维护】：只要还在空中连招期间，强行锁定物理速度，不接受下落重力叠加
        if (!sm.IsGrounded())
        {
            sm.rb.linearVelocity = Vector2.zero;
        }
    }

    public void Exit()
    {
        Debug.Log("[ComboState] 退出当前招式");
        sm.GetComponent<PlayerHitDetection>()?.ForceStopHitbox();

        // 3. 完美归还重力，让角色继续受重力掌控正常下落或待机
        sm.rb.gravityScale = originalGravity;
    }
}