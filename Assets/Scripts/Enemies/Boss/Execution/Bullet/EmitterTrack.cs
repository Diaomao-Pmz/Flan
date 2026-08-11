using UnityEngine;

/// <summary>
/// 一条弹幕发射轨道。
///
/// 【它解决什么】
/// 原先 Emitter 的运行时状态只有一份（currentAttackType / timer / isShooting / angle），
/// 结构上只能同时跑一种弹幕。把这些字段打包成对象后，Emitter 就能持有 N 条轨道，
/// 每条有自己的节奏、时长和旋转累加器 —— 单头打印机变成多头绘图仪。
///
/// 【angle 必须放在这里】
/// 它原先是 Emitter 上的共享字段。一旦两条 Rotation 轨道并发（例如双螺旋弹幕），
/// 它们会抢同一个累加器，导致两条螺旋互相干扰、角度乱跳。
/// 这个问题在单轨时代不存在，是多轨化必然带出来的。
///
/// 本类是普通 class 而非 MonoBehaviour，由 Emitter 复用，不产生每次发射的 GC。
/// </summary>
public class EmitterTrack
{
    public BossAttackType type;

    /// <summary>发射间隔（秒）。由 phase 覆盖值或 Emitter 的形态默认值决定。</summary>
    public float interval;

    /// <summary>本轨持续时长（秒）。小于等于 0 表示不限时，直到 StopAttack。</summary>
    public float duration;

    /// <summary>相对组合开始的延迟（秒）。全填 0 = 并发；递增 = 序列。</summary>
    public float startDelay;

    /// <summary>阵型托管时长，透传给 ShapeFormationController。</summary>
    public float formationDuration;

    /// <summary>本轨独立的旋转累加器，仅 Rotation 形态使用。</summary>
    public float angle;

    public bool IsFinished { get; private set; }

    private float delayTimer;
    private float shotTimer;
    private float elapsed;
    private bool started;

    /// <summary>复用前重置。所有字段都要覆盖，绝不把脏数据带进下一次组合。</summary>
    public void Setup(BossAttackType type, float startDelay, float duration,
                      float interval, float formationDuration)
    {
        this.type = type;
        this.startDelay = startDelay;
        this.duration = duration;
        this.interval = Mathf.Max(interval, 0.01f); // 防止 0 间隔导致每帧狂喷
        this.formationDuration = formationDuration;

        angle = 0f;
        delayTimer = 0f;
        shotTimer = 0f;
        elapsed = 0f;
        started = false;
        IsFinished = false;
    }

    public void Finish() => IsFinished = true;

    /// <summary>推进一帧。返回本帧是否应当发射。</summary>
    public bool Tick(float deltaTime)
    {
        if (IsFinished) return false;

        // --- 起始延迟阶段 ---
        if (!started)
        {
            delayTimer += deltaTime;
            if (delayTimer < startDelay) return false;

            started = true;
            // 与原先 StartAttack 里 timer = interval 的行为一致：延迟结束后立刻开第一发
            shotTimer = interval;
        }

        // --- 时长判定 ---
        elapsed += deltaTime;
        if (duration > 0f && elapsed >= duration)
        {
            IsFinished = true;
            return false;
        }

        // --- 发射节奏 ---
        shotTimer += deltaTime;
        if (shotTimer >= interval)
        {
            shotTimer = 0f;
            return true;
        }

        return false;
    }
}
