using UnityEngine;
using System.Collections;
using System.Collections.Generic;


// 位掩码枚举实现 Inspector 面板支持多选下拉框
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


public class BAE_BulletEmitter : MonoBehaviour, IBossActionExecutor
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

    // ==========================================
    // 【多轨化】运行时状态
    // 原先这里是 currentAttackType / angle / timer / isShooting / currentFormationDuration
    // 五个单份字段，只能跑一种弹幕。现在改为一组轨道，各自独立推进。
    // ==========================================
    private readonly List<EmitterTrack> tracks = new List<EmitterTrack>(8);

    /// <summary>
    /// 当前正在执行的轨道。仅在 ExecutePattern 调用期间有效。
    /// 各 Spawn 方法通过它读取 formationDuration / angle / 是否加速，
    /// 避免给十几个方法逐个加参数。ExecutePattern 是同步的，不存在轨道交错。
    /// </summary>
    private EmitterTrack activeTrack;

    //实现IBOOSACTIONEXECUTOR
    public System.Type NodeType => typeof(BulletNode);

    public IEnumerator Execute(ActionNode node, BossContext ctx)
    {
        BulletNode bulletNode = node as BulletNode;
        if (bulletNode == null) yield break;

        if (!StartCombo(bulletNode))
        {
            yield return new WaitForSeconds(1f); // 配置有误，停顿一下防止空转刷屏
            yield break;
        }

        yield return new WaitForSeconds(bulletNode.TotalDuration);
        StopAttack();
    }

    public void Cancel() => StopAttack();
    //---------------------------------------------------
    public void Init(Transform playerT)
    {
        playerTransform = playerT;
    }

    void Update()
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            EmitterTrack track = tracks[i];
            if (track.IsFinished) continue;

            if (track.Tick(Time.deltaTime))
            {
                activeTrack = track;
                ExecutePattern(track.type);
                activeTrack = null;
            }
        }
    }

    // ==========================================
    // 组合启动
    // ==========================================

    /// <summary>
    /// 按卡片配置铺开所有轨道。返回是否成功启动。
    /// </summary>
    public bool StartCombo(BulletNode node)
    {
        ClearTracks();

        if (node.HasPhases)
        {
            int added = 0;
            for (int i = 0; i < node.phases.Count; i++)
            {
                BulletPhase phase = node.phases[i];
                if (phase == null) continue;

                float interval = phase.intervalOverride > 0f
                    ? phase.intervalOverride
                    : GetDefaultInterval(phase.type);

                AddTrack(phase.type, phase.startDelay, phase.duration, interval, phase.formationDuration);
                added++;
            }

            if (added == 0)
            {
                Debug.LogError($"[Emitter] 卡片 {node.name} 的 phases 里全是空槽位！", this);
                return false;
            }
            return true;
        }

        // ---- 旧版单形态兼容路径 ----
        // phases 留空时，把 AttackName + attackDuration 当成一条轨道跑，
        // 现有资产不需要重新配置。
        if (string.IsNullOrEmpty(node.AttackName))
        {
            Debug.LogError($"[Emitter] 卡片 {node.name} 既没有配置 phases，AttackName 也是空的！", this);
            return false;
        }

        if (!System.Enum.TryParse(node.AttackName, out BossAttackType parsedType))
        {
            Debug.LogError($"[Emitter] 无法识别的弹幕字符串: {node.AttackName}（卡片 {node.name}）", this);
            return false;
        }

        AddTrack(parsedType, 0f, node.attackDuration, GetDefaultInterval(parsedType), node.formationDuration);
        return true;
    }

    /// <summary>取一条空闲轨道复用，没有就新建。避免每次组合都产生 GC。</summary>
    private void AddTrack(BossAttackType type, float startDelay, float duration,
                          float interval, float formationDuration)
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].IsFinished)
            {
                tracks[i].Setup(type, startDelay, duration, interval, formationDuration);
                return;
            }
        }

        EmitterTrack track = new EmitterTrack();
        track.Setup(type, startDelay, duration, interval, formationDuration);
        tracks.Add(track);
    }

    private void ClearTracks()
    {
        for (int i = 0; i < tracks.Count; i++) tracks[i].Finish();
    }

    public void StopAttack()
    {
        ClearTracks();
        activeTrack = null;
    }

    /// <summary>某形态在 Inspector 上配置的默认发射间隔。</summary>
    private float GetDefaultInterval(BossAttackType type)
    {
        switch (type)
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

    /// <summary>当前轨道的阵型托管时长。</summary>
    private float CurrentFormationDuration => activeTrack != null ? activeTrack.formationDuration : 0f;

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
        if (activeTrack == null) return false;
        int flagValue = 1 << (int)activeTrack.type;
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

            // 2. 将落点偏移加在"准星"上！
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
        // 【多轨化关键】旋转累加器改从轨道上取。
        // 原先是 Emitter 的共享字段，两条 Rotation 轨道并发时会互相抢，角度会乱跳。
        float currentAngle = activeTrack != null ? activeTrack.angle : 0f;

        Quaternion rotation = Quaternion.Euler(0, 0, currentAngle);
        Vector2 dir = rotation * new Vector2(1, 0);
        SpawnProjectile(dir, firePoint.position, projectileKey, rotationBulletSpeed);

        if (activeTrack != null) activeTrack.angle += rotationAngleIncrement;
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

        FormationCore core = squareParent.AddComponent<FormationCore>();

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
        formationCtrl.StartFormation(CurrentFormationDuration, rot);
    }

    private void SpawnTriangleBurst()
    {
        Vector2[] spawnPositions = new Vector2[triangleBulletCount];

        //根据公式得出特定角度下的xy坐标
        for (int i = 0; i < triangleBulletCount; i++)
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

        formationCtrl.StartFormation(CurrentFormationDuration, rot);
    }

    private void SpawnStarBurst()
    {
        Vector2[] spawnPositions = new Vector2[starBulletCount];

        //根据公式得出特定角度下的xy坐标
        for (int i = 0; i < starBulletCount; i++)
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

        formationCtrl.StartFormation(CurrentFormationDuration, rot);
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

        FormationCore core = Parent.AddComponent<FormationCore>();
        ShapeFormationController formationCtrl = Parent.AddComponent<ShapeFormationController>();

        for (int i = 0; i < bulletPositions.Length; i++)
        {
            Vector2 localPos = bulletPositions[i];

            // 1. 从对象池获取子弹
            GameObject bullet = ObjectPoolManager.Instance.Get(projectileKey);
            if (bullet == null) continue;

            // 2. 认贼作父，摆好阵型
            bullet.transform.SetParent(Parent.transform);
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

        return formationCtrl;
    }

    private GameObject SpawnProjectile(Vector2 dir, Vector3 spawnPos, string objectPoolKey, float speedOverride)
    {
        //对象池调用
        GameObject bullet = ObjectPoolManager.Instance?.Get(objectPoolKey);
        if (bullet == null) return null;
        bullet.transform.position = spawnPos;
        Enemy_Projectile projScript = bullet.GetComponent<Enemy_Projectile>();
        if (projScript != null)
        {
            projScript.Setup(dir);
            projScript.speed = speedOverride;

            // 【修改】：使用新的位运算检查是否需要挂载加速器
            if (IsCurrentAttackAccelerated())
            {
                BulletAcceleration acc = bullet.GetComponent<BulletAcceleration>();
                if (acc != null)
                {
                    acc.Configure(accelerationRate, coef);
                }
                else
                {
                    Debug.LogWarning($"[Emitter] {bullet.name} 缺少 BulletAcceleration 组件，加速未生效。", bullet);
                }
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

    }
}
