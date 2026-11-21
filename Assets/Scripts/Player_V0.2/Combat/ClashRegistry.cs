using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>拼刀成功后 Boss 怎么收场（配在玩家的招式 ComboNode 上）</summary>
    public enum ClashEffect
    {
        /// <summary>Boss 这一招剩下的挥击作废，整个套路也作废（主手 AA1）</summary>
        [InspectorName("中断整个套路（主手 AA1）")]
        AbortRoutine,

        /// <summary>只结束 Boss 当前这一招，套路照常进入下一步；玩家急退（副手 BB1）</summary>
        [InspectorName("只结束当前招 + 玩家急退（副手 BB1）")]
        EndCurrentAction,
    }

    /// <summary>一个正在生效的拼刀框（由玩家的攻击实例打开 / 关闭）</summary>
    public sealed class ClashWindow
    {
        /// <summary>开这个框的招式</summary>
        public ComboNode node;

        /// <summary>框跟着谁走（玩家本体）</summary>
        public Transform origin;

        /// <summary>读朝向用：X 偏移乘朝向</summary>
        public PlayerController controller;

        /// <summary>框中心相对 origin 的偏移（X 填正数 = 身前）</summary>
        public Vector2 offset;

        public Vector2 size;

        public ClashEffect effect;

        /// <summary>出招后第几秒关掉（攻击实例的时间）</summary>
        public float closeAt;

        /// <summary>拼刀成功时回调：(这个框, 对手的位置)。由开框的一方填</summary>
        public System.Action<ClashWindow, Vector2> onClash;

        /// <summary>还在名单上</summary>
        public bool open;

        /// <summary>框的世界中心</summary>
        public Vector2 Center
        {
            get
            {
                float f = controller != null && controller.facingDirection < 0 ? -1f : 1f;
                Vector2 o = origin != null ? (Vector2)origin.position : Vector2.zero;
                return o + new Vector2(offset.x * f, offset.y);
            }
        }
    }

    /// <summary>
    /// 【拼刀名单】此刻有哪些拼刀框正在生效。
    ///
    /// 比喻：裁判站在攻击方（Boss）这边。Boss 每一帧结算伤害之前，先来这张名单上看一眼
    /// "玩家有没有举着刀挡在我的拳路上"。挡着就算拼刀：这一下不造成伤害，名单上的那个框用掉，
    /// 并通知玩家那边（后退之类的反应）。没挡着才照常结算伤害。
    ///
    /// 只有 Boss 的近战挥击会来查（MeleeSwingRunner），子弹 / 激光不查。
    /// </summary>
    public static class ClashRegistry
    {
        private static readonly List<ClashWindow> active = new List<ClashWindow>();

        public static IReadOnlyList<ClashWindow> Active => active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() => active.Clear();

        public static void Open(ClashWindow w)
        {
            if (w == null || w.open) return;
            w.open = true;
            active.Add(w);
        }

        public static void Close(ClashWindow w)
        {
            if (w == null || !w.open) return;
            w.open = false;
            active.Remove(w);
        }

        /// <summary>
        /// 攻击方每帧结算伤害【之前】调用：这个判定框有没有撞上某个拼刀框。
        /// 撞上了 → 那个拼刀框用掉（一个框只拼一次）、通知开框的一方，返回 true。
        /// </summary>
        /// <param name="center">攻击判定框的世界中心</param>
        /// <param name="size">攻击判定框尺寸</param>
        /// <param name="attackerPosition">攻击方的位置（玩家急退时据此决定往哪边退）</param>
        public static bool TryClash(Vector2 center, Vector2 size, Vector2 attackerPosition, out ClashWindow hit)
        {
            for (int i = 0; i < active.Count; i++)
            {
                ClashWindow w = active[i];
                if (w == null || w.origin == null) continue;
                if (!Overlaps(center, size, w.Center, w.size)) continue;

                hit = w;
                Close(w);
                w.onClash?.Invoke(w, attackerPosition);
                return true;
            }

            hit = null;
            return false;
        }

        /// <summary>两个轴对齐的框是否重叠</summary>
        public static bool Overlaps(Vector2 c1, Vector2 s1, Vector2 c2, Vector2 s2)
        {
            return Mathf.Abs(c1.x - c2.x) * 2f < Mathf.Abs(s1.x) + Mathf.Abs(s2.x)
                && Mathf.Abs(c1.y - c2.y) * 2f < Mathf.Abs(s1.y) + Mathf.Abs(s2.y);
        }
    }
}
