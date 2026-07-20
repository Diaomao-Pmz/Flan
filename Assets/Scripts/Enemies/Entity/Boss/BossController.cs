using TMPro;
using UnityEngine;
using Flandre.CombatSystem;

[RequireComponent(typeof(BossState))]
[RequireComponent(typeof(BossTeleporter))] // 【新增】强制要求挂载传送器组件
public class BossController : EntityBase
{
    [Header("--- 表现层引用 ---")]
    public GameObject shieldVisual;
    public Animator animator { get; private set; }
    [SerializeField] Transform playerTransform;

    public BossAIDecider AI { get; private set; }

    // 状态定义
    public BossActionExecuter CombatState { get; private set; }
    public BossMoveState MoveState { get; private set; }

    // 各专职部门的执行器引用
    public BossBulletEmitter BulletEmitter { get; private set; }
    public BossTeleporter Teleporter { get; private set; } // 【新增】传送专职执行器
    public BossState bossState { get; private set; }

    [Header("--- 移动风筝系统参数 ---")]
    // 注意：maintainDistance 已被彻底删除，由 MoveState 动态向 AI 索取
    public float moveSpeed = 3f;              // 移动速度
    public float wallCheckDistance = 1.5f;    // 撞墙射线检测距离
    public LayerMask wallLayer;               // 墙壁/死角的图层

    [Header("UI For Testing")]
    public TextMeshProUGUI bossStatusText;

    public IState CurrentState { get; private set; }
    public Transform PlayerTransform => playerTransform;
    public float DistanceToPlayer => Vector2.Distance(transform.position, playerTransform.position);

    protected override void Awake()
    {
        base.Awake();

        // 统一获取各组件引用
        bossState = GetComponent<BossState>();
        animator = GetComponent<Animator>();
        AI = GetComponent<BossAIDecider>();
        BulletEmitter = GetComponent<BossBulletEmitter>();
        Teleporter = GetComponent<BossTeleporter>(); // 获取传送器

        CombatState = new BossActionExecuter(this);
        MoveState = new BossMoveState(this);
    }

    void Start()
    {
        // 统一初始化各部门
        if (BulletEmitter != null) BulletEmitter.Init(playerTransform);
        if (Teleporter != null) Teleporter.Init(playerTransform);

        // 统一订阅黑板事件
        if (bossState != null)
        {
            bossState.bossMechanic.OnShieldBroken += HandleShieldBroken;
            bossState.bossMechanic.OnShieldBroken += HideShieldVisual;
            bossState.bossMechanic.OnShieldRecovered += ShowShieldVisual;

            // 【关键修改】将被动防反传送事件，直接委托给传送器执行随机传送策略
            bossState.bossMechanic.OnTeleportTriggered += TriggerPassiveTeleport;
            bossState.bossMechanic.OnPhase2Triggered += HandlePhase2;
            bossState.health.OnDeath += Die;
        }

        // Boss 开始时先进入风筝移动状态
        ChangeState(MoveState);
    }

    void Update()
    {
        if (bossState != null && bossState.health.isDead) return;
        CurrentState?.Update();
    }

    public void ChangeState(IState state)
    {
        if (CurrentState != null) CurrentState.Exit();
        CurrentState = state;
        CurrentState?.Enter();
    }

    private void HandleShieldBroken()
    {
        ChangeState(new BossStunState(this));
    }

    // --- 以下为事件响应的封装方法（为了方便 OnDestroy 时干净地注销） ---

    private void TriggerPassiveTeleport()
    {
        if (Teleporter != null)
            Teleporter.ExecuteTeleport(TeleportTargetType.RandomPoint);
    }

    private void HideShieldVisual() { if (shieldVisual != null) shieldVisual.SetActive(false); }
    private void ShowShieldVisual() { if (shieldVisual != null) shieldVisual.SetActive(true); }

    private void HandlePhase2()
    {
        Debug.Log("[BossController] 触发二阶段！");
        if (BulletEmitter != null) BulletEmitter.StopAttack();
        if (GetComponent<Rigidbody2D>() != null) GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;

        // 【关键修改】二阶段转场传送到中央，不再写死坐标，直接委托给传送器
        if (Teleporter != null) Teleporter.ExecuteTeleport(TeleportTargetType.Center);

        if (AI != null) AI.SwitchToPhase2();
    }

    protected override void Die()
    {
        Debug.Log("Boss被击败了！触发死亡演出！");
        if (CurrentState != null) CurrentState.Exit();
        if (BulletEmitter != null) BulletEmitter.StopAttack();
    }

    void OnDestroy()
    {
        if (bossState != null)
        {
            bossState.bossMechanic.OnShieldBroken -= HandleShieldBroken;
            bossState.bossMechanic.OnShieldBroken -= HideShieldVisual;
            bossState.bossMechanic.OnShieldRecovered -= ShowShieldVisual;

            bossState.bossMechanic.OnTeleportTriggered -= TriggerPassiveTeleport;
            bossState.bossMechanic.OnPhase2Triggered -= HandlePhase2;
            bossState.health.OnDeath -= Die;
        }
    }

    public override void TakeDamage(int damage, DamageType type)
    {
        if (bossState != null)
        {
            // 将伤害请求转发给黑板 (BossState) 处理
            bossState.bossMechanic.TakeDamage(damage, type);
        }
    }
}