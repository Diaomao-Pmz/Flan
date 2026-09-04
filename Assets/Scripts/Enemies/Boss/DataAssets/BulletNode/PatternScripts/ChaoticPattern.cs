using UnityEngine;

[CreateAssetMenu(fileName = "NewChaoticPattern", menuName = "ScriptableObjects/BulletPattern/Chaotic")]
public class ChaoticPattern : BulletPatternBase
{
    [Header("--- Chaos ---")]
    public float minSpeed;
    public float maxSpeed;
    public int bulletCountPerBurst;

    public override void Spawn(BulletSpawnContext ctx)
    {
        for (int i = 0; i < bulletCountPerBurst; i++)
        {
            float speed = Random.Range(minSpeed, maxSpeed);
            Vector2 dir = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
            ctx.host.SpawnProjectile(dir, ctx.firePoint.position, ctx.projectileKey, speed);
        }
    }
}