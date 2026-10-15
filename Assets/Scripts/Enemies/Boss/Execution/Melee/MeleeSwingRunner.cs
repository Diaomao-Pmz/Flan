using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 挥击序列执行体。普通 class，由执行器持有。
///
/// 【为什么独立出来】
/// BAE_MeleeAttacker 和 BAE_DashAttacker 的第二阶段跑的是同一套挥击逻辑。
/// 抽出来之后，冲刺卡可以直接复用你在近战卡上调好的手感参数。
/// </summary>
public class MeleeSwingRunner
{
    private readonly List<Collider2D> hitBuffer = new List<Collider2D>(16);
    private readonly HashSet<Collider2D> alreadyHit = new HashSet<Collider2D>();

    // 供宿主画 Gizmos
    public bool HitboxActive { get; private set; }
    public Vector2 GizmoCenter { get; private set; }
    public Vector2 GizmoSize { get; private set; }

<<<<<<< Updated upstream
=======
    // 打断时要收起视觉，所以得记住当前用的是哪一个
    private BossMeleeVisual activeVisual;

    // 本次挥击所属的招式 —— 读它的击退配置
    private ActionNode knockbackNode;

>>>>>>> Stashed changes
    /// <summary>
    /// 按顺序跑完整套挥击。时序：前摇 → 判定窗口 → 后摇。
    /// </summary>
    public IEnumerator Run(
        List<MeleeSwing> swings,
        bool faceEachSwing,
        BossContext ctx,
        Transform owner,
        ContactFilter2D filter,
        Animator animator,
        string defaultActiveAnim,
<<<<<<< Updated upstream
        GameObject instigator)
    {
        if (swings == null || swings.Count == 0) yield break;

=======
        GameObject instigator,
        BossMeleeVisual visual = null,
        ActionNode sourceNode = null)
    {
        if (swings == null || swings.Count == 0) yield break;

        // 招式自带击退（ActionNode 上配置），整套挥击共用
        knockbackNode = sourceNode;

        activeVisual = visual;

>>>>>>> Stashed changes
        // 【数据快照】拍下开打瞬间的朝向。
        // 不这么做的话，判定框会在挥击过程中随玩家左右横跳。
        float facing = ResolveFacing(ctx, 1f);

        for (int i = 0; i < swings.Count; i++)
        {
            MeleeSwing swing = swings[i];
            if (swing == null) continue;

            // 追踪型连招：每段重新取一次朝向
            if (faceEachSwing) facing = ResolveFacing(ctx, facing);

<<<<<<< Updated upstream
            if (swing.windupTime > 0f)
=======
            // --- 前摇：手出现并举高 ---
            // 有视觉时由它 yield 掉 windupTime，没有时用 WaitForSeconds。
            // 两条路径耗时相同，判定时序不受视觉存在与否影响。
            if (visual != null)
            {
                yield return visual.Windup(
                    HitboxAnchor(owner, swing, facing),
                    swing.hitboxSize,
                    swing.windupTime);
            }
            else if (swing.windupTime > 0f)
            {
>>>>>>> Stashed changes
                yield return new WaitForSeconds(swing.windupTime);

            PlayAnim(animator, string.IsNullOrEmpty(swing.swingAnimName)
                ? defaultActiveAnim
                : swing.swingAnimName);

<<<<<<< Updated upstream
            yield return RunHitbox(swing, facing, ctx, owner, filter, instigator);
=======
            // --- 判定：手落回判定框 ---
            visual?.Strike(HitboxAnchor(owner, swing, facing), swing.hitboxSize);
>>>>>>> Stashed changes

            if (swing.recoverTime > 0f)
                yield return new WaitForSeconds(swing.recoverTime);
        }
    }

    private IEnumerator RunHitbox(
        MeleeSwing swing, float facing, BossContext ctx,
        Transform owner, ContactFilter2D filter, GameObject instigator)
    {
        alreadyHit.Clear();
        HitboxActive = true;

        float elapsed = 0f;

        do
        {
            // 每帧重算判定框位置，支持边移动边挥砍
            Vector2 center = (Vector2)owner.position
                             + new Vector2(swing.hitboxOffset.x * facing, swing.hitboxOffset.y);

            GizmoCenter = center;
            GizmoSize = swing.hitboxSize;

<<<<<<< Updated upstream
            Physics2D.OverlapBox(center, swing.hitboxSize, 0f, filter, hitBuffer);
=======
            visual?.Follow(HitboxAnchor(owner, swing, facing));

            if (ctx.MeleeHitsPlayerBody)
            {
                // 直接拿判定框和玩家身体碰撞箱比重叠，不依赖图层设置
                hitBuffer.Clear();
                if (BoxOverlapsBounds(center, swing.hitboxSize, ctx.playerBodyCollider.bounds))
                    hitBuffer.Add(ctx.playerBodyCollider);
            }
            else
            {
                Physics2D.OverlapBox(center, swing.hitboxSize, 0f, filter, hitBuffer);
            }
>>>>>>> Stashed changes

            HitboxUtility.ApplyDamage(
                hitBuffer,
                alreadyHit,           // 同一段挥击内每个目标只打一次
                swing.damage,
                DamageType.Melee,
                owner.position,       // 来源坐标，受击方据此算击退方向
                instigator,
                ctx.controller,
                knockbackNode != null && knockbackNode.overrideKnockback,
                knockbackNode != null ? knockbackNode.knockbackForce : Vector2.zero);

            // 策划填 0 表示瞬间判定，只跑一次
            if (swing.hitboxDuration <= 0f) break;

            yield return null;
            elapsed += Time.deltaTime;

        } while (elapsed < swing.hitboxDuration);

        HitboxActive = false;
    }

<<<<<<< Updated upstream
=======
    /// <summary>
    /// 判定框的【底边中点】（锚点）。offset 以 Boss 脚底（底部中心轴心）为基准，
    /// offset.x 会自动乘朝向，所以 SO 里填正数即可；offset.y 填 0 = 判定框贴地。
    /// 占位视觉（BossMeleeVisual，轴心同为底部中心）直接摆在这个点上。
    /// </summary>
    private Vector2 HitboxAnchor(Transform owner, MeleeSwing swing, float facing)
    {
        return (Vector2)owner.position
               + new Vector2(swing.hitboxOffset.x * facing, swing.hitboxOffset.y);
    }

    /// <summary>判定框的几何中心 = 锚点往上半个框高。物理检测和 Gizmos 用它</summary>
    private Vector2 HitboxCenter(Transform owner, MeleeSwing swing, float facing)
    {
        return HitboxAnchor(owner, swing, facing) + new Vector2(0f, swing.hitboxSize.y * 0.5f);
    }

    /// <summary>轴对齐的判定框与一个碰撞箱范围是否重叠</summary>
    private static bool BoxOverlapsBounds(Vector2 center, Vector2 size, Bounds target)
    {
        Vector2 half = size * 0.5f;
        return center.x - half.x < target.max.x && center.x + half.x > target.min.x
            && center.y - half.y < target.max.y && center.y + half.y > target.min.y;
    }

>>>>>>> Stashed changes
    public void Cancel()
    {
        alreadyHit.Clear();
        HitboxActive = false;
    }

    private float ResolveFacing(BossContext ctx, float fallback)
    {
        float sign = ctx.HorizontalSignToPlayer;
        float facing = sign != 0f ? sign : fallback;

        // 记下这一刀的朝向，供套路判断"玩家是否绕到背后"
        if (ctx.controller != null) ctx.controller.RecordAttackFacing(facing);
        return facing;
    }

    private void PlayAnim(Animator animator, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;
        animator.Play(stateName);
    }
}
