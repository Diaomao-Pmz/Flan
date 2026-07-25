using UnityEngine;

public class BossMoveState : IState
{
    private BossController boss;
    private Rigidbody2D rb;
    private float moveTimer;
    private float targetMaintainDistance; // 【新增】：动态存储大脑发来的最优距离

    public BossMoveState(BossController bc)
    {
        boss = bc;
        rb = boss.GetComponent<Rigidbody2D>();
    }

    public void Enter()
    {
        moveTimer = Random.Range(1f, 2.5f);
        boss.bossState.bossMechanic.isCornered = false;

        // 【核心交互】：向 AI 参谋长请求最佳接敌距离
        targetMaintainDistance = boss.AI.GetOptimalEngagementDistance();

        Debug.Log($"[BossMoveState] 收到指令，尝试保持动态最佳距离: {targetMaintainDistance:F1}。移动持续 {moveTimer:F1} 秒");
    }

    public void Update()
    {
        moveTimer -= Time.deltaTime;

        if (moveTimer <= 0)
        {
            boss.ChangeState(boss.CombatState);
            return;
        }

        Vector2 playerPos = boss.PlayerTransform.position;
        Vector2 bossPos = boss.transform.position;
        float distance = Vector2.Distance(playerPos, bossPos);
        float dirX = 0f;

        // 【修改】：使用动态获取的 targetMaintainDistance 替代写死的变量
        if (distance < targetMaintainDistance - 0.5f)
        {
            dirX = (bossPos.x > playerPos.x) ? 1f : -1f;
        }
        else if (distance > targetMaintainDistance + 0.5f)
        {
            dirX = (playerPos.x > bossPos.x) ? 1f : -1f;
        }

        if (rb != null)
        {
            rb.linearVelocity = new Vector2(dirX * boss.moveSpeed, rb.linearVelocity.y);
        }

        if (dirX != 0)
        {
            RaycastHit2D hit = Physics2D.Raycast(bossPos, new Vector2(dirX, 0), boss.wallCheckDistance, boss.wallLayer);
            boss.bossState.bossMechanic.isCornered = (hit.collider != null);
        }
    }

    public void Exit()
    {
        if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }
}