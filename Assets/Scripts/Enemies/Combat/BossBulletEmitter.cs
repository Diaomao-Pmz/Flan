using UnityEngine;

public enum BossAttackType
{
    Line,
    Random,
    Circle,
    Square,
    Rotation
}

public class BossBulletEmitter : MonoBehaviour
{
    private Transform playerTransform;

    [Header("--- 基础发射设置 ---")]
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] Transform firePoint;

    [Header("--- 弹速加速 ---")]
    public bool enableAcceleration = false;
    [Tooltip("弹速加速系数")]
    public float accelerationRate = 5f;
    public float coef = 0.3f;

    // ==========================================
    // 各形态可调节参数 (已加入 Y轴 Offset)
    // ==========================================
    [Header("--- Line ---")]
    public float lineShootInterval = 1f;
    public float lineBulletSpeed = 15f;
    public float lineBulletScale = 1.5f;
    public float lineOffsetY = 0f; // 新增 Y 轴偏移

    [Header("--- Random ---")]
    public float randomShootInterval = 0.2f;
    public float randomBulletSpeed = 10f;

    [Header("--- Rotation ---")]
    public float rotationShootInterval = 0.05f;
    public float rotationBulletSpeed = 12f;
    public float rotationAngleIncrement = 15f;

    [Header("--- Square ---")]
    public float squareShootInterval = 2f;
    public float squareBulletSpeed = 5f;
    public int squareBulletsPerSide = 5;
    public float squareSize = 3f;
    public float squareOffsetY = 0f; // 新增 Y 轴偏移

    [Header("--- Circle ---")]
    public float circleShootInterval = 1.5f;
    public float circleBulletSpeed = 8f;
    public int circleBulletCount = 18;
    public float circleOffsetY = 0f; // 新增 Y 轴偏移

    private BossAttackType currentAttackType = BossAttackType.Random;
    private float angle = 0;
    private float timer = 0;
    private bool isShooting = false;

    private float currentFormationDuration = 0f;

    public void Init(Transform playerT)
    {
        playerTransform = playerT;
    }

    void Update()
    {
        if (isShooting)
        {
            timer += Time.deltaTime;
            if (timer >= GetCurrentInterval())
            {
                timer = 0;
                ExecutePattern(currentAttackType);
            }
        }
    }

    //将来的接口
    public void StartAttack(BossAttackType attackType, float formationTime = 0f)
    {
        isShooting = true;
        currentAttackType = attackType;
        currentFormationDuration = formationTime; // 记下这个时间
        timer = GetCurrentInterval();
    }

    // 兼容接口
    public void StartAttack(string attackTypeStr, float formationTime = 0f)
    {
        if (System.Enum.TryParse(attackTypeStr, out BossAttackType parsedType))
        {
            StartAttack(parsedType, formationTime);
        }
        else
        {
            Debug.LogError($"[Emitter] 无法识别的弹幕字符串: {attackTypeStr}");
        }
    }

    public void StopAttack()
    {
        isShooting = false;
    }



    private float GetCurrentInterval()
    {
        switch (currentAttackType)
        {
            case BossAttackType.Line: return lineShootInterval;
            case BossAttackType.Random: return randomShootInterval;
            case BossAttackType.Circle: return circleShootInterval;
            case BossAttackType.Rotation: return rotationShootInterval;
            case BossAttackType.Square: return squareShootInterval;
            default: return 1f;
        }
    }

    private void ExecutePattern(BossAttackType type)
    {
        switch (type)
        {
            case BossAttackType.Line: SpawnLineProjectile(); break;
            case BossAttackType.Random: RandomlySpawnProjectile(); break;
            case BossAttackType.Circle: SpawnCircleBurst(); break;
            case BossAttackType.Rotation: RotatingSpawnProjectile(); break;
            case BossAttackType.Square: SpawnSquareBurst(); break;
        }
    }

    // ==========================================
    // 具体弹幕实现

    private void SpawnLineProjectile()
    {
        // 应用 Y 轴偏移
        Vector3 spawnPos = firePoint.position + new Vector3(0, lineOffsetY, 0);
        Vector2 dir = Vector2.left;

        if (playerTransform != null)
        {
            dir = (playerTransform.position - spawnPos).normalized; // 修正自瞄朝向，基于带有 offset 的生成点
        }

        GameObject bullet = SpawnProjectile(dir, spawnPos, projectilePrefab, lineBulletSpeed);
        if (bullet != null)
        {
            bullet.transform.localScale = new Vector3(lineBulletScale, lineBulletScale, 1f);
        }
    }

    private void RandomlySpawnProjectile()
    {
        Vector2 dir = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
        SpawnProjectile(dir, firePoint.position, projectilePrefab, randomBulletSpeed);
    }

    private void RotatingSpawnProjectile()
    {
        Quaternion rotation = Quaternion.Euler(0, 0, angle);
        Vector2 dir = rotation * new Vector2(1, 0);
        SpawnProjectile(dir, firePoint.position, projectilePrefab, rotationBulletSpeed);
        angle += rotationAngleIncrement;
    }

    private void SpawnCircleBurst()
    {
        // 应用 Y 轴偏移
        Vector3 spawnPos = firePoint.position + new Vector3(0, circleOffsetY, 0);
        float angleStep = 360f / circleBulletCount;

        for (int i = 0; i < circleBulletCount; i++)
        {
            float currentAngle = i * angleStep;
            Vector2 dir = new Vector2(Mathf.Cos(currentAngle * Mathf.Deg2Rad), Mathf.Sin(currentAngle * Mathf.Deg2Rad));
            SpawnProjectile(dir, spawnPos, projectilePrefab, circleBulletSpeed);
        }
    }

    private void SpawnSquareBurst()
    {
        // 1. 应用 Y 轴偏移，确立所有子弹诞生的中心点 (母舰位置)
        Vector3 spawnPos = firePoint.position + new Vector3(0, squareOffsetY, 0);

        GameObject squareParent = new GameObject("BossSquareFormation");
        squareParent.transform.position = spawnPos;

        // 2. 挂载原有的旋转和飞行脚本 (此时它会被下方的展开控制器暂时禁用，等展开完再起飞)
        RotatingFormation rot = squareParent.AddComponent<RotatingFormation>();
        float directionX = (playerTransform != null && playerTransform.position.x - transform.position.x >= 0) ? 1f : -1f;
        rot.direction = new Vector2(directionX, 0);
        rot.speed = squareBulletSpeed;
        rot.rotationSpeed = Random.Range(0, 2) == 0 ? Random.Range(45f, 120f) : Random.Range(-120f, -45f);

        // 传递方阵整体加速设定
        rot.enableAcceleration = enableAcceleration;
        rot.accelerationRate = accelerationRate;

        // 3. 【新增】挂载图形展开控制器
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

                // 4. 【核心改变】所有子弹统一在中心点 spawnPos 生成！而不是 spawnPos + localPos
                GameObject bullet = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
                bullet.transform.SetParent(squareParent.transform);

                float rotAngle = Mathf.Atan2(localPos.y, localPos.x) * Mathf.Rad2Deg;
                bullet.transform.rotation = Quaternion.Euler(0, 0, rotAngle);

                // 处理物理和碰撞脚本剥离
                Projectile p = bullet.GetComponent<Projectile>();
                if (p != null)
                {
                    LayerMask mask = p.destroyLayer;
                    bool isEnemy = p.isEnemyProjectile;

                    // 【新增】提取原生脚本的伤害值。
                    // 假设你的 Projectile 脚本里原本就有一个叫 damage 的变量。
                    // 如果 Projectile 里没写 damage 变量，你可以直接写死：int originalDamage = 10;
                    int originalDamage = p.damage;

                    Destroy(p);
                    if (bullet.GetComponent<Rigidbody2D>() != null) Destroy(bullet.GetComponent<Rigidbody2D>());

                    FormationBullet fb = bullet.AddComponent<FormationBullet>();
                    fb.destroyLayer = mask;
                    fb.isEnemyProjectile = isEnemy;
                    fb.damage = originalDamage; // 【新增】将提取出的伤害值，传递给这个新的外壳！
                }

                // 5. 【新增】把生成的子弹，和它最终该去的局部坐标(localPos)托付给控制器
                formationCtrl.AddBullet(bullet.transform, localPos);
            }
        }

        if (currentFormationDuration > 0)
        {
            formationCtrl.StartFormation(currentFormationDuration, rot);
        }
        else
        {
            formationCtrl.StartFormation(0f, rot);
        }
    }

    // 统一下游发射接口，增加 spawnPos 参数以支持偏移
    private GameObject SpawnProjectile(Vector2 dir, Vector3 spawnPos, GameObject prefab, float speedOverride)
    {
        GameObject bullet = Instantiate(prefab, spawnPos, Quaternion.identity);
        Projectile projScript = bullet.GetComponent<Projectile>();
        if (projScript != null)
        {
            projScript.Setup(dir);
            projScript.speed = speedOverride;

            // 如果勾选了加速，动态挂载加速控制脚本
            if (enableAcceleration)
            {
                BulletAcceleration acc = bullet.AddComponent<BulletAcceleration>();
                acc.accelerationRate = accelerationRate;
            }
        }
        return bullet;
    }
}

