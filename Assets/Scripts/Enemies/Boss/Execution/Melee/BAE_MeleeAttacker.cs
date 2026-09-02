using System.Collections;
using UnityEngine;

/// <summary>
/// Boss 近战执行器。认领 MeleeNode。
///
/// 【本次重构】挥击逻辑移入共用的 MeleeSwingRunner，本类只负责认领卡片、
/// 准备过滤器和画 Gizmos。行为与之前完全一致。
/// 冲刺执行器（BAE_DashAttacker）的第二阶段跑的是同一个 Runner。
/// </summary>
public class BAE_MeleeAttacker : MonoBehaviour, IBossActionExecutor
{
    [Header("--- 判定目标 ---")]
    [Tooltip("哪些图层会被判定命中。通常只勾 Player。")]
    [SerializeField] private LayerMask targetLayers;

    [Tooltip("是否把触发器（Trigger）也纳入判定。玩家碰撞体若是 Trigger 则必须勾上。")]
    [SerializeField] private bool detectTriggers = true;

    [Header("--- 占位视觉（可选）---")]
    [Tooltip("近战占位方块。留空则不显示，判定逻辑不受影响。\n" +
             "以后换成 Animator 驱动的真动画时，把这个槽位留空即可。")]
    [SerializeField] private BossMeleeVisual meleeVisual;

    [Header("--- Debug ---")]
    [SerializeField] private bool showHitbox = true;

    private Animator animator;
    private ContactFilter2D contactFilter;
    private readonly MeleeSwingRunner runner = new MeleeSwingRunner();

    private void Awake()
    {
        animator = GetComponent<Animator>();
        contactFilter = HitboxUtility.BuildFilter(targetLayers, detectTriggers);
    }

    public System.Type NodeType => typeof(MeleeNode);

    public IEnumerator Execute(ActionNode node, BossContext ctx)
    {
        MeleeNode melee = node as MeleeNode;
        if (melee == null) yield break;

        if (melee.swings == null || melee.swings.Count == 0)
        {
            Debug.LogWarning($"[BAE_MeleeAttacker] 卡片 {melee.name} 没有配置任何挥击段，已跳过。", this);
            yield break;
        }

        PlayAnim(melee.chargeAnimName);

        yield return runner.Run(
            melee.swings,
            melee.faceTargetEachSwing,
            ctx,
            transform,
            contactFilter,
            animator,
            melee.activeAnimName,
            gameObject,
            meleeVisual);

        PlayAnim(melee.recoverAnimName);
    }

    public void Cancel()
    {
        runner.Cancel();          // 内部会收起 meleeVisual
        meleeVisual?.Hide();      // 双保险：Run 未启动时 runner 还没记住 visual
    }

    private void PlayAnim(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;
        animator.Play(stateName);
    }

    private void OnDrawGizmos()
    {
        if (!showHitbox || !Application.isPlaying || !runner.HitboxActive) return;

        Gizmos.color = new Color(1f, 0.3f, 0f, 1f); // 橙红，和玩家的绿框区分开
        Gizmos.DrawWireCube(runner.GizmoCenter, runner.GizmoSize);
    }
}
