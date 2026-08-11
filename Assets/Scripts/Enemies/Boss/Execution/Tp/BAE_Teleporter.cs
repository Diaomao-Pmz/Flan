using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BAE_Teleporter : MonoBehaviour, IBossActionExecutor
{
    [Header("--- 随机点传送 (RandomPoint) ---")]
    [Tooltip("一阶段专用传送点。与二阶段互不共用。")]
    [SerializeField] private List<Transform> phase1Points = new List<Transform>();

    [Tooltip("二阶段专用传送点。与一阶段互不共用。")]
    [SerializeField] private List<Transform> phase2Points = new List<Transform>();

    [Tooltip("小于此距离视为「就在原地」，会被排除，避免传了个寂寞。")]
    [SerializeField] private float samePointThreshold = 0.1f;

    [Header("--- 绕背传送 (BehindPlayer) ---")]
    [Tooltip("玩家 Transform。留空则由 BossController.Init() 自动注入。")]
    [SerializeField] private Transform playerTransform;

    [Tooltip("落点与玩家的水平距离。Boss 会出现在玩家背后这么远的地方。")]
    [SerializeField] private float behindPlayerOffset = 3f;

    [Tooltip("勾选后绕背传送会同时对齐玩家的 Y 坐标；不勾则保持 Boss 当前高度。")]
    [SerializeField] private bool matchPlayerHeight = false;

    [Header("--- 中央传送 (Center) ---")]
    [Tooltip("场地中央锚点。二阶段转场等场合使用。")]
    [SerializeField] private Transform centerPoint;

    // 黑板引用，用于读取当前阶段。缓存以避免每次传送都 GetComponent。
    private BossState bossState;

    // ==========================================================
    //  IBossActionExecutor 实现：认领 TeleportNode
    public System.Type NodeType => typeof(TeleportNode);

    public IEnumerator Execute(ActionNode node, BossContext ctx)
    {
        TeleportNode tpNode = node as TeleportNode;
        if (tpNode == null) yield break;

        // 传送前摇。不想要的话把资产里的 teleportDelay 调成 0。
        if (tpNode.teleportDelay > 0f)
        {
            yield return new WaitForSeconds(tpNode.teleportDelay);
        }

        ExecuteTeleport(tpNode.targetType);
    }

    /// <summary>传送是瞬时行为，没有需要中断的持续演出。</summary>
    public void Cancel()
    {
        // 有意留空。若日后加了传送残影/特效协程，在这里停掉它。
    }

    //---------------------------------------------------

    public void Init(Transform player)
    {
        // Inspector 里已经拖了就以 Inspector 为准，否则接受外部注入
        if (playerTransform == null) playerTransform = player;
        bossState = GetComponent<BossState>();
    }

    // ==========================================================
    //  具体位移逻辑
    public void ExecuteTeleport(TeleportTargetType strategy)
    {
        switch (strategy)
        {
            case TeleportTargetType.RandomPoint:
                TeleportToRandomPoint();
                break;
            case TeleportTargetType.BehindPlayer:
                TeleportBehindPlayer();
                break;
            case TeleportTargetType.Center:
                TeleportToCenter();
                break;
        }
    }

    /// <summary>
    /// 按当前阶段选取对应的传送点列表，随机挑一个不在原地的点。
    /// 一阶段与二阶段的点位互不共用。
    /// </summary>
    private void TeleportToRandomPoint()
    {
        List<Transform> pool = GetCurrentPhasePoints();

        if (pool == null || pool.Count == 0)
        {
            Debug.LogWarning($"[BAE_Teleporter] 当前阶段的传送点列表为空，传送已跳过。", this);
            return;
        }

        // 先排除「就在原地」和空槽位，再从剩下的里面随机
        List<Transform> candidates = new List<Transform>(pool.Count);
        for (int i = 0; i < pool.Count; i++)
        {
            Transform p = pool[i];
            if (p == null) continue;
            if (Vector2.Distance(transform.position, p.position) < samePointThreshold) continue;
            candidates.Add(p);
        }

        // 全都被排除了（例如列表里只配了一个点，而 Boss 正好站在上面）
        if (candidates.Count == 0)
        {
            Debug.LogWarning("[BAE_Teleporter] 没有可用的落点（可能只配了一个点且 Boss 就在其上）。", this);
            return;
        }

        Transform target = candidates[Random.Range(0, candidates.Count)];
        transform.position = target.position;
    }

    private void TeleportBehindPlayer()
    {
        if (playerTransform == null)
        {
            Debug.LogWarning("[BAE_Teleporter] 未设置玩家引用，绕背传送已跳过。", this);
            return;
        }

        // 玩家在 Boss 右边 → 传到玩家更右边；反之传到更左边
        float sign = playerTransform.position.x > transform.position.x ? 1f : -1f;

        float targetY = matchPlayerHeight ? playerTransform.position.y : transform.position.y;

        transform.position = new Vector3(
            playerTransform.position.x + sign * behindPlayerOffset,
            targetY,
            transform.position.z);
    }

    private void TeleportToCenter()
    {
        if (centerPoint == null)
        {
            Debug.LogWarning("[BAE_Teleporter] 未设置中央锚点，中央传送已跳过。", this);
            return;
        }

        transform.position = centerPoint.position;
    }

    /// <summary>读取黑板判断当前阶段，返回对应的点位列表。</summary>
    private List<Transform> GetCurrentPhasePoints()
    {
        if (bossState == null) bossState = GetComponent<BossState>();

        bool isPhase2 = bossState != null && bossState.bossMechanic.isPhase2;
        return isPhase2 ? phase2Points : phase1Points;
    }
}