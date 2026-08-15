using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【目标搜索】—— 挂在玩家身上，回答"附近有谁"。
    ///
    /// ==========================================================
    /// 三个需求共用这一个组件：
    ///   滑铲攻击的自动朝向  —— 命中后转向那个敌人
    ///   蓄力突刺的目标锁定  —— dash 途中第一个碰到的敌人
    ///   （以后）锁定型招式、辅助瞄准
    ///
    /// 单独抽出来是因为"找敌人"和"打敌人"是两件事。
    /// 塞进 PlayerHitDetection 会让判定框组件同时兼职索敌，
    /// 而索敌以后大概率要加优先级规则（血量最低？威胁最大？屏幕内？），
    /// 那些逻辑不该和判定框搅在一起。
    /// ==========================================================
    ///
    /// 【零分配】内部用 ContactFilter2D + 复用 List，
    /// 每帧调用也不会产生 GC —— 符合 readme 的零分配要求。
    /// </summary>
    public class TargetFinder : MonoBehaviour
    {
        [Header("搜索设置")]
        [Tooltip("哪些图层算敌人。留空则退化为按 Tag「Enemy」判断")]
        public LayerMask enemyLayer;

        [Tooltip("默认搜索半径")]
        public float defaultSearchRadius = 12f;

        [Tooltip("勾选后额外按 Tag 校验，防止图层配错时误锁定场景物件")]
        public bool alsoCheckTag = true;

        [Tooltip("敌人 Tag")]
        public string enemyTag = "Enemy";

        [Header("Debug")]
        public bool drawGizmos = false;

        // 复用缓冲，避免每次查询都 new
        private readonly List<Collider2D> results = new List<Collider2D>(32);
        private ContactFilter2D filter;

        private Transform lastFoundTarget;

        private void Awake()
        {
            filter = ContactFilter2D.noFilter;
            filter.useTriggers = true;   // 敌人的受击框通常是 Trigger

            if (enemyLayer.value != 0)
            {
                filter.SetLayerMask(enemyLayer);
                filter.useLayerMask = true;
            }
        }

        // ==========================================================
        // 查询
        // ==========================================================

        /// <summary>
        /// 找离 from 最近的敌人。找不到返回 null。
        /// </summary>
        /// <param name="radius">搜索半径。传负数则用 defaultSearchRadius</param>
        public Transform FindNearest(Vector2 from, float radius = -1f)
        {
            float r = radius > 0f ? radius : defaultSearchRadius;

            results.Clear();
            Physics2D.OverlapCircle(from, r, filter, results);

            Transform best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < results.Count; i++)
            {
                Collider2D c = results[i];
                if (!IsValidEnemy(c)) continue;

                float sqr = ((Vector2)c.transform.position - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = ResolveRoot(c);
                }
            }

            lastFoundTarget = best;
            return best;
        }

        /// <summary>
        /// 在指定方向的扇形范围内找最近的敌人。
        /// 用于"朝我面向的方向锁定"这类场景，避免锁到背后的敌人。
        /// </summary>
        /// <param name="halfAngle">扇形半角（度）。90 表示整个前方半圆</param>
        public Transform FindNearestInDirection(
            Vector2 from, Vector2 direction, float halfAngle = 60f, float radius = -1f)
        {
            float r = radius > 0f ? radius : defaultSearchRadius;
            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            float cosLimit = Mathf.Cos(halfAngle * Mathf.Deg2Rad);

            results.Clear();
            Physics2D.OverlapCircle(from, r, filter, results);

            Transform best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < results.Count; i++)
            {
                Collider2D c = results[i];
                if (!IsValidEnemy(c)) continue;

                Vector2 delta = (Vector2)c.transform.position - from;
                if (delta.sqrMagnitude < 0.0001f) continue;

                // 点积判断是否落在扇形内，比算角度快
                if (Vector2.Dot(delta.normalized, dir) < cosLimit) continue;

                float sqr = delta.sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = ResolveRoot(c);
                }
            }

            lastFoundTarget = best;
            return best;
        }

        /// <summary>目标在我左边还是右边。1 = 右，-1 = 左。目标为空时返回 0</summary>
        public int GetFacingTowards(Transform target)
        {
            if (target == null) return 0;

            float dx = target.position.x - transform.position.x;
            if (Mathf.Abs(dx) < 0.01f) return 0;   // 重叠时不强行转身，避免抖动

            return dx > 0f ? 1 : -1;
        }

        /// <summary>最近一次查询找到的目标。仅供调试与 UI 使用</summary>
        public Transform LastFoundTarget => lastFoundTarget;

        // ==========================================================
        // 内部
        // ==========================================================

        private bool IsValidEnemy(Collider2D c)
        {
            if (c == null) return false;
            if (alsoCheckTag && !c.CompareTag(enemyTag)) return false;

            // 必须真的是个可受伤实体 —— 否则会锁到敌人身上的纯视觉子物体
            return c.GetComponentInParent<EntityBase>() != null;
        }

        /// <summary>判定框常挂在子物体上，转成敌人本体的 Transform</summary>
        private Transform ResolveRoot(Collider2D c)
        {
            EntityBase e = c.GetComponentInParent<EntityBase>();
            return e != null ? e.transform : c.transform;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, defaultSearchRadius);

            if (Application.isPlaying && lastFoundTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, lastFoundTarget.position);
            }
        }
    }
}
