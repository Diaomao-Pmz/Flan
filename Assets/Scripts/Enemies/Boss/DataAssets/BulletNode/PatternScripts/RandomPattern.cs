using UnityEngine;

[CreateAssetMenu(fileName = "NewRandomPattern", menuName = "ScriptableObjects/BulletPattern/Random")]
public class RandomPattern : BulletPatternBase
{
    [Header("--- Random ---")]
    public float randomBulletSpeed = 10f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector2 dir = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
        ctx.host.SpawnProjectile(dir, ctx.firePoint.position, ctx.projectileKey, randomBulletSpeed);
    }
}
