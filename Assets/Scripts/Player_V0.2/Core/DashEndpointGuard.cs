using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【位移终点裁判】—— 冲刺、飞行冲刺、滑铲、招式位移共用。
    ///
    /// ==========================================================
    /// 比喻：不是起跑前看一眼终点线就闭眼冲，而是边跑边看 ——
    ///       每个物理帧都用 Boss 此刻的位置重新问一次：
    ///       "按剩下的路走完，我会不会停在 Boss 身体里？"
    ///
    /// 规则（终点 = 按剩下的路程走完时，玩家身体的位置）：
    ///   终点不和 Boss 重叠，或只重叠不超过「重叠容差」   → 照常走完
    ///   重叠，这个动作能穿身，且终点已穿进 Boss 宽度的「穿过比例」以上 → 送到 Boss 身后贴着（会多走一小段）
    ///   其余情况                                         → 停在 Boss 面前，两个碰撞箱边贴边
    ///   起步时已经和 Boss 重叠（被贴脸）                   → 不拦，让玩家冲出来
    ///
    /// "Boss" = PlayerMovementConfig.passThroughLayers 里那些图层上的【实体】碰撞体（触发器不算）。
    /// 计算只用长方形（碰撞体的包围盒），开销几乎为零。
    /// ==========================================================
    /// </summary>
    public static class DashEndpointGuard
    {
        public enum Verdict
        {
            /// <summary>终点不落在 Boss 身体里：照常走</summary>
            Clear,

            /// <summary>停在 Boss 面前</summary>
            StopInFront,

            /// <summary>穿到 Boss 身后</summary>
            PassBehind,
        }

        /// <summary>停在面前 / 穿到身后时，和 Boss 之间留的一丝缝（格），免得物理引擎认为还贴着</summary>
        private const float Skin = 0.01f;

        private static readonly List<Collider2D> hits = new List<Collider2D>(16);

        /// <summary>
        /// 从现在起，这一段位移最多（或至少）还应该走多远。
        /// </summary>
        /// <param name="body">玩家身体碰撞箱此刻的范围</param>
        /// <param name="dir">位移方向（不必是单位向量）</param>
        /// <param name="remaining">按原计划还剩多远（格）</param>
        /// <param name="canPass">这个动作本身能不能穿过 Boss</param>
        /// <param name="self">玩家自己的刚体（跳过自己的碰撞体）</param>
        /// <returns>允许再走的距离：小于 remaining = 停在面前；大于 = 穿到身后</returns>
        public static float Resolve(
            Bounds body, Vector2 dir, float remaining, bool canPass,
            PlayerMovementConfig cfg, Rigidbody2D self, out Verdict verdict)
        {
            verdict = Verdict.Clear;
            remaining = Mathf.Max(0f, remaining);

            if (cfg == null || !cfg.endpointGuard || cfg.passThroughLayers.value == 0) return remaining;
            if (dir.sqrMagnitude < 0.000001f) return remaining;
            dir.Normalize();

            Vector2 c = body.center;
            Vector2 half = body.extents;
            Vector2 end = c + dir * remaining;

            // 只找路上可能碰到的（路径包围盒 + 身体大小）
            Vector2 pad = half + new Vector2(0.05f, 0.05f);
            Vector2 min = Vector2.Min(c, end) - pad;
            Vector2 max = Vector2.Max(c, end) + pad;

            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = cfg.passThroughLayers,
                useTriggers = false,
            };

            hits.Clear();
            Physics2D.OverlapArea(min, max, filter, hits);

            float tol = Mathf.Max(0f, cfg.endpointOverlapTolerance);
            float passRatio = Mathf.Clamp01(cfg.endpointPassRatio);

            float result = remaining;
            float firstEnter = float.PositiveInfinity;

            for (int i = 0; i < hits.Count; i++)
            {
                Collider2D col = hits[i];
                if (col == null) continue;
                if (self != null && col.attachedRigidbody == self) continue;

                Bounds b = col.bounds;
                Vector2 bc = b.center;

                // 玩家中心能到的"禁区"：Boss 的框向外扩玩家半个身子（两个框重叠 ⇔ 玩家中心落在禁区里）
                Vector2 eh = (Vector2)b.extents + half;

                // 终点是否落在 Boss 身体里（扣掉容差：只擦到一点边不算）
                Vector2 d = end - bc;
                bool endInside = Mathf.Abs(d.x) < eh.x - tol && Mathf.Abs(d.y) < eh.y - tol;
                if (!endInside) continue;

                if (!RayThroughBox(c, dir, bc, eh, out float tEnter, out float tExit)) continue;

                // 终点穿进去多深（0 = 刚碰到前沿，1 = 到了后沿）
                float span = tExit - tEnter;
                float depth = span > 0.0001f ? (remaining - tEnter) / span : 1f;

                Verdict v;
                float allowed;

                if (canPass && depth >= passRatio)
                {
                    v = Verdict.PassBehind;
                    allowed = tExit + Skin;
                }
                else if (tEnter >= 0f)
                {
                    v = Verdict.StopInFront;
                    allowed = Mathf.Max(0f, tEnter - Skin);
                }
                else
                {
                    // 起步时（或 Boss 走过来时）已经在它身体里了：不拦
                    continue;
                }

                // 路上有好几个：以最先碰到的那个为准
                if (tEnter < firstEnter)
                {
                    firstEnter = tEnter;
                    result = allowed;
                    verdict = v;
                }
            }

            return result;
        }

        /// <summary>
        /// 从 origin 沿 dir（单位向量）出发的射线，进出一个轴对齐框的距离。
        /// 起点在框里时 tEnter 为负。
        /// </summary>
        private static bool RayThroughBox(Vector2 origin, Vector2 dir, Vector2 center, Vector2 halfSize,
                                          out float tEnter, out float tExit)
        {
            tEnter = float.NegativeInfinity;
            tExit = float.PositiveInfinity;

            for (int axis = 0; axis < 2; axis++)
            {
                float o = origin[axis];
                float v = dir[axis];
                float lo = center[axis] - halfSize[axis];
                float hi = center[axis] + halfSize[axis];

                if (Mathf.Abs(v) < 0.000001f)
                {
                    // 这个轴不动：起点不在范围内就永远碰不到
                    if (o <= lo || o >= hi) return false;
                    continue;
                }

                float t1 = (lo - o) / v;
                float t2 = (hi - o) / v;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }

                if (t1 > tEnter) tEnter = t1;
                if (t2 < tExit) tExit = t2;
            }

            return tEnter <= tExit;
        }
    }

    /// <summary>
    /// 【受终点裁判管的一段位移】—— 每个位移动作各持有一个。
    ///
    /// 动作自己照旧按速度曲线算"这一帧想走多远"，交给 Step；
    /// Step 每帧问一次裁判，返回这一帧实际允许走多远。
    ///   · 被 Boss 挡住：走到 Boss 面前就不再走（动作自己的时长照常走完）
    ///   · 要穿到 Boss 身后：动作自己的时间到了也继续按平均速度走，直到出来（Reached 才为 true）
    ///   · Boss 走开了：限制自动放开，最多走回原计划的距离
    /// </summary>
    public sealed class GuardedMove
    {
        private const float Eps = 0.0001f;

        private readonly PlayerStateMachine sm;
        private BoxCollider2D body;

        private Vector2 dir;
        private float planned;      // 原计划的总距离
        private float limit;        // 裁判此刻允许的总距离
        private float traveled;     // 已经走了多远
        private float cruiseSpeed;  // 平均速度（穿到身后多走的那一段用它）
        private bool canPass;
        private bool active;

        public GuardedMove(PlayerStateMachine stateMachine)
        {
            sm = stateMachine;
        }

        /// <summary>这一段位移被 Boss 挡住了（会停在 Boss 面前）</summary>
        public bool Blocked => active && limit < planned - Eps;

        /// <summary>允许的距离已经走完（没开始 / 没受管时也算走完）</summary>
        public bool Reached => !active || traveled >= limit - Eps;

        /// <param name="direction">位移方向</param>
        /// <param name="distance">原计划的总距离（格）</param>
        /// <param name="duration">原计划的用时（秒），用来算平均速度</param>
        /// <param name="canPassThrough">这个动作能不能穿过 Boss</param>
        public void Begin(Vector2 direction, float distance, float duration, bool canPassThrough)
        {
            dir = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.zero;
            planned = Mathf.Max(0f, distance);
            limit = planned;
            traveled = 0f;
            cruiseSpeed = planned / Mathf.Max(Time.fixedDeltaTime, duration);
            canPass = canPassThrough;
            active = planned > Eps && dir != Vector2.zero;
        }

        public void End() => active = false;

        /// <summary>
        /// 这一物理帧想走 want 格（按动作自己的速度曲线算，动作时间已到时传 0）。
        /// 返回实际允许走多少。
        /// </summary>
        public float Step(float want)
        {
            want = Mathf.Max(0f, want);
            if (!active) return want;

            if (body == null) body = sm.GetComponent<BoxCollider2D>();
            if (body == null) return want;

            PlayerMovementConfig cfg = sm.playerState != null ? sm.playerState.movementConfig : null;

            float allowed = DashEndpointGuard.Resolve(
                body.bounds, dir, planned - traveled, canPass, cfg, sm.rb, out _);
            limit = traveled + allowed;

            // 动作自己的时间到了，但还要穿到 Boss 身后：按平均速度继续走
            if (want <= 0f && traveled < limit - Eps) want = cruiseSpeed * Time.fixedDeltaTime;

            float step = Mathf.Clamp(want, 0f, Mathf.Max(0f, limit - traveled));
            traveled += step;
            return step;
        }
    }
}
