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

    // 本次挥击所属的招式 —— 读它的击退配置
    private ActionNode knockbackNode;

    // 这一段挥击被玩家拼刀了（RunSwing 里发现，Run 据此提前收招）
    private ClashWindow clashedBy;

    /// <summary>
    /// 按顺序跑完整套挥击。
    ///
    /// 每一段挥击逐帧推进：
    ///   windupTime   前摇｜方块出现并逐渐举高（水平挥击：往 Boss 身后拉），灰色
    ///   holdTime     蓄势｜方块举着不动；憋多久在这一段开始时就按 releaseBias 抽好（快慢刀）
    ///   attackTime   攻击｜方块挥向判定框，在判定打开的那一刻正好到达
    ///   recoverTime  后摇｜方块停在判定框上不动
    ///   → 下一段 / 结束  ｜方块消失
    /// 判定只在 hitboxActiveRange（攻击开始后第 X ~ Y 秒）里生效，这段时间方块是红色，其余时间灰色。
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
        BossMeleeVisual visual = null,
        ActionNode sourceNode = null)
    {
        if (swings == null || swings.Count == 0) yield break;

        // 招式自带击退（ActionNode 上配置），整套挥击共用
        knockbackNode = sourceNode;
        clashedBy = null;

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

            yield return RunSwing(swing, facing, ctx, owner, filter, animator, defaultActiveAnim, instigator, visual);

            // --- 被拼刀：这一招到此为止（剩下的挥击、后摇都不打了）---
            // 只在这里收招、不在判定的半路上掐协程：这一帧已经确定不造成伤害，再干净地退出。
            // 套路要不要跟着作废由 Boss 决定（主手 AA1 = 作废，副手 BB1 = 继续下一步）。
            if (clashedBy != null)
            {
                ClashWindow clash = clashedBy;
                clashedBy = null;

                visual?.Hide();
                activeVisual = null;

                if (ctx.controller != null) ctx.controller.OnMeleeClashed(clash.effect, clash.node);
                yield break;
            }

            // 后摇结束才收起
            visual?.Hide();
        }

        activeVisual = null;
    }

    /// <summary>
    /// 跑一段挥击：前摇 → 蓄势（时长开始时就抽好）→ 攻击 + 后摇。
    /// 判定在攻击开始后的 [HitboxStart, HitboxEnd] 之间每帧检测；X 与 Y 相等时只检测一帧。
    /// 被拼刀时记下 clashedBy 并立刻返回（由 Run 收招）。
    /// </summary>
    private IEnumerator RunSwing(
        MeleeSwing swing, float facing, BossContext ctx,
        Transform owner, ContactFilter2D filter,
        Animator animator, string defaultActiveAnim,
        GameObject instigator, BossMeleeVisual visual)
    {
        alreadyHit.Clear();
        HitboxActive = false;

        // 这一刀憋多久：现在就抽好（每次出这一招都重抽）
        float hold = swing.RollHoldTime();

        // ---- 1. 前摇：方块举起 / 往后拉 ----
        float windup = Mathf.Max(0f, swing.windupTime);
        float t = 0f;
        while (t < windup)
        {
            visual?.Pose(HitboxAnchor(owner, swing, facing), swing.hitboxSize,
                         MeleeSwingPose.Windup, t / windup, swing.swingDirection, facing, false);
            yield return null;
            t += Time.deltaTime;
        }

        // ---- 2. 蓄势：举着不动，憋满抽好的时长 ----
        float held = 0f;
        while (held < hold)
        {
            visual?.Pose(HitboxAnchor(owner, swing, facing), swing.hitboxSize,
                         MeleeSwingPose.Hold, 1f, swing.swingDirection, facing, false);
            yield return null;
            held += Time.deltaTime;
        }

        // ---- 3. 攻击 + 后摇（a = 攻击开始后第几秒）----
        PlayAnim(animator, string.IsNullOrEmpty(swing.swingAnimName)
            ? defaultActiveAnim
            : swing.swingAnimName);

        float attack = Mathf.Max(0f, swing.attackTime);
        float after = swing.AfterAttackLength;
        float hitStart = swing.HitboxStart;
        float hitEnd = swing.HitboxEnd;

        // 方块在判定打开的那一刻挥到判定框（判定开在后摇里的话，攻击结束时到）
        float arriveAt = Mathf.Clamp(hitStart, 0f, attack);

        bool hitDone = false;
        float a = 0f;

        while (true)
        {
            // 每帧重算判定框位置，支持边移动边挥砍
            Vector2 anchor = HitboxAnchor(owner, swing, facing);
            Vector2 center = HitboxCenter(owner, swing, facing);

            bool live = !hitDone && a >= hitStart;
            HitboxActive = live;
            if (live)
            {
                GizmoCenter = center;
                GizmoSize = swing.hitboxSize;
            }

            if (a < arriveAt)
                visual?.Pose(anchor, swing.hitboxSize, MeleeSwingPose.Swing, a / arriveAt, swing.swingDirection, facing, live);
            else
                visual?.Pose(anchor, swing.hitboxSize, MeleeSwingPose.Arrived, 1f, swing.swingDirection, facing, live);

            if (live)
            {
                if (CheckHit(swing, center, ctx, owner, filter, instigator))
                {
                    HitboxActive = false;
                    yield break;   // 被拼刀
                }

                // 走到 Y 就关；X == Y 时检测这一帧后立刻关（瞬间判定）
                if (a >= hitEnd) hitDone = true;
            }

            if (a >= after) break;

            yield return null;
            a += Time.deltaTime;
        }

        HitboxActive = false;
    }

    /// <summary>判定生效的这一帧：先看拼刀，再结算伤害。被拼刀返回 true（这一下不造成伤害）</summary>
    private bool CheckHit(
        MeleeSwing swing, Vector2 center, BossContext ctx,
        Transform owner, ContactFilter2D filter, GameObject instigator)
    {
        // 【拼刀】结算伤害之前先看玩家有没有拼刀框挡在这里 —— 挡着就这一下不造成伤害，交给 Run 收招
        if (swing.clashable
            && ClashRegistry.TryClash(center, swing.hitboxSize, owner.position, out ClashWindow clash))
        {
            clashedBy = clash;
            return true;
        }

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

        return false;
    }

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

    public void Cancel()
    {
        alreadyHit.Clear();
        HitboxActive = false;
        clashedBy = null;

        // 关键：打断时必须收起方块，否则 Boss 躺下了「手」还压在地上
        activeVisual?.Hide();
        activeVisual = null;
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
