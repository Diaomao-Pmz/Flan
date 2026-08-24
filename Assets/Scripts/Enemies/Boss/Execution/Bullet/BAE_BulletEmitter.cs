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
                ExecutePattern(track.pattern);
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
                    : phase.pattern.DefaultInterval;

                AddTrack(phase.pattern, phase.startDelay, phase.duration, interval, phase.formationDuration);
                added++;
            }

            if (added == 0)
            {
                Debug.LogError($"[Emitter] 卡片 {node.name} 的 phases 里全是空槽位！", this);
                return false;
            }
            return true;
        }
        return true;
    }

    /// <summary>取一条空闲轨道复用，没有就新建。避免每次组合都产生 GC。</summary>
    private void AddTrack(BulletPatternBase pattern, float startDelay, float duration,
                          float interval, float formationDuration)
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].IsFinished)
            {
                tracks[i].Setup(pattern, startDelay, duration, interval, formationDuration);
                return;
            }
        }

        EmitterTrack track = new EmitterTrack();
        track.Setup(pattern, startDelay, duration, interval, formationDuration);
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

    private void ExecutePattern(BulletPatternBase pattern)
    {
        BulletSpawnContext ctx = new BulletSpawnContext();
        ctx.SetUp(firePoint, playerTransform, GetPlayerTargetPosition(), projectileKey, activeTrack, this);
        pattern.Spawn(ctx);
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

    /// <summary>
    /// 创建一个搭载shape formation controller的父对象，返回其shape formation controller组件
    /// </summary>
    /// <param name="bulletPositions">子弹位置</param>
    /// <param name="parentName">父对象名字</param>
    /// <returns></returns>
    public ShapeFormationController CreateFormationParent(Vector2[] bulletPositions, string parentName, Vector3 spawnPos)
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

    public GameObject SpawnProjectile(Vector2 dir, Vector3 spawnPos, string objectPoolKey, float speedOverride)
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
