using UnityEngine;

[CreateAssetMenu(fileName = "NewStarPattern", menuName = "ScriptableObjects/BulletPattern/Star")]
public class StarPattern : BulletPatternBase
{
    [Header("--- Star ---")]
    public int starBulletCount = 10;
    public float starBulletSpeed = 5f;
    public float star_rmin = 2;
    public float star_rmax = 4;
    public float starOffsetY = 0f;
    public bool enableAcceleration = true;
    public float accelerationRate = 1f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector2[] spawnPositions = new Vector2[starBulletCount];

        //根据公式得出特定角度下的xy坐标
        for (int i = 0; i < starBulletCount; i++)
        {
            float angle = (i / (float)starBulletCount) * 2 * Mathf.PI;

            Vector2 xycood = MathHelper.N_StarEquation(5, angle, star_rmin, star_rmax);

            spawnPositions[i] = xycood;
        }

        Vector3 parentSpawnPos = ctx.firePoint.position + new Vector3(0, starOffsetY, 0);

        ShapeFormationController formationCtrl = ctx.host.CreateFormationParent(spawnPositions, "BossTriangleFormation", parentSpawnPos);

        RotatingFormation rot = formationCtrl.gameObject.AddComponent<RotatingFormation>();
        float directionX = (ctx.player != null && ctx.player.position.x - ctx.host.transform.position.x >= 0) ? 1f : -1f;
        rot.direction = new Vector2(directionX, 0);
        rot.speed = starBulletSpeed;
        rot.rotationSpeed = Random.Range(0, 2) == 0 ? Random.Range(45f, 120f) : Random.Range(-120f, -45f);

        // 【修改】：使用新的位运算检查方阵是否需要加速
        rot.enableAcceleration = enableAcceleration;
        rot.accelerationRate = accelerationRate;

        formationCtrl.StartFormation(ctx.activeTrack.formationDuration, rot);
    }
}
