using System;
using UnityEngine;

/// <summary>旋转：发射方向从起始角度匀速转到结束角度，转完从头再来（螺旋弹幕）</summary>
[Serializable, BulletPatternInfo("旋转 Rotation")]
public class RotationPattern : BulletPatternBase
{
    [Header("--- 旋转专属 ---")]
    [Tooltip("开始发射的角度（度）。0 = 正右，90 = 正上")]
    public float initialAngle = 0f;

    [Tooltip("转到这个角度后，重新从起始角度开始。比起始角度小 = 顺时针转")]
    public float endAngle = 360f;

    [Tooltip("从起始角度转到结束角度用多久（秒）")]
    [Min(0f)]
    public float rotationDuration = 3f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        EmitterTrack track = ctx.activeTrack;
        if (track == null) return;

        float currentAngle = float.IsNaN(track.angle) ? initialAngle : track.angle;

        Vector2 dir = Quaternion.Euler(0f, 0f, currentAngle) * Vector2.right;
        ctx.host.SpawnBullet(this, dir, ctx.firePoint.position);

        // ---- 下一发的角度 ----
        float totalAngle = endAngle - initialAngle;
        if (rotationDuration <= 0f || Mathf.Approximately(totalAngle, 0f))
        {
            track.angle = initialAngle;
            return;
        }

        // 每发转多少度 = 总角度 × (发射间隔 / 转一圈用时)
        float next = currentAngle + totalAngle * (track.interval / rotationDuration);
        bool passed = totalAngle > 0f ? next >= endAngle : next <= endAngle;
        track.angle = passed ? initialAngle : next;
    }
}
