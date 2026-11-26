using System.Collections;
using UnityEngine;

/// <summary>
/// Boss 时钟：真实时间 × 当前阶段节奏表的动作倍率（Tempo Config 的 Action Speed）。
/// 招式执行器里所有「等多久」「计时到多久」都用它，这样调一个倍率就能整体加快 / 放慢 Boss 出招。
/// 走路、冷却、被击退、眩晕、子弹飞行不用它（仍按真实时间）。
/// </summary>
public static class BossTime
{
    /// <summary>当前动作倍率（没有 Boss 时 = 1）</summary>
    public static float Speed(BossController boss) => boss != null ? boss.ActionSpeed : 1f;

    /// <summary>这一帧的 Boss 时间（Update 里用）</summary>
    public static float Delta(BossController boss) => Time.deltaTime * Speed(boss);

    /// <summary>这一物理帧的 Boss 时间（FixedUpdate / WaitForFixedUpdate 循环里用）</summary>
    public static float FixedDelta(BossController boss) => Time.fixedDeltaTime * Speed(boss);

    /// <summary>按 Boss 时间等待（代替 WaitForSeconds）。等待中倍率变了会立刻按新倍率走</summary>
    public static IEnumerator Wait(BossController boss, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            yield return null;
            t += Delta(boss);
        }
    }
}
