using UnityEngine;

public class BossStunState : IState
{
    private BossController boss;
    private float stunTimer;
    private float currentRecoverTime;
    private Rigidbody2D rb;

    public BossStunState(BossController bc)
    {
        boss = bc;
        rb = boss.GetComponent<Rigidbody2D>();
    }

    public void Enter()
    {
        Debug.Log("[BossStunState] Boss 护盾破碎，进入击飞破防状态！");

        // 1. 获取面板配置的恢复时间
        currentRecoverTime = boss.bossState.bossMechanic.shieldRecoverTime;
        stunTimer = 0f;

        // 2. 不再使用受质量影响的 AddForce，而是直接赋予绝对速度！
        if (rb != null)
        {
            // 读取我们在 BossState 里配置的击飞速度
            float knockupSpeed = boss.bossState.bossMechanic.stunKnockupSpeed;
            rb.linearVelocity = new Vector2(0f, knockupSpeed);
        }

        // 3. 强制打断正在发射的弹幕
        if (boss.BulletEmitter != null) boss.BulletEmitter.StopAttack();

        // [TODO 下一步激活] 订阅玩家的连招延时事件
        // ComboInputBuffer.OnPlayerComboExecuted += ExtendStunTime; 
    }

    public void Update()
    {
        stunTimer += Time.deltaTime;

        // 倒计时结束，恢复护盾并回到战斗状态
        if (stunTimer >= currentRecoverTime)
        {
            boss.bossState.bossMechanic.RecoverShield();
            boss.ChangeState(boss.CombatState);
        }
    }

    public void Exit()
    {
        Debug.Log("[BossStunState] 破防结束，Boss 护盾重新生成！");
        // [TODO 下一步激活] 取消订阅
        // ComboInputBuffer.OnPlayerComboExecuted -= ExtendStunTime;
    }

    // 留给下一步玩家Combo调用的回调
    private void ExtendStunTime()
    {
        float addTime = boss.bossState.bossMechanic.comboExtendDuration;
        currentRecoverTime += addTime;
        Debug.Log($"[BossStunState] 玩家连招追加！破防时间延长 {addTime} 秒，当前总时长: {currentRecoverTime}");
    }
}