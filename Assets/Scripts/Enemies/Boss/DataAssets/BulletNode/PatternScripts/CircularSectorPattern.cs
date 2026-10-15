using System;
using UnityEngine;

/// <summary>扇形：以玩家方向为中心张开一个扇面，每条射线上可以叠几层不同速度的子弹</summary>
[Serializable, BulletPatternInfo("扇形 CircularSector")]
public class CircularSectorPattern : BulletPatternBase
{
    [Header("--- 扇形专属 ---")]
    [Tooltip("扇面分成几段（实际射线数 = 段数 + 1）")]
    [Min(1)]
    public int sectionCount = 10;

    [Tooltip("扇面张开的角度（度），以玩家方向为中心")]
    public float angleRange = 90f;

    [Tooltip("子弹从离发射点多远的地方生成。负数 = 从反方向生成")]
    public float radius = 0f;

    [Tooltip("出生点 Y 偏移（相对发射点）")]
    public float offsetY = 0f;

    [Tooltip("每条射线上叠几层子弹、每层的速度。\n" +
             "留空 = 每条射线一颗，用通用设置里的子弹速度")]
    public float[] layerSpeeds = new float[0];

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector3 spawnPos = ctx.firePoint.position + new Vector3(0f, offsetY, 0f);
        float angleStep = angleRange / sectionCount;
        float targetAngle = Vector2.SignedAngle(Vector2.right, ctx.playerTargetPosition - (Vector2)spawnPos);
        float startAngle = targetAngle - angleRange / 2f;

        for (int i = 0; i <= sectionCount; i++)
        {
            float a = (startAngle + i * angleStep) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector3 pos = spawnPos + (Vector3)(radius * dir);

            if (layerSpeeds == null || layerSpeeds.Length == 0)
            {
                ctx.host.SpawnBullet(this, dir, pos);
                continue;
            }

            for (int j = 0; j < layerSpeeds.Length; j++)
                ctx.host.SpawnBullet(this, dir, pos, layerSpeeds[j]);
        }
    }
}
