<<<<<<< Updated upstream
=======
﻿using System;
>>>>>>> Stashed changes
using UnityEngine;

/// <summary>环形：一圈均匀分布的子弹同时向外飞</summary>
[Serializable, BulletPatternInfo("环形 Circle")]
public class CirclePattern : BulletPatternBase
{
    [Header("--- 环形专属 ---")]
    [Tooltip("一圈多少颗")]
    [Min(1)]
    public int bulletCount = 18;

    [Tooltip("出生点 Y 偏移（相对发射点）")]
    public float offsetY = 0f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector3 spawnPos = ctx.firePoint.position + new Vector3(0f, offsetY, 0f);
        float angleStep = 360f / bulletCount;

        for (int i = 0; i < bulletCount; i++)
        {
            float a = i * angleStep * Mathf.Deg2Rad;
            ctx.host.SpawnBullet(this, new Vector2(Mathf.Cos(a), Mathf.Sin(a)), spawnPos);
        }
    }
}
