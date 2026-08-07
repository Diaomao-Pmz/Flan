using UnityEngine;

public enum BossAttackType
{
    Line,
    Random,
    Circle,
    Square,
    Rotation,
    Triangle,
    Star
}

// 【新增】：为了让 Inspector 面板支持多选下拉框，必须定义一个位掩码枚举
[System.Flags]
public enum BossAttackFlags
{
    None = 0,
    Line = 1 << 0,      // 对应 BossAttackType.Line
    Random = 1 << 1,    // 对应 BossAttackType.Random
    Circle = 1 << 2,    // 对应 BossAttackType.Circle
    Square = 1 << 3,    // 对应 BossAttackType.Square
    Rotation = 1 << 4,  // 对应 BossAttackType.Rotation
    Triangle = 1 << 5,
    Star = 1 << 6,
    All = ~0            // 全选
}

public class BossBulletEmitter : MonoBehaviour
{
    private Transform playerTransform;

    [Header("--- 基础发射设置 ---")]
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] string projectileKey = "EnemyBullet";
    [SerializeField] Transform firePoint;

    [Header("--- 弹速加速设置 ---")]
    [Tooltip("在下拉列表中勾选需要应用加速逻辑的招式")]
    public BossAttackFlags acceleratedAttacks = BossAttackFlags.None;
    [Tooltip("弹速加速系数")]
    public float accelerationRate = 5f;
    public float coef = 0.3f;

    // ==========================================
    // 各形态可调节参数 (Line形态已改为落点偏移)
    // ==========================================
    [Header("--- Line ---")]
    public float lineShootInterval = 1f;
    public float lineBulletSpeed = 15f;
    public float lineBulletScale = 1.5f;
    [Tooltip("落点Y轴偏移：瞄准玩家弱点上方(+)或下方(-)")]
    public float lineOffsetY = 0f;

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
    public float squareOffsetY = 0f; // 方阵作为整体，依然保留出生点偏移

    [Header("--- Triangle ---")]
    public int triangleBulletCount = 10;
    public float triangleShootInterval = 1f;
    public float triangleBulletSpeed = 5f;
    public float tri_rmin = 2;
    public float tri_rmax = 4;
    public float triangleOffsetY = 0f;

    [Header("--- Star ---")]
    public int starBulletCount = 10;
    public float starShootInterval = 1f;
    public float starBulletSpeed = 5f;
    public float star_rmin = 2;
    public float star_rmax = 4;
    public float starOffsetY = 0f;

    [Header("--- Circle ---")]
    public float circleShootInterval = 1.5f;
    public float circleBulletSpeed = 8f;
    public int circleBulletCount = 18;
    public float circleOffsetY = 0f; // 圆环作为整体，依然保留出生点偏移

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

    public void StartAttack(BossAttackType attackType, float formationTime = 0f)
    {
        isShooting = true;
        currentAttackType = attackType;
        currentFormationDuration = formationTime;
        timer = GetCurrentInterval();
    }

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
            case BossAttackType.Triangle: return triangleShootInterval;
            case BossAttackType.Star: return starShootInterval;
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
            case BossAttackType.Triangle: SpawnTriangleBurst(); break;
            case BossAttackType.Star: SpawnStarBurst(); break;
        }
    }

    // 【新增】：智能获取玩家身上的 Hurtbox 坐标
    private Vector3 GetPlayerTargetPosition()
    {
        if (playerTransform == null) return firePoint.position;

        // 尝试寻找之前你在 PlayerStateMachine 里定义的 "Hurtbox_Core"
        Transform hurtbox = playerTransform.Find("Hurtbox_Core");
        if (hurtbox != null)
        {
            return hurtbox.position;
        }

        // 如果名字没对上，作为防呆设计，获取碰撞体中心点而不是脚底
        Collider2D col = playerTransform.GetComponent<Collider2D>();
        if (col != null)
        {
            return col.bounds.center;
        }

        // 最差情况，返回脚底
        return playerTransform.position;
    }

    // 【新增】：检查当前招式是否在加速字典（多选框）中被勾选
    private bool IsCurrentAttackAccelerated()
    {
        int flagValue = 1 << (int)currentAttackType;
        return ((int)acceleratedAttacks & flagValue) != 0;
    }

    // ==========================================
    // 具体弹幕实现

    private void SpawnLineProjectile()
    {
        Vector2 dir = Vector2.left;

        if (playerTransform != null)
        {
            // 1. 获取最精确的玩家弱点坐标
            Vector3 targetPos = GetPlayerTargetPosition();

            // 2. 将落点偏移加在“准星”上！
            targetPos.y += lineOffsetY;

            // 3. 枪口依然在老地方，但瞄准的是偏移后的弱点
            dir = (targetPos - firePoint.position).normalized;
        }

        // 枪口位置不进行任何偏移，在原点生成
        Vector3 spawnPos = firePoint.position;

        GameObject bullet = SpawnProjectile(dir, spawnPos, projectileKey, lineBulletSpeed);
        if (bullet != null)
        {
            bullet.transform.localScale = new Vector3(lineBulletScale, lineBulletScale, 1f);
        }
    }

    private void RandomlySpawnProjectile()
    {
        Vector2 dir = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
        SpawnProjectile(dir, firePoint.position, projectileKey, randomBulletSpeed);
    }

    private void RotatingSpawnProjectile()
    {
        Quaternion rotation = Quaternion.Euler(0, 0, angle);
        Vector2 dir = rotation * new Vector2(1, 0);
        SpawnProjectile(dir, firePoint.position, projectileKey, rotationBulletSpeed);
        angle += rotationAngleIncrement;
    }

    private void SpawnCircleBurst()
    {
        Vector3 spawnPos = firePoint.position + new Vector3(0, circleOffsetY, 0);
        float angleStep = 360f / circleBulletCount;

        for (int i = 0; i < circleBulletCount; i++)
        {
            float currentAngle = i * angleStep;
            Vector2 dir = new Vector2(Mathf.Cos(currentAngle * Mathf.Deg2Rad), Mathf.Sin(currentAngle * Mathf.Deg2Rad));
            SpawnProjectile(dir, spawnPos, projectileKey, circleBulletSpeed);
        }
    }

    private void SpawnSquareBurst()
    {
        Vector3 spawnPos = firePoint.position + new Vector3(0, squareOffsetY, 0);

        GameObject squareParent = new GameObject("BossSquareFormation");
        squareParent.transform.position = spawnPos;

        RotatingFormation rot = squareParent.AddComponent<RotatingFormation>();
        float directionX = (playerTransform != null && playerTransform.position.x - transform.position.x >= 0) ? 1f : -1f;
        rot.direction = new Vector2(directionX, 0);
        rot.speed = squareBulletSpeed;
        rot.rotationSpeed = Random.Range(0, 2) == 0 ? Random.Range(45f, 120f) : Random.Range(-120f, -45f);

        // 【修改】：使用新的位运算检查方阵是否需要加速
        rot.enableAcceleration = IsCurrentAttackAccelerated();
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
                GameObject bullet = ObjectPoolManager.Instance.Get(projectileKey);

                // 2. 认贼作父，摆好阵型
                bullet.transform.SetParent(squareParent.transform);
                bullet.transform.localPosition = localPos;

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
        formationCtrl.StartFormation(currentFormationDuration, rot);
    }

    private void SpawnTriangleBurst()
    {
        Vector2[] spawnPositions = new Vector2[triangleBulletCount];

        //根据公式得出特定角度下的xy坐标
        for(int i = 0; i  < triangleBulletCount; i++)
        {
            float angle = (i / (float)triangleBulletCount) * 2 * Mathf.PI;

            Vector2 xycood = MathHelper.N_PolygonAngleEquation(3, angle, tri_rmin, tri_rmax);

            spawnPositions[i] = xycood;
        }

        Vector3 parentSpawnPos = firePoint.position + new Vector3(0, triangleOffsetY, 0);

        ShapeFormationController formationCtrl = CreateFormationParent(spawnPositions, "BossTriangleFormation", parentSpawnPos);

        RotatingFormation rot = formationCtrl.gameObject.AddComponent<RotatingFormation>();
        float directionX = (playerTransform != null && playerTransform.position.x - transform.position.x >= 0) ? 1f : -1f;
        rot.direction = new Vector2(directionX, 0);
        rot.speed = triangleBulletSpeed;
        rot.rotationSpeed = Random.Range(0, 2) == 0 ? Random.Range(45f, 120f) : Random.Range(-120f, -45f);

        // 【修改】：使用新的位运算检查方阵是否需要加速
        rot.enableAcceleration = IsCurrentAttackAccelerated();
        rot.accelerationRate = accelerationRate;

        formationCtrl.StartFormation(currentFormationDuration, rot);
    }

    private void SpawnStarBurst()
    {
        Vector2[] spawnPositions = new Vector2[starBulletCount];

        //根据公式得出特定角度下的xy坐标
        for(int i = 0; i  < starBulletCount; i++)
        {
            float angle = (i / (float)starBulletCount) * 2 * Mathf.PI;

            Vector2 xycood = MathHelper.N_PolygonAngleEquation(5, angle, star_rmin, star_rmax);

            spawnPositions[i] = xycood;
        }

        Vector3 parentSpawnPos = firePoint.position + new Vector3(0, starOffsetY, 0);

        ShapeFormationController formationCtrl = CreateFormationParent(spawnPositions, "BossStarFormation", parentSpawnPos);

        RotatingFormation rot = formationCtrl.gameObject.AddComponent<RotatingFormation>();
        float directionX = (playerTransform != null && playerTransform.position.x - transform.position.x >= 0) ? 1f : -1f;
        rot.direction = new Vector2(directionX, 0);
        rot.speed = starBulletSpeed;
        rot.rotationSpeed = Random.Range(0, 2) == 0 ? Random.Range(45f, 120f) : Random.Range(-120f, -45f);

        // 【修改】：使用新的位运算检查方阵是否需要加速
        rot.enableAcceleration = IsCurrentAttackAccelerated();
        rot.accelerationRate = accelerationRate;

        formationCtrl.StartFormation(currentFormationDuration, rot);
    }

    /// <summary>
    /// 创建一个搭载shape formation controller的父对象，返回其shape formation controller组件
    /// </summary>
    /// <param name="bulletPositions">子弹位置</param>
    /// <param name="parentName">父对象名字</param>
    /// <returns></returns>
    private ShapeFormationController CreateFormationParent(Vector2[] bulletPositions, string parentName, Vector3 spawnPos)
    {
        //创建父物体
        GameObject Parent = new GameObject(parentName);
        Parent.transform.position = spawnPos;

        ShapeFormationController formationCtrl = Parent.AddComponent<ShapeFormationController>();

        for(int i = 0; i < bulletPositions.Length; i++)
        {
            Vector2 localPos = bulletPositions[i];

            // 1. 从对象池获取子弹
            GameObject bullet = ObjectPoolManager.Instance.Get(projectileKey);

            // 2. 认贼作父，摆好阵型
            bullet.transform.SetParent(Parent.transform);

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

        return formationCtrl;
    }

    private GameObject SpawnProjectile(Vector2 dir, Vector3 spawnPos, string objectPoolKey, float speedOverride)
    {
        //对象池调用
        GameObject bullet = ObjectPoolManager.Instance?.Get(objectPoolKey);
        Debug.Log($"objpool: {ObjectPoolManager.Instance == null}");
        bullet.transform.position = spawnPos;
        Enemy_Projectile projScript = bullet.GetComponent<Enemy_Projectile>();
        if (projScript != null)
        {
            projScript.Setup(dir);
            projScript.speed = speedOverride;

            // 【修改】：使用新的位运算检查是否需要挂载加速器
            if (IsCurrentAttackAccelerated())
            {
                BulletAcceleration a = bullet.GetComponent<BulletAcceleration>();
                if (a != null)
                {
                    Destroy(a);
                }

                BulletAcceleration acc = bullet.AddComponent<BulletAcceleration>();
                acc.accelerationRate = accelerationRate;
                acc.coef = this.coef;
            }
        }
        return bullet;
    }
}

