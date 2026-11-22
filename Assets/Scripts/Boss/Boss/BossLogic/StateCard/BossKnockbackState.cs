using UnityEngine;

/// <summary>
/// 【受击硬直 / 被击退】被玩家的招式打中（见 BossController.HandleBreakAndKnockback）：
///   · 有击退距离：往远离玩家的方向滑一段精确距离，速度由快到慢（有惯性）
///   · 有硬直时间：这段时间里什么都不做
///   两者都有时边滑边硬直，整个状态持续 max(滑行时间, 硬直时间)，结束后回到移动状态重新做决定。
///
/// 距离 / 硬直由攻击方的招式决定（ComboNode.bossKnockbackDistance / bossHitStun），
/// 滑多久由 Boss 自己决定（BossController.knockbackDuration）—— 攻击方描述意图，受击方决定表现。
/// 在物理帧里按匀减速推进，最后一个物理帧只走剩下的那一点，总距离和帧率无关。撞墙会被物理挡住。
/// </summary>
public class BossKnockbackState : IState
{
    private readonly BossController boss;
    private readonly Rigidbody2D rb;

    private float direction = 1f;   // +1 往右滑 / -1 往左滑
    private float distance;
    private float duration = 0.25f;

    private float stunTime;         // 硬直时长（0 = 只滑不额外硬直）

    private float timer;            // 滑行计时（物理帧）
    private float stateTimer;       // 整个状态计时（画面帧）
    private float startSpeed;
    private float deceleration;

    public BossKnockbackState(BossController bc)
    {
        boss = bc;
        rb = boss.GetComponent<Rigidbody2D>();
    }

    /// <summary>进入前调用：这次往哪滑、滑多远、滑多久、硬直多久</summary>
    public void Setup(float dir, float dist, float slideTime, float stun)
    {
        direction = dir < 0f ? -1f : 1f;
        distance = Mathf.Max(0f, dist);
        duration = distance > 0f ? Mathf.Max(Time.fixedDeltaTime, slideTime) : 0f;
        stunTime = Mathf.Max(0f, stun);
    }

    /// <summary>整个状态持续多久 = max(滑行时间, 硬直时间)</summary>
    private float TotalTime => Mathf.Max(duration, stunTime);

    public void Enter()
    {
        timer = 0f;
        stateTimer = 0f;

        // 匀减速到 0：平均速度 = 距离 / 时长，初速度是它的两倍
        startSpeed = duration > 0f ? 2f * distance / duration : 0f;
        deceleration = duration > 0f ? startSpeed / duration : 0f;

        if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        boss.SetMovementStatus(distance > 0f ? "[移动] 被击退" : "[移动] 受击硬直");
    }

    public void FixedUpdate()
    {
        if (rb == null) return;

        if (timer >= duration)
        {
            // 滑完了（或本来就不滑）：硬直剩下的时间原地不动
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        float dt = Time.fixedDeltaTime;
        float step = Mathf.Min(dt, duration - timer);

        float v0 = Mathf.Max(0f, startSpeed - deceleration * timer);
        timer += step;
        float v1 = Mathf.Max(0f, startSpeed - deceleration * timer);

        // 这一步的平均速度；最后一步只走剩下的那一点
        float avg = (v0 + v1) * 0.5f;
        rb.linearVelocity = new Vector2(direction * avg * (step / dt), rb.linearVelocity.y);
    }

    public void Update()
    {
        stateTimer += Time.deltaTime;

        // 滑完、并且硬直时间也到了
        if (timer >= duration - 0.00001f && stateTimer >= TotalTime) boss.ChangeState(boss.MoveState);
    }

    public void Exit()
    {
        if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }
}
