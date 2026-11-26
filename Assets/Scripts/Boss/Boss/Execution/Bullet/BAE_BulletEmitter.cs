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


/// <summary>一张弹幕卡的子弹带什么击退（写在每条轨道上，几张卡同时放时各带各的）</summary>
public readonly struct BulletKnockback
{
    public readonly bool overrideKnockback;
    public readonly Vector2 force;

    public BulletKnockback(bool overrideKnockback, Vector2 force)
    {
        this.overrideKnockback = overrideKnockback;
        this.force = force;
    }
}

public class BAE_BulletEmitter : MonoBehaviour, IBossActionExecutor, IBackgroundableExecutor
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
    // 每条轨道记着自己属于哪张卡（comboId），几张卡可以同时放，各自到点结束。
    // ==========================================
    private readonly List<EmitterTrack> tracks = new List<EmitterTrack>(8);

    /// <summary>
    /// 当前正在执行的轨道。仅在 ExecutePattern 调用期间有效。
    /// 图案通过它读取 angle / interval（旋转弹幕用），
    /// 避免给十几个方法逐个加参数。ExecutePattern 是同步的，不存在轨道交错。
    /// </summary>
    private EmitterTrack activeTrack;

    // ---- 正在放的弹幕卡（前台 + 后台）----
    private struct ComboRecord
    {
        public int id;
        public float endTime;
        public bool reactive; // 转到弹幕层后，Boss 走位时玩家贴近也会触发近身反应（由出招状态标记）
    }

    private readonly List<ComboRecord> combos = new List<ComboRecord>(4);
    private int nextComboId = 1;

    // 当前这一步（前台）的那张卡；-1 = 没有。打断只停它，转到后台的不归 Cancel 管
    private int foregroundId = -1;
    private bool releaseRequested;
    private bool foregroundFollow = true; // 前台那张卡转到弹幕层后跟不跟随 Boss（脱手 / 托管 / 打断时补交接都用它）
    private bool? nextFollow;             // 出招方指定的"下一张卡交出去后跟不跟随"（套路步骤覆盖）；null = 按卡上的

    /// <summary>出下一张弹幕卡之前调用：指定它交到弹幕层后跟不跟随 Boss（只对下一次 Execute 生效）</summary>
    public void SetNextFollow(bool follow) => nextFollow = follow;

    // 脱手时选了「不跟随 Boss」的卡：在脱手那一刻 Boss 站的位置放一个锚点，这张卡之后的子弹从锚点发。
    // 键 = 卡的编号；这张卡停掉时销毁
    private readonly Dictionary<int, Transform> anchors = new Dictionary<int, Transform>();

    /// <summary>当前这一步（前台）那张卡的编号；-1 = 没有</summary>
    public int ForegroundComboId => foregroundId;

    /// <summary>这张卡还在发吗</summary>
    public bool IsComboAlive(int comboId)
    {
        for (int i = 0; i < combos.Count; i++)
        {
            if (combos[i].id == comboId) return true;
        }
        return false;
    }

    private float ComboEndTime(int comboId)
    {
        for (int i = 0; i < combos.Count; i++)
        {
            if (combos[i].id == comboId) return combos[i].endTime;
        }
        return 0f;
    }

    /// <summary>标记这张卡转到弹幕层后是否允许走位中的近身反应</summary>
    public void SetComboReactive(int comboId, bool reactive)
    {
        for (int i = 0; i < combos.Count; i++)
        {
            if (combos[i].id != comboId) continue;
            ComboRecord r = combos[i];
            r.reactive = reactive;
            combos[i] = r;
        }
    }

    /// <summary>
    /// 弹幕层里有没有「允许近身反应、并且跟随 Boss」的弹幕（Boss 走位时据此检查近身反应）。
    /// 钉在原地的不算
    /// </summary>
    public bool HasReactiveBarrage
    {
        get
        {
            for (int i = 0; i < combos.Count; i++)
            {
                ComboRecord r = combos[i];
                if (r.id != foregroundId && r.reactive && !anchors.ContainsKey(r.id)) return true;
            }
            return false;
        }
    }

    /// <summary>弹幕层里有没有弹幕在发：已经脱手（不是当前这一步）的卡还没结束</summary>
    public bool HasReleasedBarrage
    {
        get
        {
            for (int i = 0; i < combos.Count; i++)
            {
                if (combos[i].id != foregroundId) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 给刚出膛的子弹写上击退配置。
    /// 所有生成子弹的地方都要调它（SpawnBullet / CreateFormationParent 内部已调用）。
    /// </summary>
    /// <param name="knockback">不填 = 用当前正在发射的轨道所属那张卡的设置</param>
    public void StampKnockback(GameObject bullet, BulletKnockback? knockback = null)
    {
        if (bullet == null) return;
        Enemy_Projectile p = bullet.GetComponent<Enemy_Projectile>();
        if (p == null) return;

        BulletKnockback k = knockback ?? (activeTrack != null ? activeTrack.knockback : default);
        p.SetKnockback(k.overrideKnockback, k.force);
    }

    /// <summary>正在发射的轨道所属那张卡的击退设置（图案要在之后慢慢发子弹时，先用它记下来）</summary>
    public BulletKnockback CurrentKnockback => activeTrack != null ? activeTrack.knockback : default;

    //实现IBOOSACTIONEXECUTOR
    public System.Type NodeType => typeof(BulletNode);

    public IEnumerator Execute(ActionNode node, BossContext ctx)
    {
        BulletNode bulletNode = node as BulletNode;
        bool follow = nextFollow ?? (bulletNode == null || bulletNode.followBossWhenReleased);
        nextFollow = null;
        if (bulletNode == null) yield break;

        if (!TryStartCombo(bulletNode, out int id))
        {
            yield return new WaitForSeconds(1f); // 配置有误，停顿一下防止空转刷屏
            yield break;
        }

        foregroundId = id;
        foregroundFollow = follow;
        releaseRequested = false;
        yield return WaitForeground(bulletNode, id);
    }

    /// <summary>
    /// 等前台这张卡放完；中途收到脱手 / 托管指令就交给弹幕层，马上返回（这一招对 Boss 来说算演完了）。
    /// 开卡（Execute）和近身反应后取回（Reclaim 之后）都用它
    /// </summary>
    public IEnumerator WaitForeground(BulletNode node, int comboId)
    {
        // 跟不跟随用开卡时定下的 foregroundFollow（套路步骤可能覆盖了卡上的设置；取回后沿用同一个）

        // 发射器的 Update 会在这张卡到点时把它收掉；这里另外按结束时间判断一次，防止发射器被禁用时一直等
        while (IsComboAlive(comboId) && Clock < ComboEndTime(comboId))
        {
            if (releaseRequested)
            {
                HandOff(comboId, foregroundFollow, node.actionName);
                yield break;
            }
            yield return null;
        }

        StopCombo(comboId);
        if (foregroundId == comboId) foregroundId = -1;
    }

    /// <summary>把前台这张卡交给弹幕层：不跟随 Boss 的就在此刻的位置钉住</summary>
    private void HandOff(int comboId, bool follow, string cardName)
    {
        if (!follow) PinFirePoint(comboId);
        Debug.Log($"[Emitter] 弹幕「{cardName}」转到弹幕层继续发（{(follow ? "跟随 Boss" : "钉在原地")}）");
        if (foregroundId == comboId) foregroundId = -1;
        releaseRequested = false;
    }

    /// <summary>
    /// 近身反应打完、取回：把还在发的这张卡重新挂回 Boss 身上（钉住的锚点撤掉，子弹回到 Boss 身上发）。
    /// 返回 false = 这张卡已经发完了
    /// </summary>
    public bool Reclaim(int comboId)
    {
        if (!IsComboAlive(comboId)) return false;
        RemoveAnchor(comboId);
        foregroundId = comboId;
        releaseRequested = false;
        Debug.Log("[Emitter] 近身反应打完，弹幕取回到 Boss 身上");
        return true;
    }

    /// <summary>打断：只停当前这一步的那张卡，转到后台的继续发</summary>
    public void Cancel()
    {
        // 已经发出脱手 / 托管指令、还没来得及处理：当作已经交出去了，接着发完
        if (foregroundId >= 0 && releaseRequested) HandOff(foregroundId, foregroundFollow, "（打断时补交接）");
        else if (foregroundId >= 0) StopCombo(foregroundId);
        foregroundId = -1;
        releaseRequested = false;
    }

    public bool ReleaseToBackground()
    {
        if (foregroundId < 0) return false;
        releaseRequested = true;
        return true;
    }

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

            if (track.Tick(BossTime.Delta(Owner)))
            {
                activeTrack = track;
                ExecutePattern(track.pattern);
                activeTrack = null;
            }
        }

        // 每张卡到自己的结束时间就收掉（转到后台的卡靠这里结束）
        for (int i = combos.Count - 1; i >= 0; i--)
        {
            if (Clock >= combos[i].endTime) StopCombo(combos[i].id);
        }
    }

    // ==========================================
    // 组合启动
    // ==========================================

    /// <summary>按卡片配置铺开所有轨道。返回是否成功启动。已经在放的其他卡不受影响</summary>
    public bool StartCombo(BulletNode node) => TryStartCombo(node, out _);

    /// <summary>按卡片配置铺开所有轨道，并给这张卡分配一个编号。已经在放的其他卡不受影响</summary>
    public bool TryStartCombo(BulletNode node, out int comboId)
    {
        comboId = -1;
        int id = nextComboId++;
        BulletKnockback knockback = new BulletKnockback(node.overrideKnockback, node.knockbackForce);

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

                AddTrack(phase.pattern, phase.startDelay, phase.duration, id, knockback);
                added++;
            }
        }

        if (added == 0)
        {
            Debug.LogError($"[Emitter] 卡片 {node.name} 没有任何可用的弹幕段（Phases 为空或都没选类型）！", this);
            return false;
        }

        combos.Add(new ComboRecord { id = id, endTime = Clock + node.TotalDuration });
        comboId = id;
        return true;
    }

    /// <summary>取一条空闲轨道复用，没有就新建。避免每次组合都产生 GC。</summary>
    private void AddTrack(BulletPatternBase pattern, float startDelay, float duration, int comboId, BulletKnockback knockback)
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].IsFinished)
            {
                tracks[i].Setup(pattern, startDelay, duration, comboId, knockback);
                return;
            }
        }

        EmitterTrack track = new EmitterTrack();
        track.Setup(pattern, startDelay, duration, comboId, knockback);
        tracks.Add(track);
    }

    /// <summary>只停某一张卡的轨道（重复调用是安全的空操作）</summary>
    public void StopCombo(int comboId)
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].comboId == comboId) tracks[i].Finish();
        }
        for (int i = combos.Count - 1; i >= 0; i--)
        {
            if (combos[i].id == comboId) combos.RemoveAt(i);
        }
        RemoveAnchor(comboId);
    }

    /// <summary>全部停：前台和后台的弹幕卡一起停。破盾 / 转阶段 / 死亡时用</summary>
    public void StopAttack()
    {
        for (int i = 0; i < tracks.Count; i++) tracks[i].Finish();
        combos.Clear();
        foregroundId = -1;
        releaseRequested = false;
        activeTrack = null;

        foreach (Transform a in anchors.Values)
        {
            if (a != null) Destroy(a.gameObject);
        }
        anchors.Clear();
    }

    // ==========================================
    // 钉在原地（脱手时不跟随 Boss）
    // ==========================================

    /// <summary>在发射点此刻的位置放一个锚点，这张卡之后的子弹都从锚点发</summary>
    private void PinFirePoint(int comboId)
    {
        if (firePoint == null || anchors.ContainsKey(comboId)) return;

        GameObject anchor = new GameObject($"BarrageAnchor_{comboId}");
        anchor.transform.SetPositionAndRotation(firePoint.position, firePoint.rotation);
        anchors[comboId] = anchor.transform;
    }

    private void RemoveAnchor(int comboId)
    {
        if (!anchors.TryGetValue(comboId, out Transform a)) return;
        if (a != null) Destroy(a.gameObject);
        anchors.Remove(comboId);
    }

    private void ExecutePattern(BulletPatternBase pattern)
    {
        // 钉在原地的卡从锚点发，其余从 Boss 身上的发射点发
        Transform origin = firePoint;
        if (activeTrack != null && anchors.TryGetValue(activeTrack.comboId, out Transform anchor) && anchor != null)
            origin = anchor;

        BulletSpawnContext ctx = new BulletSpawnContext();
        ctx.SetUp(origin, playerTransform, GetPlayerTargetPosition(), ResolveKey(pattern), activeTrack, this);
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

    /// <summary>所属 Boss（动作倍率从它那里读）</summary>
    private BossController Owner
    {
        get
        {
            if (bossController == null) bossController = GetComponent<BossController>();
            return bossController;
        }
    }

    /// <summary>Boss 时钟（跟动作倍率走）。发射间隔、每张卡的结束时间都按它算；子弹飞行不受影响</summary>
    private float Clock => Owner != null ? Owner.ActionTime : Time.time;

    /// <summary>按 Boss 时间等待（图案里慢慢放子弹时用，代替 WaitForSeconds）</summary>
    public IEnumerator WaitScaled(float seconds) => BossTime.Wait(Owner, seconds);

    /// <summary>
    /// 创建一个阵型父物体：从对象池取子弹挂在它下面，交给 ShapeFormationController 展开。
    /// 子弹的外观、大小、存活时间、击退按图案的通用设置套用；移动由阵型负责。
    /// </summary>
    public ShapeFormationController CreateFormationParent(BulletPatternBase pattern, Vector2[] bulletPositions,
                                                          string parentName, Vector3 spawnPos, bool isControlledByFormation = true)
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
                //暂时给一个随机的旋转角度
                p.Setup(Random.insideUnitCircle.normalized);
                p.isControlledByFormation = isControlledByFormation;
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
    /// <param name="knockback">不填 = 用当前正在发射的轨道所属那张卡的击退；图案在之后（协程里）慢慢发子弹时要传入开始时记下的</param>
    public GameObject SpawnBullet(BulletPatternBase pattern, Vector2 dir, Vector3 spawnPos, float? speedOverride = null,
                                  BulletKnockback? knockback = null)
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

        StampKnockback(bullet, knockback);
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
