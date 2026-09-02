using UnityEngine;
using static UnityEngine.RuleTile.TilingRuleOutput;

[CreateAssetMenu(fileName = "NewTrianglePattern", menuName = "ScriptableObjects/BulletPattern/Triangle")]
public class TrianglePattern : BulletPatternBase
{
    [Header("--- Triangle ---")]
    public int triangleBulletCount = 10;
    public float triangleBulletSpeed = 5f;
    public float tri_rmin = 2;
    public float tri_rmax = 4;
    public float triangleOffsetY = 0f;
    public bool enableAcceleration = true;
    public float accelerationRate = 1f;
    public float formationDuration = 0.5f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector2[] spawnPositions = new Vector2[triangleBulletCount];

        //根据公式得出特定角度下的xy坐标
        for (int i = 0; i < triangleBulletCount; i++)
        {
            float angle = (i / (float)triangleBulletCount) * 2 * Mathf.PI;

            Vector2 xycood = MathHelper.N_PolygonAngleEquation(3, angle, tri_rmin, tri_rmax);

            spawnPositions[i] = xycood;
        }

        Vector3 parentSpawnPos = ctx.firePoint.position + new Vector3(0, triangleOffsetY, 0);

        ShapeFormationController formationCtrl = ctx.host.CreateFormationParent(spawnPositions, "BossTriangleFormation", parentSpawnPos);

        RotatingFormation rot = formationCtrl.gameObject.AddComponent<RotatingFormation>();
        float directionX = (ctx.player != null && ctx.player.position.x - ctx.host.transform.position.x >= 0) ? 1f : -1f;
        rot.direction = new Vector2(directionX, 0);
        rot.speed = triangleBulletSpeed;
        rot.rotationSpeed = Random.Range(0, 2) == 0 ? Random.Range(45f, 120f) : Random.Range(-120f, -45f);

        // 【修改】：使用新的位运算检查方阵是否需要加速
        rot.enableAcceleration = enableAcceleration;
        rot.accelerationRate = accelerationRate;

        formationCtrl.StartFormation(ctx.activeTrack.formationDuration, rot);
    }
}
