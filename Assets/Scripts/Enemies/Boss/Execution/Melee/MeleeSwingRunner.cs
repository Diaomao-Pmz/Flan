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
        GameObject instigator)
    {
        if (swings == null || swings.Count == 0) yield break;

        // 【数据快照】拍下开打瞬间的朝向。
        // 不这么做的话，判定框会在挥击过程中随玩家左右横跳。
        float facing = ResolveFacing(ctx, 1f);

        for (int i = 0; i < swings.Count; i++)
        {
            MeleeSwing swing = swings[i];
            if (swing == null) continue;

            // 追踪型连招：每段重新取一次朝向
            if (faceEachSwing) facing = ResolveFacing(ctx, facing);

            if (swing.windupTime > 0f)
                yield return new WaitForSeconds(swing.windupTime);

            PlayAnim(animator, string.IsNullOrEmpty(swing.swingAnimName)
                ? defaultActiveAnim
                : swing.swingAnimName);

            yield return RunHitbox(swing, facing, ctx, owner, filter, instigator);

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

    public void Cancel()
    {
        alreadyHit.Clear();
        HitboxActive = false;
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