// ==========================================
// 辅助类：负责让正方形弹幕整体飞行和旋转 (已接入加速逻辑)
public class RotatingFormation : MonoBehaviour
{
    public Vector2 direction;
    public float speed;
    public float rotationSpeed;
    public float lifeTime = 10f;

    public bool enableAcceleration = false;
    public float accelerationRate = 0f;

    private float timer = 0f;
    private float baseSpeed;

    void Start()
    {
        Destroy(gameObject, lifeTime);
        baseSpeed = speed; // 记录初始速度
    }

    void Update()
    {
        // 应用 t^2 加速公式
        if (enableAcceleration)
        {
            timer += Time.deltaTime;
            speed = baseSpeed + (accelerationRate * timer * timer);
        }

        transform.Translate(direction * speed * Time.deltaTime, Space.World);
        transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
    }
}

// 辅助类：顶替 Projectile，只负责正方形子弹的碰撞检测
public class FormationBullet : MonoBehaviour
{
    public LayerMask destroyLayer;
    public bool isEnemyProjectile = true;
    public int damage = 10; // 伤害值变量

    void OnTriggerEnter2D(Collider2D hitInfo)
    {
        // 【完全照搬 Projectile 的逻辑】：通过父级获取 PlayerState
        PlayerState playerState = hitInfo.GetComponentInParent<PlayerState>();

        // 如果是玩家自己的子弹，且打到了玩家自己，无视并退出
        if (!isEnemyProjectile && playerState != null) return;

        // 如果是怪物的子弹，不能打怪物自己 (假设怪物标签是 Enemy)
        if (isEnemyProjectile && hitInfo.CompareTag("Enemy")) return;

        // 检查撞到的物体层级是否在允许销毁的 Layer 里面
        if ((destroyLayer.value & (1 << hitInfo.gameObject.layer)) != 0)
        {
            // 怪物打玩家
            if (isEnemyProjectile && playerState != null)
            {
                // 【完全照搬 Projectile 的逻辑】：调用 health 模块扣血
                playerState.health.TakeDamage(damage);
            }

            // 结算完伤害后，销毁子弹
            Destroy(gameObject);
        }
    }
}

// 辅助类：负责接管独立子弹的二次方加速逻辑
public class BulletAcceleration : MonoBehaviour
{
    public float accelerationRate;
    public float coef;

    private Projectile proj;
    private float timer = 0f;
    private float baseSpeed;
    

    void Start()
    {
        proj = GetComponent<Projectile>();
        if (proj != null)
        {
            baseSpeed = proj.speed; // 记录子弹初始速度
        }
    }

    void Update()
    {
        if (proj != null)
        {
            timer += Time.deltaTime;
            // 速度 = 初始速度 + a * t^2
            proj.speed = coef * baseSpeed + (accelerationRate * timer * timer);
        }
    }
}