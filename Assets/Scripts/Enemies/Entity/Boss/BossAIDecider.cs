using System.Collections.Generic;
using UnityEngine;

public class BossAIDecider : MonoBehaviour
{
    private BossController boss;

    [Header("--- 技能池与特招 ---")]
    [SerializeField] List<ActionNode> phase1Action; // 【修改】：直接存放资产，不再套壳
    [SerializeField] List<ActionNode> phase2Action;
    [Tooltip("当被逼入死角时，强制抽出的保命底牌")]
    public ActionNode tpSkill;

    private List<ActionNode> currentAttackNodes;

    // 【修改】：抛弃固定变量，当前卡池中最远的施法距离，就是 Boss 的有效索敌距离
    public bool canAttack => boss.DistanceToPlayer <= GetMaxAggroRange();

    void Start()
    {
        boss = GetComponent<BossController>();
        currentAttackNodes = phase1Action;
    }

    public void SwitchToPhase2()
    {
        Debug.Log("[BossAIDecider] 切换到二阶段招式池！");
        currentAttackNodes = phase2Action;
    }

    // 辅助方法：动态获取当前卡池的最远射程
    private float GetMaxAggroRange()
    {
        if (currentAttackNodes == null || currentAttackNodes.Count == 0) return 5f;
        float maxRange = 0f;
        foreach (var node in currentAttackNodes)
        {
            if (node != null && node.maxCastDistance > maxRange) maxRange = node.maxCastDistance;
        }
        return maxRange;
    }

    // 【新增架构特性】：推算当前卡池的最优拉扯距离，供双腿（MoveState）使用
    public float GetOptimalEngagementDistance()
    {
        if (currentAttackNodes == null || currentAttackNodes.Count == 0) return 5f;

        ActionNode bestNode = null;
        int maxWeight = -1;

        // 找出当前权重最高（最想用）的技能
        foreach (var node in currentAttackNodes)
        {
            if (node != null && node.baseWeight > maxWeight)
            {
                maxWeight = node.baseWeight;
                bestNode = node;
            }
        }

        // 取该技能最大最小射程的中间值，作为移动的风筝目标点
        if (bestNode != null)
        {
            return (bestNode.minCastDistance + bestNode.maxCastDistance) / 2f;
        }
        return 5f;
    }

    public ActionNode SelectSkill()
    {
        // 1. 死角特判拦截
        if (boss.bossState.bossMechanic.isCornered && boss.bossState.bossMechanic.currentTeleportTimer <= 0)
        {
            if (tpSkill != null)
            {
                Debug.Log("[AI] 被逼入死角！强行切牌出老千：传送！");
                boss.bossState.bossMechanic.currentTeleportTimer = boss.bossState.bossMechanic.teleportCooldown;
                return tpSkill;
            }
        }

        // 2. 常规抽卡：实时构建有效卡池
        if (currentAttackNodes == null || currentAttackNodes.Count == 0) return null;

        float dist = boss.DistanceToPlayer;
        List<ActionNode> validNodes = new List<ActionNode>();
        int totalSum = 0;

        // 遍历所有牌，只有距离合适的牌才有资格入池
        foreach (ActionNode node in currentAttackNodes)
        {
            if (node == null) continue;

            if (dist >= node.minCastDistance && dist <= node.maxCastDistance)
            {
                validNodes.Add(node);
                totalSum += node.baseWeight; // 【修改】：直接读取资产的基础权重
            }
        }

        if (totalSum <= 0) return null; // 没有任何技能的距离合适

        // 3. 按权重随机抽取
        int rn = Random.Range(1, totalSum + 1);
        int compareNum = 0;

        foreach (ActionNode node in validNodes)
        {
            compareNum += node.baseWeight;
            if (rn <= compareNum)
            {
                Debug.Log($"[AI] 随机抽卡: {node.actionName}");
                return node;
            }
        }

        return null;
    }
}