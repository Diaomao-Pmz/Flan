using System;
using UnityEngine;

/// <summary>混沌：一次喷出一大把随机方向、随机速度的子弹（通用设置里的子弹速度不使用）</summary>
[Serializable, BulletPatternInfo("混沌 Chaotic")]
public class ChaoticPattern : BulletPatternBase
{
    [Header("--- 混沌专属 ---")]
    [Tooltip("每次发射多少颗")]
    [Min(1)]
    public int bulletCountPerBurst = 20;

    [Tooltip("速度随机下限（本图案用这个范围代替通用的子弹速度）")]
    public float minSpeed = 1f;

    [Tooltip("速度随机上限")]
    public float maxSpeed = 3f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        for (int i = 0; i < bulletCountPerBurst; i++)
        {
            float speed = UnityEngine.Random.Range(minSpeed, maxSpeed);
            Vector2 dir = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)).normalized;
            GameObject bullet = ctx.host.SpawnBullet(this, dir, ctx.firePoint.position, speed);
            AddComponents(bullet);
        }
    }
}
