using UnityEngine;
using static UnityEngine.RuleTile.TilingRuleOutput;

[CreateAssetMenu(fileName = "NewSquarePattern", menuName = "ScriptableObjects/BulletPattern/Square")]
public class SquarePattern : BulletPatternBase
{
    [Header("--- Square ---")]
    public float squareBulletSpeed = 5f;
    public int squareBulletsPerSide = 5;
    public float squareSize = 3f;
    public float squareOffsetY = 0f; // 方阵作为整体，依然保留出生点偏移
    public bool enableAcceleration = true;
    public float accelerationRate = 1f;
    public float formationDuration = 0.5f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector3 spawnPos = ctx.firePoint.position + new Vector3(0, squareOffsetY, 0);

        GameObject squareParent = new GameObject("BossSquareFormation");
        squareParent.transform.position = spawnPos;

        FormationCore core = squareParent.AddComponent<FormationCore>();

        RotatingFormation rot = squareParent.AddComponent<RotatingFormation>();
        float directionX = (ctx.player != null && ctx.player.position.x - ctx.host.transform.position.x >= 0) ? 1f : -1f;
        rot.direction = new Vector2(directionX, 0);
        rot.speed = squareBulletSpeed;
        rot.rotationSpeed = Random.Range(0, 2) == 0 ? Random.Range(45f, 120f) : Random.Range(-120f, -45f);

        // 【修改】：使用新的位运算检查方阵是否需要加速
        rot.enableAcceleration = enableAcceleration;
        rot.accelerationRate = accelerationRate;

        ShapeFormationController formationCtrl = squareParent.AddComponent<ShapeFormationController>();

        float halfSize = squareSize / 2f;
        Vector2[] corners = {
            new Vector2(halfSize, halfSize),
            new Vector2(-halfSize, halfSize),
            new Vector2(-halfSize, -halfSize),
            new Vector2(halfSize, -halfSize)
        };

        for (int edge = 0; edge < 4; edge++)
        {
            Vector2 startPoint = corners[edge];
            Vector2 endPoint = corners[(edge + 1) % 4];

            for (int i = 0; i < squareBulletsPerSide; i++)
            {
                float t = (float)i / squareBulletsPerSide;
                Vector2 localPos = Vector2.Lerp(startPoint, endPoint, t);

                // 1. 从对象池获取子弹
                GameObject bullet = ObjectPoolManager.Instance.Get(ctx.projectileKey);
                if (bullet == null) continue;

                // 2. 认贼作父，摆好阵型
                bullet.transform.SetParent(squareParent.transform);
                bullet.transform.localPosition = localPos;
                core.Register(bullet.transform);

                // 3. 调整朝向
                float rotAngle = Mathf.Atan2(localPos.y, localPos.x) * Mathf.Rad2Deg;
                bullet.transform.rotation = Quaternion.Euler(0, 0, rotAngle);

                // 4. 开启阵型接管模式
                Enemy_Projectile p = bullet.GetComponent<Enemy_Projectile>();
                if (p != null)
                {
                    p.isControlledByFormation = true;
                }

                formationCtrl.AddBullet(bullet.transform, localPos);
            }
        }
        // 5. 注册到阵型控制器
        formationCtrl.StartFormation(ctx.activeTrack.formationDuration, rot);
    }
}
