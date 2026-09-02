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
///
/// 【视觉是可选的】
/// visual 参数可以传 null，逻辑照常运行。删掉整个 BossMeleeVisual 组件
/// 也不会影响判定 —— 换成 Animator 驱动的真动画时，只需删掉这几个 visual 调用。
/// </summary>
public class MeleeSwingRunner
{
    private readonly List<Collider2D> hitBuffer = new List<Collider2D>(16);
    private readonly HashSet<Collider2D> alreadyHit = new HashSet<Collider2D>();

    // 供宿主画 Gizmos
    public bool HitboxActive { get; private set; }
    public Vector2 GizmoCenter { get; private set; }
    public Vector2 GizmoSize { get; private set; }

    // 打断时要收起视觉，所以得记住当前用的是哪一个
    private BossMeleeVisual activeVisual;

    /// <summary>
    /// 按顺序跑完整套挥击。
    ///
    /// 时序（视觉与判定共用同一组 SO 参数）：
    ///   windupTime      判定未生效｜手出现并逐渐举高
    ///   hitboxDuration  判定生效  ｜手落回判定框
    ///   recoverTime     判定结束  ｜手停在原地不动（后摇）
    ///   → 下一段 / 结束        ｜手消失
    /// </summary>
    public IEnumerator Run(
        List<MeleeSwing> swings,
        bool faceEachSwing,
        BossContext ctx,
        Transform owner,
        ContactFilter2D filter,
        Animator animator,
        string defaultActiveAnim,
        GameObject instigator,
        BossMeleeVisual visual = null)
    {
        if (swings == null || swings.Count == 0) yield break;

        activeVisual = visual;

        // 【数据快照】拍下开打瞬间的朝向。
        // 不这么做的话，判定框会在挥击过程中随玩家左右横跳。
        float facing = ResolveFacing(ctx, 1f);

        for (int i = 0; i < swings.Count; i++)
        {
            MeleeSwing swing = swings[i];
            if (swing == null) continue;

            // 追踪型连招：每段重新取一次朝向
            if (faceEachSwing) facing = ResolveFacing(ctx, facing);

            // --- 前摇：手出现并举高 ---
            // 有视觉时由它 yield 掉 windupTime，没有时用 WaitForSeconds。
            // 两条路径耗时相同，判定时序不受视觉存在与否影响。
            if (visual != null)
            {
                yield return visual.Windup(
                    HitboxCenter(owner, swing, facing),
                    swing.hitboxSize,
                    swing.windupTime);
            }
            else if (swing.windupTime > 0f)
            {
                yield return new WaitForSeconds(swing.windupTime);
            }

            PlayAnim(animator, string.IsNullOrEmpty(swing.swingAnimName)
                ? defaultActiveAnim
                : swing.swingAnimName);

            // --- 判定：手落回判定框 ---
            visual?.Strike(HitboxCenter(owner, swing, facing), swing.hitboxSize);

            yield return RunHitbox(swing, facing, ctx, owner, filter, instigator, visual);

            // --- 后摇：手停在原地不动 ---
            // 注意这里**不调用 Hide()** —— 砸完了手还压在地上，
            // 直到后摇走完才抬起来，这段硬直对玩家是可读的输出窗口。
            if (swing.recoverTime > 0f)
                yield return new WaitForSeconds(swing.recoverTime);

            // 后摇结束才收起
            visual?.Hide();
        }

        activeVisual = null;
    }

    private IEnumerator RunHitbox(
        MeleeSwing swing, float facing, BossContext ctx,
        Transform owner, ContactFilter2D filter, GameObject instigator,
        BossMeleeVisual visual)
    {
        alreadyHit.Clear();
        HitboxActive = true;

        float elapsed = 0f;

        do
        {
            // 每帧重算判定框位置，支持边移动边挥砍
            Vector2 center = HitboxCenter(owner, swing, facing);

            GizmoCenter = center;
            GizmoSize = swing.hitboxSize;

            visual?.Follow(center);

            Physics2D.OverlapBox(center, swing.hitboxSize, 0f, filter, hitBuffer);

            HitboxUtility.ApplyDamage(
                hitBuffer,
                alreadyHit,           // 同一段挥击内每个目标只打一次
                swing.damage,
                DamageType.Melee,
                owner.position,       // 来源坐标，受击方据此算击退方向
                instigator,
                ctx.controller);

            // 策划填 0 表示瞬间判定，只跑一次
            if (swing.hitboxDuration <= 0f) break;

            yield return null;
            elapsed += Time.deltaTime;

        } while (elapsed < swing.hitboxDuration);

        HitboxActive = false;
    }

    /// <summary>判定框中心。offset.x 会自动乘朝向，所以 SO 里填正数即可。</summary>
    private Vector2 HitboxCenter(Transform owner, MeleeSwing swing, float facing)
    {
        return (Vector2)owner.position
               + new Vector2(swing.hitboxOffset.x * facing, swing.hitboxOffset.y);
    }

    public void Cancel()
    {
        alreadyHit.Clear();
        HitboxActive = false;

        // 关键：打断时必须收起方块，否则 Boss 躺下了「手」还压在地上
        activeVisual?.Hide();
        activeVisual = null;
    }

    private float ResolveFacing(BossContext ctx, float fallback)
    {
        float sign = ctx.HorizontalSignToPlayer;
        return sign != 0f ? sign : fallback;
    }

    private void PlayAnim(Animator animator, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;
        animator.Play(stateName);
    }
}