// ==========================================
// 辅助类保持不变
public class RotatingFormation : FormationBase
{
    public Vector2 direction;
    public float speed;
    public float rotationSpeed;
    public bool enableAcceleration = false;
    public float accelerationRate = 0f;
    private float timer = 0f;
    private float baseSpeed;

    void Start()
    {
        baseSpeed = speed;
    }

    protected override void Update()
    {
        base.Update();

        if (enableAcceleration)
        {
            timer += Time.deltaTime;
            speed = baseSpeed + (accelerationRate * timer * timer);
        }
        transform.Translate(direction * speed * Time.deltaTime, Space.World);
        transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
    }
}

//rotating formation等formation 类的基类
public class FormationBase : MonoBehaviour
{
    protected virtual void Update()
    {
        if(transform.childCount == 0)
        {
            Destroy(gameObject);
        }
    }
}

public class FormationBullet : MonoBehaviour
{
    public LayerMask destroyLayer;
    public bool isEnemyProjectile = true;
    public int damage = 10;

    void OnTriggerEnter2D(Collider2D hitInfo)
    {
        PlayerState playerState = hitInfo.GetComponentInParent<PlayerState>();
        if (!isEnemyProjectile && playerState != null) return;
        if (isEnemyProjectile && hitInfo.CompareTag("Enemy")) return;

        if (isEnemyProjectile && playerState != null)
        {
            float relativeX = playerState.transform.position.x - transform.position.x;
            Vector2 knockbackDir = new Vector2(Mathf.Sign(relativeX) * 8f, 5f);
            playerState.health.TakeDamage(damage, knockbackDir, playerState);
        }
        Destroy(gameObject);
    }
}

public class BulletAcceleration : MonoBehaviour
{
    public float accelerationRate;
    public float coef;
    private Enemy_Projectile proj;
    private float timer = 0f;
    private float baseSpeed;

    void Start()
    {
        proj = GetComponent<Enemy_Projectile>();
        if (proj != null)
        {
            baseSpeed = proj.speed;
        }
    }

    void Update()
    {
        if (proj != null)
        {
            timer += Time.deltaTime;
            proj.speed = coef * baseSpeed + (accelerationRate * timer * timer);
        }
    }
}