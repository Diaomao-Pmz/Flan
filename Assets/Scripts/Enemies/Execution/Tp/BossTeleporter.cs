using UnityEngine;

public class BossTeleporter : MonoBehaviour
{
    [Header("--- 传送参考点 ---")]
    public Transform[] phase1TeleportPoints;
    public Transform phase2FixedPoint;

    public Transform[] anotherPoints;//专门用于 Another 策略的两个场景锚点

    private Transform playerTransform;

    public void Init(Transform player)
    {
        playerTransform = player;
    }

    // 接收大脑发出的策略指令，执行对应的位移逻辑
    public void ExecuteTeleport(TeleportTargetType strategy)
    {
        switch (strategy)
        {
            case TeleportTargetType.Another:
                TeleportToAnother();
                break;
            case TeleportTargetType.RandomPoint:
                TeleportToRandomPoint();
                break;
            case TeleportTargetType.BehindPlayer:
                TeleportBehindPlayer();
                break;
            case TeleportTargetType.Center:
                if (phase2FixedPoint != null) transform.position = phase2FixedPoint.position;
                break;
        }
    }

    private void TeleportToAnother()
    {
        // 防呆设计：确保策划在面板里塞了刚好两个锚点
        if (anotherPoints == null || anotherPoints.Length < 2)
        {
            Debug.LogWarning("[BossTeleporter] Another 传送点未正确配置！请在 Inspector 中拖入至少 2 个锚点。");
            return;
        }

        // 访问数据黑板，读取“双腿（MoveState）”写进去的死角情报
        BossState state = GetComponent<BossState>();

        if (state != null && state.bossMechanic.isCornered)
        {
            // 核心逻辑：Boss被逼入墙角，开始计算哪个锚点离自己更远
            float dist0 = Vector2.Distance(transform.position, anotherPoints[0].position);
            float dist1 = Vector2.Distance(transform.position, anotherPoints[1].position);

            // 三元运算符：如果 dist0 大于 dist1，就选锚点 0，否则选锚点 1
            Transform targetPoint = dist0 > dist1 ? anotherPoints[0] : anotherPoints[1];

            transform.position = targetPoint.position;
            Debug.Log($"靠墙逃脱触发！传送到较远锚点: {targetPoint.name}</color>");
        }
        else
        {
            // 【可选补充】：如果抽到了这张卡，但 Boss 并没有靠墙怎么办？
            // 默认给一个随机挑选的逻辑作为兜底，防止原地罚站
            Transform targetPoint = anotherPoints[Random.Range(0, anotherPoints.Length)];
            transform.position = targetPoint.position;
            Debug.Log("未靠墙执行 Another 传送，随机挑选了一个锚点。");
        }
    }

    private void TeleportToRandomPoint()
    {
        if (phase1TeleportPoints != null && phase1TeleportPoints.Length > 0)
        {
            Transform targetPoint = phase1TeleportPoints[Random.Range(0, phase1TeleportPoints.Length)];
            // 防止原地传送的逻辑
            if (Vector2.Distance(transform.position, targetPoint.position) < 0.1f)
            {
                int currentIndex = System.Array.IndexOf(phase1TeleportPoints, targetPoint);
                targetPoint = phase1TeleportPoints[(currentIndex + 1) % phase1TeleportPoints.Length];
            }
            transform.position = targetPoint.position;
            Debug.Log("[BossTeleporter] 执行了随机点传送！");
        }
    }

    private void TeleportBehindPlayer()
    {
        if (playerTransform != null)
        {
            // 简单的绕背逻辑：如果在玩家左边，就传到右边；在右边，就传到左边
            float offsetX = playerTransform.position.x > transform.position.x ? 3f : -3f;
            transform.position = new Vector3(playerTransform.position.x + offsetX, transform.position.y, transform.position.z);
            Debug.Log("[BossTeleporter] 执行了绕背传送！");
        }
    }
}