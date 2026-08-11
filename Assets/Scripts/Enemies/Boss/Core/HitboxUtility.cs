using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 判定与伤害结算的共用工具。
///
/// 【为什么要抽出来】
/// 近战、激光、冲刺都要做同一件事：框内找目标 → 拿 IDamageable → 发 DamageInfo → 防自伤。
/// 不抽的话这段逻辑会复制三份，以后改伤害通路（比如给 DamageInfo 加元素属性）
/// 就要改三个地方，迟早漏一个。
///
/// 所有方法都用调用方传入的容器，不在内部分配，符合零分配要求。
/// </summary>
public static class HitboxUtility
{
    /// <summary>
    /// 对 hits 里的碰撞体结算伤害。
    /// </summary>
    /// <param name="blacklist">命中去重表。传 null 表示不去重（激光的周期性伤害用）。</param>
    /// <param name="selfToSkip">跳过自己，防自伤。通常传 BossController。</param>
    /// <returns>本次实际造成伤害的目标数。</returns>
    public static int ApplyDamage(
        List<Collider2D> hits,
        HashSet<Collider2D> blacklist,
        int damage,
        DamageType type,
        Vector2 sourcePosition,
        GameObject instigator,
        object selfToSkip)
    {
        if (hits == null) return 0;

        int count = 0;

        for (int i = 0; i < hits.Count; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null) continue;

            if (blacklist != null)
            {
                if (blacklist.Contains(hit)) continue;
                blacklist.Add(hit);
            }

            // 碰撞体常挂在子物体上，必须往父级找
            IDamageable target = hit.GetComponentInParent<IDamageable>();
            if (target == null) continue;

            // 防自伤：Boss 自己也实现了 IDamageable
            if (selfToSkip != null && ReferenceEquals(target, selfToSkip)) continue;

            target.TakeDamage(new DamageInfo(damage, type, sourcePosition, instigator));
            count++;
        }

        return count;
    }

    /// <summary>构造一个只认指定图层的过滤器。</summary>
    public static ContactFilter2D BuildFilter(LayerMask layers, bool includeTriggers)
    {
        return new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = layers,
            useTriggers = includeTriggers
        };
    }

    /// <summary>
    /// 沿一条射线求实际可达长度。用于激光被墙挡住时截断。
    /// 没有障碍时返回 maxDistance。
    /// </summary>
    public static float RaycastLength(Vector2 origin, Vector2 direction, float maxDistance, LayerMask obstacles)
    {
        if (obstacles.value == 0) return maxDistance;

        RaycastHit2D hit = Physics2D.Raycast(origin, direction, maxDistance, obstacles);
        return hit.collider != null ? hit.distance : maxDistance;
    }

    /// <summary>前方是否有障碍。冲刺撞墙判定用。</summary>
    public static bool IsBlocked(Vector2 origin, Vector2 direction, float distance, LayerMask obstacles)
    {
        if (obstacles.value == 0) return false;
        return Physics2D.Raycast(origin, direction, distance, obstacles).collider != null;
    }
}
