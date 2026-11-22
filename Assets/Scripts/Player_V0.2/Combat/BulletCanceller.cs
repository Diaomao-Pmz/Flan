using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>招式用哪些方式抵消 Boss 的子弹（可多选）</summary>
    [Flags]
    public enum BulletCancelMode
    {
        None = 0,

        [InspectorName("子弹")]
        Projectile = 1 << 0,

        [InspectorName("近战判定")]
        Melee = 1 << 1,

        [InspectorName("位移")]
        Thrust = 1 << 2,
    }

    /// <summary>
    /// 【消弹】找出 Boss 的子弹（Enemy_Projectile），在它的位置播消弹特效，然后回收进对象池。
    /// 用的是主动查询，不看物理图层碰撞矩阵：玩家子弹和 Boss 子弹的图层之间就算不碰撞也能消。
    /// </summary>
    public static class BulletCanceller
    {
        private static readonly List<Collider2D> overlapBuffer = new List<Collider2D>(32);
        private static readonly List<RaycastHit2D> castBuffer = new List<RaycastHit2D>(32);

        private static ContactFilter2D Filter
        {
            get
            {
                ContactFilter2D f = ContactFilter2D.noFilter;
                f.useTriggers = true;   // Boss 子弹是触发器
                return f;
            }
        }

        /// <summary>这个碰撞体是不是一颗还在场上的 Boss 子弹</summary>
        public static bool TryGetEnemyBullet(Component c, out Enemy_Projectile bullet)
        {
            bullet = c != null ? c.GetComponentInParent<Enemy_Projectile>() : null;
            return bullet != null && bullet.gameObject.activeInHierarchy;
        }

        /// <summary>消掉这颗 Boss 子弹：在它的位置播消弹特效，然后回收</summary>
        public static void Cancel(Enemy_Projectile bullet)
        {
            if (bullet == null || !bullet.gameObject.activeInHierarchy) return;

            EffectManager.Instance?.PlayBulletCancel(bullet.transform.position);

            if (ObjectPoolManager.Instance != null) ObjectPoolManager.Instance.Recycle(bullet.gameObject);
            else bullet.gameObject.SetActive(false);
        }

        /// <summary>消掉压在这个刚体（它身上所有碰撞体）上的 Boss 子弹。返回消了几颗</summary>
        public static int CancelOverlapping(Rigidbody2D body)
        {
            if (body == null) return 0;

            overlapBuffer.Clear();
            body.Overlap(Filter, overlapBuffer);
            return CancelColliders(overlapBuffer);
        }

        /// <summary>
        /// 消掉这个碰撞体此刻压着的，以及沿 direction 往前扫 distance 这一路上碰到的 Boss 子弹。
        /// 子弹飞得快时只查当前位置会漏掉两帧之间的小子弹，所以连路上一起扫。返回消了几颗
        /// </summary>
        public static int CancelAlong(Collider2D col, Vector2 direction, float distance)
        {
            if (col == null || !col.enabled) return 0;

            overlapBuffer.Clear();
            col.Overlap(Filter, overlapBuffer);
            int n = CancelColliders(overlapBuffer);

            if (distance > 0.0001f && direction.sqrMagnitude > 0.0001f)
            {
                castBuffer.Clear();
                col.Cast(direction.normalized, Filter, castBuffer, distance, true);
                for (int i = 0; i < castBuffer.Count; i++)
                {
                    if (!TryGetEnemyBullet(castBuffer[i].collider, out Enemy_Projectile b)) continue;
                    Cancel(b);
                    n++;
                }
            }

            return n;
        }

        /// <summary>消掉这个方框（世界坐标中心 + 大小，不旋转）里的 Boss 子弹。返回消了几颗</summary>
        public static int CancelInBox(Vector2 center, Vector2 size)
        {
            if (size.x <= 0f || size.y <= 0f) return 0;

            overlapBuffer.Clear();
            Physics2D.OverlapBox(center, size, 0f, Filter, overlapBuffer);
            return CancelColliders(overlapBuffer);
        }

        private static int CancelColliders(List<Collider2D> list)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!TryGetEnemyBullet(list[i], out Enemy_Projectile b)) continue;
                Cancel(b);
                n++;
            }
            return n;
        }
    }
}
