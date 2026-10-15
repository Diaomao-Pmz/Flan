<<<<<<< Updated upstream
=======
﻿using System;
>>>>>>> Stashed changes
using UnityEngine;

/// <summary>随机：每次朝随机方向发一颗</summary>
[Serializable, BulletPatternInfo("随机 Random")]
public class RandomPattern : BulletPatternBase
{
    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector2 dir = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)).normalized;
        ctx.host.SpawnBullet(this, dir, ctx.firePoint.position);
    }
}
