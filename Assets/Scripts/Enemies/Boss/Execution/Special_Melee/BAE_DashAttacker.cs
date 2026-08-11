using System.Collections;
using UnityEngine;

/// <summary>
/// Boss 突进斩执行器。认领 DashAttackNode。
/// 两阶段：先用物理速度冲到玩家面前，再交给共用的 MeleeSwingRunner 打挥击。
///
/// 【物理归属】冲刺期间本执行器独占 Rigidbody2D 的水平速度。
/// BossCombatState.Enter() 已经把 X 速度清零、MoveState 也已 Exit，
/// 所以这段时间没有别人在写 linearVelocity.x，不会打架。
/// 结束时主动归零，避免把速度残留带回 MoveState。
/// </summary>
public class BAE_DashAttacker : MonoBehaviour, IBossActionExecutor
{
    [Header("--- 判定目标 ---")]
    [Tooltip("哪些图层会被挥击命中。通常只勾 Player。")]
    [SerializeField] private LayerMask targetLayers;

    [Tooltip("是否把触发器（Trigger）也纳入判定")]
    [SerializeField] private bool detectTriggers = true;

    [Header("--- 冲刺 ---")]
    [Tooltip("哪些图层算墙。留空则用 BossController 上配置的 wallLayer。")]
    [SerializeField] private LayerMask wallLayerOverride;

    [Tooltip("撞墙检测射线长度")]
    [SerializeField] private float wallCheckDistance = 1.2f;

    [Header("--- Debug ---")]
    [SerializeField] private bool showHitbox = true;

    private Animator animator;
    private Rigidbody2D rb;
    private ContactFilter2D contactFilter;
    private readonly MeleeSwingRunner runner = new MeleeSwingRunner();

    private void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        contactFilter = HitboxUtility.BuildFilter(targetLayers, detectTriggers);
    }

    public System.Type NodeType => typeof(DashAttackNode);

    public IEnumerator Execute(ActionNode node, BossContext ctx)
    {
        DashAttackNode dash = node as DashAttackNode;
        if (dash == null) yield break;

        // ---- 前摇 ----
        PlayAnim(dash.chargeAnimName);
        if (dash.windupBeforeDash > 0f)
            yield return new WaitForSeconds(dash.windupBeforeDash);

        // ---- 第一阶段：冲刺 ----
        PlayAnim(dash.dashAnimName);
        yield return DashTowardTarget(dash, ctx);

        if (dash.pauseAfterDash > 0f)
            yield return new WaitForSeconds(dash.pauseAfterDash);

        // ---- 第二阶段：挥击（复用与近战相同的执行体）----
        if (dash.swings != null && dash.swings.Count > 0)
        {
            yield return runner.Run(
                dash.swings,
                dash.faceTargetEachSwing,
                ctx,
                transform,
                contactFilter,
                animator,
                dash.activeAnimName,
                gameObject);
        }

        PlayAnim(dash.recoverAnimName);
    }

    public void Cancel()
    {
        runner.Cancel();
        StopHorizontal(); // 打断时别把冲刺速度留给 MoveState
    }

    // ==========================================================
    //  冲刺
    // ==========================================================

    private IEnumerator DashTowardTarget(DashAttackNode dash, BossContext ctx)
    {
        if (rb == null || ctx.player == null) yield break;

        // 【数据快照】起冲瞬间锁定方向，冲刺途中不再转向。
        float dirX = ctx.HorizontalSignToPlayer;
        if (dirX == 0f) dirX = 1f;

        LayerMask walls = ResolveWallLayer(ctx);

        Vector2 startPos = transform.position;
        float elapsed = 0f;

        while (true)
        {
            // 终止条件 1：够近了
            if (ctx.DistanceToPlayer <= dash.stopDistance) break;

            // 终止条件 2：冲够了
            if (Vector2.Distance(startPos, transform.position) >= dash.maxDashDistance) break;

            // 终止条件 3：撞墙
            if (dash.stopOnWall &&
                HitboxUtility.IsBlocked(transform.position, new Vector2(dirX, 0f), wallCheckDistance, walls))
                break;

            // 终止条件 4：超时兜底，防止任何意外导致卡死
            if (elapsed >= dash.dashTimeout) break;

            rb.linearVelocity = new Vector2(dirX * dash.dashSpeed, rb.linearVelocity.y);

            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        StopHorizontal();
    }

    private void StopHorizontal()
    {
        if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    private LayerMask ResolveWallLayer(BossContext ctx)
    {
        if (wallLayerOverride.value != 0) return wallLayerOverride;
        return ctx.controller != null ? ctx.controller.wallLayer : default;
    }

    private void PlayAnim(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;
        animator.Play(stateName);
    }

    private void OnDrawGizmos()
    {
        if (!showHitbox || !Application.isPlaying || !runner.HitboxActive) return;

        Gizmos.color = new Color(1f, 0.6f, 0f, 1f); // 橙黄，与纯近战的橙红区分
        Gizmos.DrawWireCube(runner.GizmoCenter, runner.GizmoSize);
    }
}
