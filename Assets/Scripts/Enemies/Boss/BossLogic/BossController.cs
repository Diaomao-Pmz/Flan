using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Flandre.CombatSystem;

[RequireComponent(typeof(BossState))]
[RequireComponent(typeof(BAE_Teleporter))] // 【新增】强制要求挂载传送器组件
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
    public BossStunState StunState { get; private set; }

    // 各专职部门的执行器引用
    public BAE_BulletEmitter BulletEmitter { get; private set; }
    public BAE_Teleporter Teleporter { get; private set; } // 【新增】传送专职执行器
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

    /// <summary>
    /// 只读情报总线。所有执行器与条件对象共用同一份，避免各自 FindObjectOfType。
    /// 在 Start 中组装，确保各组件的 Awake 都已跑完。
    /// </summary>
    public BossContext Context { get; private set; }

    // 【新增】Node 类型 → 执行器 的映射表。加新技能不再需要改任何分派代码。
    private readonly Dictionary<Type, IBossActionExecutor> executors
        = new Dictionary<Type, IBossActionExecutor>();

    protected override void Awake()
    {
        base.Awake();

        // 统一获取各组件引用
        bossState = GetComponent<BossState>();
        animator = GetComponent<Animator>();
        AI = GetComponent<BossAIDecider>();
        BulletEmitter = GetComponent<BAE_BulletEmitter>();
        Teleporter = GetComponent<BAE_Teleporter>(); // 获取传送器

        CombatState = new BossActionExecuter(this);
        MoveState = new BossMoveState(this);
        StunState = new BossStunState(this);   // 复用实例，不再每次破盾 new 一个

        BuildExecutorRegistry();
    }

    /// <summary>
    /// 扫描挂在自己身上的所有执行器，建立「认领关系」。
    /// 执行器只要挂上来就会被自动发现，不需要在这里逐个登记。
    /// </summary>
    private void BuildExecutorRegistry()
    {
        executors.Clear();

        foreach (IBossActionExecutor executor in GetComponents<IBossActionExecutor>())
        {
            Type nodeType = executor.NodeType;

            if (nodeType == null)
            {
                Debug.LogError($"[BossController] {executor.GetType().Name} 的 NodeType 为空，已跳过。", this);
                continue;
            }

            if (executors.ContainsKey(nodeType))
            {
                Debug.LogError(
                    $"[BossController] {nodeType.Name} 被重复认领：" +
                    $"{executors[nodeType].GetType().Name} 与 {executor.GetType().Name}。后者已忽略。", this);
                continue;
            }

            executors[nodeType] = executor;
        }
    }

    /// <summary>按卡片的运行时类型找到对应执行器。找不到返回 null。</summary>
    public IBossActionExecutor GetExecutorFor(ActionNode node)
    {
        if (node == null) return null;
        return executors.TryGetValue(node.GetType(), out IBossActionExecutor executor) ? executor : null;
    }

    /// <summary>通知所有执行器立即收摊。破盾、转阶段、死亡时统一调用。</summary>
    public void CancelAllExecutors()
    {
        foreach (IBossActionExecutor executor in executors.Values)
        {
            executor.Cancel();
        }
    }

    void Start()
    {
        // 统一初始化各部门
        if (BulletEmitter != null) BulletEmitter.Init(playerTransform);
        if (Teleporter != null) Teleporter.Init(playerTransform);

        // 组装只读情报总线
        Context = new BossContext(this, playerTransform);

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
        ChangeState(StunState);
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

        // 【改动】不再只停弹幕，通知所有执行器收摊
        CancelAllExecutors();

        if (TryGetComponent(out Rigidbody2D rb)) rb.linearVelocity = Vector2.zero;

        // 【关键修改】二阶段转场传送到中央，不再写死坐标，直接委托给传送器
        if (Teleporter != null) Teleporter.ExecuteTeleport(TeleportTargetType.Center);

        if (AI != null) AI.SwitchToPhase2();

        // 二阶段转场是一次强制重置：立刻满盾，并把状态机从破防中拽出来。
        // 不做这两件事的话，CurrentState 会一直停在 BossStunState 上数完剩余的破防时间。
        bossState.bossMechanic.RecoverShield();
        ChangeState(MoveState);
    }

    protected override void Die()
    {
        Debug.Log("Boss被击败了！触发死亡演出！");
        if (CurrentState != null) CurrentState.Exit();
        CancelAllExecutors();
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

    public override void TakeDamage(in DamageInfo info)
    {
        if (bossState != null)
        {
            // 将伤害请求转发给黑板 (BossState) 处理
            bossState.bossMechanic.TakeDamage(info.amount, info.type);
        }
    }
}