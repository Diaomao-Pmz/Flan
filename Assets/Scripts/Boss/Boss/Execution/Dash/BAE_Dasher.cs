using System.Collections;
using UnityEngine;

/// <summary>
/// Boss 冲刺执行器（纯位移）。认领 DashMoveNode。挂在 Boss 身上。
///
/// 两种用法：
///   · 套路步骤 / 主动出招：跟其他执行器一样，由出招状态调 Execute 演完一次冲刺
///   · 位移卡池：移动状态调 TryMovementDash，在移动阶段里冲一次（冲刺期间移动状态不再控制走路）
///
/// 冲刺过程：前摇 → 按冲刺卡算好方向和距离 → 每个物理帧按「距离 ÷ 用时」前进，
/// 同时问终点裁判（BossDashEndpointGuard）别停在玩家身体里、前面有墙就停 → 冲完停顿。
/// 能穿过玩家时，冲刺期间 Boss 刚体排除玩家图层（互不碰撞）；冲完还叠着就等分开再恢复（最多 0.5 秒）。
///
/// 【物理归属】冲刺期间本执行器独占 Boss 刚体的水平速度。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class BAE_Dasher : MonoBehaviour, IBossActionExecutor
{
    [Header("--- 墙 ---")]
    [Tooltip("哪些图层算墙（冲刺撞上就停）。留空则用 BossController 上的 Wall Layer")]
    [SerializeField] private LayerMask wallLayerOverride;

    [Header("--- Debug ---")]
    [Tooltip("勾上 = 每次冲刺在 Console 打印方向、距离和终点判定结果")]
    [SerializeField] private bool debugDash = false;

    /// <summary>冲完还和玩家叠着时，最多再穿身这么久（秒），超时就强制恢复碰撞</summary>
    private const float MaxOverlapGrace = 0.5f;

    /// <summary>冲刺时间到了还在穿身时的兜底：最多再多走这么久（秒）</summary>
    private const float MaxOvertime = 1f;

    private const float Eps = 0.0001f;

    private Rigidbody2D rb;
    private Collider2D body;
    private Animator animator;
    private BossController controller;
    private Transform playerTransform;

    private Coroutine moveRoutine;    // 位移卡池发起的冲刺
    private bool dashing;

    // 穿身（照搬玩家 PassThroughController 的做法：只增减自己加进 excludeLayers 的那几位）
    private bool passActive;
    private int addedBits;
    private float graceTimer;
    private Collider2D passPlayer;

    private readonly RaycastHit2D[] castHits = new RaycastHit2D[4];

    public System.Type NodeType => typeof(DashMoveNode);

    /// <summary>位移卡池发起的冲刺还没结束（含前摇）。期间移动状态不要控制走路</summary>
    public bool IsMovementDashing => moveRoutine != null;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        controller = GetComponent<BossController>();
        foreach (Collider2D c in GetComponents<Collider2D>())
        {
            if (!c.isTrigger) { body = c; break; }
        }
        if (body == null)
            Debug.LogWarning("[BAE_Dasher] Boss 身上找不到非触发器的 Collider2D，冲刺的终点判定和撞墙检查不可用。", this);
    }

    public void Init(Transform player)
    {
        playerTransform = player;
    }

    // ==========================================================
    //  套路步骤 / 主动出招
    // ==========================================================

    public IEnumerator Execute(ActionNode node, BossContext ctx)
    {
        DashMoveNode dash = node as DashMoveNode;
        if (dash == null) yield break;
        yield return DashRoutine(dash, null);
    }

    public void Cancel()
    {
        CancelMovementDash();
        StopDash();
    }

    // ==========================================================
    //  位移卡池（移动状态调用）
    // ==========================================================

    /// <summary>
    /// 位移卡池抽中冲刺卡时调用。返回 false = 这次不冲（距离太短 / 缺组件），移动状态照常走路
    /// </summary>
    /// <param name="intentDistance">冲到某个距离时用的默认距离（卡上没填时）：当前意图的理想距离</param>
    public bool TryMovementDash(DashMoveNode node, float intentDistance)
    {
        if (node == null || rb == null || playerTransform == null) return false;
        if (moveRoutine != null) return false;

        if (!Plan(node, intentDistance, out _, out float distance) || distance < node.minDashDistance)
            return false;

        moveRoutine = StartCoroutine(MovementDashRoutine(node, intentDistance));
        return true;
    }

    /// <summary>位移卡池的冲刺还没冲完就离开移动状态（被打晕、受击打断……）：停下</summary>
    public void CancelMovementDash()
    {
        if (moveRoutine == null) return;
        StopCoroutine(moveRoutine);
        moveRoutine = null;
        StopDash();
    }

    private IEnumerator MovementDashRoutine(DashMoveNode node, float intentDistance)
    {
        yield return DashRoutine(node, intentDistance);
        moveRoutine = null;
    }

    // ==========================================================
    //  冲刺本体
    // ==========================================================

    private IEnumerator DashRoutine(DashMoveNode dash, float? intentDistance)
    {
        if (rb == null || playerTransform == null) yield break;

        StopHorizontal();
        PlayAnim(dash.chargeAnimName);
        if (dash.windup > 0f) yield return BossTime.Wait(controller, dash.windup);

        // 前摇结束这一刻按双方位置定好方向和距离，冲刺途中不再改
        if (!Plan(dash, intentDistance, out float dirX, out float planned) || planned < dash.minDashDistance)
        {
            if (debugDash) Debug.Log($"[BAE_Dasher] {dash.actionName} 距离太短，不冲", this);
            PlayAnim(dash.recoverAnimName);
            yield break;
        }

        Collider2D player = controller != null ? controller.Context.playerBodyCollider : null;
        bool canPass = dash.CanPass;
        if (canPass) BeginPass(player);

        PlayAnim(dash.dashAnimName);
        dashing = true;

        Vector2 dir = new Vector2(dirX, 0f);
        float duration = Mathf.Max(0.01f, dash.dashDuration);
        float cruise = planned / duration;           // 平均速度（格/秒）
        float traveled = 0f;
        float elapsed = 0f;
        float limit = planned;
        LayerMask walls = ResolveWallLayer();
        BossDashEndpointGuard.Verdict lastVerdict = BossDashEndpointGuard.Verdict.Clear;

        while (true)
        {
            // dt = 这一物理帧的 Boss 时间（按动作倍率）；realDt = 真实物理帧，换算速度用。距离不变，倍率越大冲得越快
            float realDt = Time.fixedDeltaTime;
            float dt = BossTime.FixedDelta(controller);

            // 终点裁判：按剩下的路冲完会不会停在玩家身体里
            if (dash.endpointGuard && body != null)
            {
                float allowed = BossDashEndpointGuard.Resolve(
                    body.bounds, dir, planned - traveled, player,
                    canPass, dash.endpointPassRatio, dash.endpointOverlapTolerance, out lastVerdict);
                limit = traveled + allowed;
            }

            // 这一帧想走多远：用时内按平均速度；用时到了但还要穿到玩家身后，继续按平均速度走
            float want = elapsed < duration - Eps ? cruise * dt : 0f;
            if (want <= 0f && traveled < limit - Eps) want = cruise * dt;

            float step = Mathf.Clamp(want, 0f, Mathf.Max(0f, limit - traveled));

            // 前面有墙：只走到贴墙
            bool hitWall = false;
            if (step > 0f && walls.value != 0 && body != null)
            {
                float free = FreeDistance(dir, step, walls);
                if (free < step - Eps) { step = free; hitWall = true; }
            }

            rb.linearVelocity = new Vector2(dirX * step / realDt, rb.linearVelocity.y);
            traveled += step;
            elapsed += dt;

            yield return new WaitForFixedUpdate();

            bool timeUp = elapsed >= duration - Eps;
            if (hitWall) break;
            if (timeUp && traveled >= limit - Eps) break;
            if (elapsed >= duration + MaxOvertime) break; // 兜底，防止任何意外导致卡死
        }

        StopHorizontal();
        dashing = false;
        EndPass();

        if (debugDash)
            Debug.Log($"[BAE_Dasher] {dash.actionName}：方向 {dirX:+0;-0}，计划 {planned:F2} 格，实际 {traveled:F2} 格，" +
                      $"终点判定 {lastVerdict}", this);

        PlayAnim(dash.recoverAnimName);
        if (dash.pauseAfter > 0f) yield return BossTime.Wait(controller, dash.pauseAfter);
    }

    /// <summary>按冲刺卡和此刻双方位置算方向（+1 右 / -1 左）和要冲的距离（已按最远距离封顶）</summary>
    private bool Plan(DashMoveNode dash, float? intentDistance, out float dirX, out float distance)
    {
        dirX = 1f;
        distance = 0f;
        if (playerTransform == null) return false;

        float bossX = transform.position.x;
        float playerX = playerTransform.position.x;
        float side = bossX >= playerX ? 1f : -1f;   // Boss 在玩家哪一边

        switch (dash.target)
        {
            case DashMoveTarget.ToDistance:
            {
                float d = dash.landingDistance >= 0f
                    ? dash.landingDistance
                    : intentDistance ?? (controller != null && controller.AI != null
                        ? controller.AI.GetOptimalEngagementDistance() : 5f);
                float targetX = playerX + side * d;   // 落在 Boss 现在这一侧
                float delta = targetX - bossX;
                dirX = delta >= 0f ? 1f : -1f;
                distance = Mathf.Abs(delta);
                break;
            }
            case DashMoveTarget.AwayFromPlayer:
                dirX = side;
                distance = dash.retreatDistance;
                break;
            case DashMoveTarget.ThroughPlayer:
                dirX = -side;
                distance = Mathf.Abs(playerX - bossX) + dash.behindDistance;
                break;
        }

        distance = Mathf.Min(distance, Mathf.Max(0f, dash.maxDashDistance));
        return true;
    }

    /// <summary>沿 dir 最多还能走多远才贴上墙（用 Boss 身体投射）</summary>
    private float FreeDistance(Vector2 dir, float want, LayerMask walls)
    {
        ContactFilter2D filter = new ContactFilter2D { useLayerMask = true, layerMask = walls, useTriggers = false };
        int n = body.Cast(dir, filter, castHits, want + 0.02f);
        float free = want;
        for (int i = 0; i < n; i++)
            free = Mathf.Min(free, Mathf.Max(0f, castHits[i].distance - 0.01f));
        return free;
    }

    private void StopDash()
    {
        if (!dashing) return;
        dashing = false;
        StopHorizontal();
        EndPass();
    }

    private void StopHorizontal()
    {
        if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    // ==========================================================
    //  穿身
    // ==========================================================

    private void BeginPass(Collider2D player)
    {
        if (rb == null || player == null) return;
        int bit = 1 << player.gameObject.layer;
        if (!passActive)
        {
            int current = rb.excludeLayers.value;
            addedBits = bit & ~current;     // 只记自己新加的位，原本就排除的不动
            rb.excludeLayers = current | bit;
            passActive = true;
        }
        passPlayer = player;
        graceTimer = -1f; // 冲刺中：不计时
    }

    /// <summary>冲完：不再叠着就马上恢复碰撞，还叠着就交给 FixedUpdate 等分开</summary>
    private void EndPass()
    {
        if (!passActive) return;
        graceTimer = 0f;
        if (!OverlappingPlayer()) RestoreCollision();
    }

    private void FixedUpdate()
    {
        if (!passActive || graceTimer < 0f) return;

        if (graceTimer < MaxOverlapGrace && OverlappingPlayer())
        {
            graceTimer += Time.fixedDeltaTime;
            return;
        }
        RestoreCollision();
    }

    private bool OverlappingPlayer()
    {
        if (body == null || passPlayer == null || !passPlayer.enabled) return false;
        return Physics2D.Distance(body, passPlayer).isOverlapped;
    }

    private void RestoreCollision()
    {
        if (!passActive) return;
        if (rb != null) rb.excludeLayers = rb.excludeLayers.value & ~addedBits;
        addedBits = 0;
        passActive = false;
        passPlayer = null;
    }

    private void OnDisable()
    {
        StopDash();
        RestoreCollision();
    }

    // ==========================================================

    private LayerMask ResolveWallLayer()
    {
        if (wallLayerOverride.value != 0) return wallLayerOverride;
        return controller != null ? controller.wallLayer : default;
    }

    private void PlayAnim(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;
        animator.Play(stateName);
    }
}
