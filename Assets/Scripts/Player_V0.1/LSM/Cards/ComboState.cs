using UnityEngine;

public class ComboState : IState
{
    private PlayerStateMachine sm;

    public ComboState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        // 核心结合点：直接去问 Buffer 要当前对出来的暗号节点
        ComboNode activeNode = sm.GetComponent<ComboInputBuffer>().currentNode;

        if (activeNode != null)
        {
            Debug.Log($"[ComboState] 执行连招: {activeNode.nodeName}");

            // 1. 播放数据资产中配置的动画
            sm.anim.Play(activeNode.animName, 0, 0f);

            // 2. 如果这招自带突进，给它个初速度（朝向 * 突进值）
            float dir = sm.playerController.facingDirection;
            sm.rb.linearVelocity = new Vector2(dir * activeNode.forwardThrust.x, sm.rb.linearVelocity.y);
        }
    }

    public void Update()
    {
        // 依然保留边跑边打的逻辑（类Tevi的灵魂手感）
        float moveDir = sm.playerController.moveInput.x;

        if (Mathf.Abs(moveDir) > 0.1f)
        {
            // 打 8 折速度移动
            sm.rb.linearVelocity = new Vector2(moveDir * sm.moveSpeed * 0.8f, sm.rb.linearVelocity.y);

            // 边走边打更新朝向
            int newDir = moveDir > 0 ? 1 : -1;
            sm.playerController.SetFacingDirection(newDir);
        }
        else
        {
            // 如果没推摇杆，保留进入状态时的惯性或突进速度，但施加一点摩擦力
            sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x * 0.9f, sm.rb.linearVelocity.y);
        }

        // 切出状态依然交由 OnAttackAnimationEnd（动画事件标记）处理
    }

    public void Exit()
    {
        Debug.Log("[ComboState] 退出当前招式");

        // 防止人冲刺走了，原地还留着空气判定框
        sm.GetComponent<PlayerHitDetection>()?.ForceStopHitbox();

        // 连招重置逻辑
        // 如果希望：跳跃/冲刺后，玩家再按攻击，必须从【平A第一段】重新起手，就取消注释下面这行。
        sm.GetComponent<ComboInputBuffer>().ResetCombo();

        /* 如果你把上面那行 ResetCombo() 注释掉（不重置），玩家就可以实现：
         * 【平A第一段】 -> 【冲刺打断】 -> 【冲刺结束前按攻击】 -> 直接打出【平A第二段】
         */
    }
}