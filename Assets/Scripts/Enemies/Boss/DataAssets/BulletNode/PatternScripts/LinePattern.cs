using System;
using UnityEngine;

/// <summary>直线：朝玩家瞄准，一次一颗</summary>
[Serializable, BulletPatternInfo("直线瞄准 Line")]
public class LinePattern : BulletPatternBase
{
    [Header("--- 直线专属 ---")]
    [Tooltip("瞄准点 Y 偏移：瞄准玩家受击点上方(+)或下方(-)")]
    public float aimOffsetY = 0f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector3 spawnPos = ctx.firePoint.position;
        Vector2 dir = Vector2.left;

        if (ctx.player != null)
        {
            Vector2 target = ctx.playerTargetPosition + new Vector2(0f, aimOffsetY);
            Vector2 delta = target - (Vector2)spawnPos;
            if (delta.sqrMagnitude > 0.0001f) dir = delta.normalized;
        }

        ctx.host.SpawnBullet(this, dir, spawnPos);
    }
}
