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
    /// 图案通过它读取 angle / interval（旋转弹幕用），
    /// 避免给十几个方法逐个加参数。ExecutePattern 是同步的，不存在轨道交错。
    /// </summary>
    private EmitterTrack activeTrack;

    // 当前弹幕招式的击退配置。StartCombo 每次只跑一张卡（会先清空旧轨道），所以一份就够
    private bool currentOverrideKnockback;
    private Vector2 currentKnockbackForce;

    /// <summary>
    /// 给刚出膛的子弹写上当前招式的击退配置。
    /// 所有生成子弹的地方都要调它（SpawnBullet / CreateFormationParent 内部已调用）。
    /// </summary>
    public void StampKnockback(GameObject bullet)
    {
        if (bullet == null) return;
        Enemy_Projectile p = bullet.GetComponent<Enemy_Projectile>();
        if (p != null) p.SetKnockback(currentOverrideKnockback, currentKnockbackForce);
    }

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

        currentOverrideKnockback = node.overrideKnockback;
        currentKnockbackForce = node.knockbackForce;

        int added = 0;
        if (node.phases != null)
        {
            for (int i = 0; i < node.phases.Count; i++)
            {
                BulletPhase phase = node.phases[i];
                if (phase == null) continue;

                if (phase.pattern == null)
                {
                    Debug.LogWarning($"[Emitter] 卡片 {node.name} 第 {i} 段没有选弹幕类型，已跳过。", this);
                    continue;
                }

                AddTrack(phase.pattern, phase.startDelay, phase.duration);
                added++;
            }
        }

        if (added == 0)
        {
            Debug.LogError($"[Emitter] 卡片 {node.name} 没有任何可用的弹幕段（Phases 为空或都没选类型）！", this);
            return false;
        }
        return true;
    }

    /// <summary>取一条空闲轨道复用，没有就新建。避免每次组合都产生 GC。</summary>
    private void AddTrack(BulletPatternBase pattern, float startDelay, float duration)
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].IsFinished)
            {
                tracks[i].Setup(pattern, startDelay, duration);
                return;
            }
        }

        EmitterTrack track = new EmitterTrack();
        track.Setup(pattern, startDelay, duration);
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
        ctx.SetUp(firePoint, playerTransform, GetPlayerTargetPosition(), ResolveKey(pattern), activeTrack, this);
        pattern.Spawn(ctx);
    }

    /// <summary>这个图案用哪种子弹：图案上填了 key 就用它，否则用发射器的默认子弹</summary>
    private string ResolveKey(BulletPatternBase pattern)
        => pattern != null && !string.IsNullOrEmpty(pattern.projectileKey) ? pattern.projectileKey : projectileKey;

    /// <summary>
    /// 远程瞄准点：与激光统一，走 BossContext.PlayerAimPoint（递归查找 Hurtbox_Core，
    /// 找不到时退回玩家碰撞体中心）。以后改瞄准规则只改 BossContext 一处。
    /// </summary>
    private Vector3 GetPlayerTargetPosition()
    {
        if (playerTransform == null) return firePoint.position;

        if (bossController == null) bossController = GetComponent<BossController>();
        if (bossController != null && bossController.Context.player != null)
            return bossController.Context.PlayerAimPoint;

        // 兜底：Context 还没组装好（理论上不会发生）
        Collider2D col = playerTransform.GetComponentInChildren<Collider2D>();
        return col != null ? col.bounds.center : playerTransform.position;
    }

    private BossController bossController;

    /// <summary>
    /// 创建一个阵型父物体：从对象池取子弹挂在它下面，交给 ShapeFormationController 展开。
    /// 子弹的外观、大小、存活时间、击退按图案的通用设置套用；移动由阵型负责。
    /// </summary>
    public ShapeFormationController CreateFormationParent(BulletPatternBase pattern, Vector2[] bulletPositions,
                                                          string parentName, Vector3 spawnPos)
    {
        GameObject parent = new GameObject(parentName);
        parent.transform.position = spawnPos;

        FormationCore core = parent.AddComponent<FormationCore>();
        ShapeFormationController formationCtrl = parent.AddComponent<ShapeFormationController>();
        string key = ResolveKey(pattern);

        for (int i = 0; i < bulletPositions.Length; i++)
        {
            Vector2 localPos = bulletPositions[i];

            GameObject bullet = ObjectPoolManager.Instance != null ? ObjectPoolManager.Instance.Get(key) : null;
            if (bullet == null) continue;

            bullet.transform.SetParent(parent.transform);
            core.Register(bullet.transform);

            float rotAngle = Mathf.Atan2(localPos.y, localPos.x) * Mathf.Rad2Deg;
            bullet.transform.rotation = Quaternion.Euler(0, 0, rotAngle);

            Enemy_Projectile p = bullet.GetComponent<Enemy_Projectile>();
            if (p != null)
            {
                p.isControlledByFormation = true;
                ApplyCommon(p, pattern);
            }
            StampKnockback(bullet);

            formationCtrl.AddBullet(bullet.transform, localPos);
        }

        return formationCtrl;
    }

    /// <summary>
    /// 生成一颗独立飞行的子弹，并套用图案的通用设置（外观、速度、大小、存活时间、加速、击退）。
    /// </summary>
    /// <param name="speedOverride">不填 = 用图案的子弹速度；扇形多层、混沌随机速度时传入</param>
    public GameObject SpawnBullet(BulletPatternBase pattern, Vector2 dir, Vector3 spawnPos, float? speedOverride = null)
    {
        GameObject bullet = ObjectPoolManager.Instance != null ? ObjectPoolManager.Instance.Get(ResolveKey(pattern)) : null;
        if (bullet == null) return null;

        bullet.transform.position = spawnPos;

        Enemy_Projectile proj = bullet.GetComponent<Enemy_Projectile>();
        if (proj != null)
        {
            proj.Setup(dir);
            proj.speed = speedOverride ?? pattern.bulletSpeed;
            ApplyCommon(proj, pattern);

            // 加速要在速度写好之后配置（加速器会拍下当前速度作为基准）
            if (pattern.enableAcceleration)
            {
                BulletAcceleration acc = bullet.GetComponent<BulletAcceleration>();
                if (acc != null) acc.Configure(pattern.accelerationRate, pattern.accelerationCoef);
                else Debug.LogWarning($"[Emitter] {bullet.name} 缺少 BulletAcceleration 组件，加速未生效。", bullet);
            }
        }

        StampKnockback(bullet);
        return bullet;
    }

    /// <summary>大小、存活时间（单颗子弹和阵型子弹共用）</summary>
    private static void ApplyCommon(Enemy_Projectile proj, BulletPatternBase pattern)
    {
        if (pattern.bulletScale > 0f) proj.SetScale(pattern.bulletScale);
        proj.SetLifetime(pattern.lifetime);
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
