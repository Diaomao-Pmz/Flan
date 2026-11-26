using UnityEngine;

/// <summary>
/// 【Boss 冲刺的终点裁判】—— 规则和玩家那边的 DashEndpointGuard 一样，只是对象反过来：
/// 每个物理帧用 Boss 此刻的位置问一次「按剩下的路冲完，会不会停在玩家身体里？」
///
///   终点不和玩家重叠，或只重叠不超过「重叠容差」          → 照常冲完
///   重叠，这张卡能穿过玩家，且终点已穿进玩家宽度的「穿过比例」以上 → 送到玩家身后贴着（会多走一小段）
///   其余情况                                            → 停在玩家面前，两个碰撞箱边贴边
///   起步时已经和玩家重叠（贴脸）                          → 不拦，让 Boss 冲出来
///
/// 玩家和 Boss 的同类功能分开写（用户 11-21 的要求），所以这里是 Boss 自己的一份。
/// 计算只用长方形（碰撞体的包围盒），开销几乎为零。
/// </summary>
public static class BossDashEndpointGuard
{
    public enum Verdict
    {
        Clear,        // 终点不落在玩家身体里：照常冲
        StopInFront,  // 停在玩家面前
        PassBehind,   // 穿到玩家身后
    }

    /// <summary>停在面前 / 穿到身后时，和玩家之间留的一丝缝（格）</summary>
    private const float Skin = 0.01f;

    /// <summary>从现在起，这一段冲刺最多（或至少）还应该走多远</summary>
    /// <param name="body">Boss 身体碰撞箱此刻的范围</param>
    /// <param name="dir">冲刺方向（不必是单位向量）</param>
    /// <param name="remaining">按原计划还剩多远（格）</param>
    /// <param name="player">玩家身体碰撞箱</param>
    /// <returns>允许再走的距离：小于 remaining = 停在面前；大于 = 穿到身后</returns>
    public static float Resolve(
        Bounds body, Vector2 dir, float remaining, Collider2D player,
        bool canPass, float passRatio, float overlapTolerance, out Verdict verdict)
    {
        verdict = Verdict.Clear;
        remaining = Mathf.Max(0f, remaining);

        if (player == null || !player.enabled) return remaining;
        if (dir.sqrMagnitude < 0.000001f) return remaining;
        dir.Normalize();

        Vector2 c = body.center;
        Vector2 half = body.extents;
        Vector2 end = c + dir * remaining;

        Bounds b = player.bounds;
        Vector2 bc = b.center;

        // Boss 中心能到的「禁区」：玩家的框向外扩 Boss 半个身子（两个框重叠 ⇔ Boss 中心落在禁区里）
        Vector2 eh = (Vector2)b.extents + half;

        // 终点是否落在玩家身体里（扣掉容差：只擦到一点边不算）
        float tol = Mathf.Max(0f, overlapTolerance);
        Vector2 d = end - bc;
        bool endInside = Mathf.Abs(d.x) < eh.x - tol && Mathf.Abs(d.y) < eh.y - tol;
        if (!endInside) return remaining;

        if (!RayThroughBox(c, dir, bc, eh, out float tEnter, out float tExit)) return remaining;

        // 终点穿进去多深（0 = 刚碰到前沿，1 = 到了后沿）
        float span = tExit - tEnter;
        float depth = span > 0.0001f ? (remaining - tEnter) / span : 1f;

        if (canPass && depth >= Mathf.Clamp01(passRatio))
        {
            verdict = Verdict.PassBehind;
            return tExit + Skin;
        }

        if (tEnter >= 0f)
        {
            verdict = Verdict.StopInFront;
            return Mathf.Max(0f, tEnter - Skin);
        }

        // 起步时（或玩家走过来时）已经在玩家身体里了：不拦
        return remaining;
    }

    /// <summary>从 origin 沿 dir（单位向量）出发的射线，进出一个轴对齐框的距离。起点在框里时 tEnter 为负</summary>
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
