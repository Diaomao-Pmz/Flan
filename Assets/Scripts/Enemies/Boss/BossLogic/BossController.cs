using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Flandre.CombatSystem;

[RequireComponent(typeof(BossState))]
[RequireComponent(typeof(BAE_Teleporter))]
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
    public BAE_Teleporter Teleporter { get; private set; }
    public BossState bossState { get; private set; }

    [Header("--- 移动风筝系统参数 ---")]
    public float moveSpeed = 3f;
    public float wallCheckDistance = 1.5f;
    public LayerMask wallLayer;

    [Header("--- 打断表现 ---")]
    [Tooltip("动作被打断后的硬直时长（秒）。玩家的追击窗口")]
    public float interruptStaggerDuration = 0.6f;

    [Header("UI For Testing")]
    public TextMeshProUGUI bossStatusText;

    public IState CurrentState { get; private set; }
    public Transform PlayerTransform => playerTransform;
    public float DistanceToPlayer => Vector2.Distance(transform.position, playerTransform.position);

    /// <summary>
    /// 当前正在执行的动作卡。由 BossActionExecuter 在开演/收工时写入。
    /// 打断判定需要知道"现在做的是哪一类动作"。
    /// </summary>
    public ActionNode CurrentActionNode { get; private set; }

    /// <summary>被打断后进入的短暂硬直是否仍在计时</summary>
    public bool IsStaggered => Time.time < staggerEndTime;
    private float staggerEndTime = -1f;

    public BossContext Context { get; private set; }

    private readonly Dictionary<Type, IBossActionExecutor> executors
        = new Dictionary<Type, IBossActionExecutor>();

    protected override void Awake()
    {
        base.Awake();

        bossState = GetComponent<BossState>();
        animator = GetComponent<Animator>();
        AI = GetComponent<BossAIDecider>();
        BulletEmitter = GetComponent<BAE_BulletEmitter>();
        Teleporter = GetComponent<BAE_Teleporter>();

        CombatState = new BossActionExecuter(this);
        MoveState = new BossMoveState(this);
        StunState = new BossStunState(this);

        BuildExecutorRegistry();
    }

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

    public IBossActionExecutor GetExecutorFor(ActionNode node)
    {
        if (node == null) return null;
        return executors.TryGetValue(node.GetType(), out IBossActionExecutor executor) ? executor : null;
    }

    public void CancelAllExecutors()
    {
        foreach (IBossActionExecutor executor in executors.Values)
        {
            executor.Cancel();
        }
    }

    /// <summary>由 BossActionExecuter 调用，登记/清除当前动作卡</summary>
    public void SetCurrentActionNode(ActionNode node) => CurrentActionNode = node;

    void Start()
    {
        if (BulletEmitter != null) BulletEmitter.Init(playerTransform);
        if (Teleporter != null) Teleporter.Init(playerTransform);

        Context = new BossContext(this, playerTransform);

        if (bossState != null)
        {
            bossState.bossMechanic.OnShieldBroken += HandleShieldBroken;
            bossState.bossMechanic.OnShieldBroken += HideShieldVisual;
            bossState.bossMechanic.OnShieldRecovered += ShowShieldVisual;

            bossState.bossMechanic.OnTeleportTriggered += TriggerPassiveTeleport;
            bossState.bossMechanic.OnPhase2Triggered += HandlePhase2;
            bossState.bossMechanic.OnInterruptRequested += HandleInterruptRequest;

            bossState.health.OnDeath += Die;

            // 把黑板血量镜像回基类字段，详见 MirrorHealthToBase 的说明
            bossState.health.OnStatChanged += MirrorHealthToBase;
            MirrorHealthToBase();
        }

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

    // ==========================================================
    // 打断
    // ==========================================================

    /// <summary>
    /// 玩家打出了带打断能力的攻击。
    ///
    /// 【裁决在这里做，不在黑板里】
    /// BossMechanic 只广播"有人想打断我"，它不知道当前在演哪张卡；
    /// 而 BossActionExecuter 知道卡但不该管护盾与状态流转。
    /// 只有 Controller 同时握着这两份信息，所以裁决归它。
    /// </summary>
    private void HandleInterruptRequest(DamageInfo info)
    {
        // 不在出招 → 没什么可打断的
        if (CurrentState != CombatState || CurrentActionNode == null) return;

        if (!CurrentActionNode.CanBeInterruptedBy(info))
        {
            // 打断失败也是有意义的反馈 —— 玩家该知道这一招破不了这个动作
            OnInterruptFailed?.Invoke(CurrentActionNode);
            return;
        }

        Debug.Log($"[BossController] 动作「{CurrentActionNode.actionName}」" +
                  $"({CurrentActionNode.Category}) 被打断");

        CancelAllExecutors();
        SetCurrentActionNode(null);

        staggerEndTime = Time.time + interruptStaggerDuration;

        OnActionInterrupted?.Invoke();

        // 回到移动状态。硬直期间 AI 不会立刻再出招（见 BossMoveState 的等待）
        ChangeState(MoveState);
    }

    /// <summary>动作被成功打断时广播。特效、音效、顿帧订阅这个</summary>
    public event Action OnActionInterrupted;

    /// <summary>打断失败时广播（韧性不够）。可以做个"叮"的弹刀反馈</summary>
    public event Action<ActionNode> OnInterruptFailed;

    // ==========================================================
    // 事件响应
    // ==========================================================

    private void HandleShieldBroken()
    {
        ChangeState(StunState);
    }

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

        CancelAllExecutors();
        SetCurrentActionNode(null);

        if (TryGetComponent(out Rigidbody2D rb)) rb.linearVelocity = Vector2.zero;

        if (Teleporter != null) Teleporter.ExecuteTeleport(TeleportTargetType.Center);

        if (AI != null) AI.SwitchToPhase2();

        // 二阶段转场是一次强制重置：立刻满盾，并把状态机从破防中拽出来。
        bossState.bossMechanic.RecoverShield();
        ChangeState(MoveState);
    }

    protected override void Die()
    {
        Debug.Log("Boss被击败了！触发死亡演出！");
        if (CurrentState != null) CurrentState.Exit();
        CancelAllExecutors();
        SetCurrentActionNode(null);
    }

    /// <summary>
    /// 把黑板上的血量镜像回 EntityBase 的字段。
    ///
    /// 【为什么需要这一步】
    /// Boss 有两套血量：EntityBase.currentHP 和 BossState.health.currentHP。
    /// 因为 TakeDamage 被 override 成转发给黑板，基类那份【从来不会被扣】——
    /// 它永远显示满血。
    ///
    /// 直接把基类的血量字段删掉是不行的：EntityBase 是所有敌人的基类，
    /// 以后的小怪会用它，而且判定框、UI 都靠 GetComponentInParent&lt;EntityBase&gt;() 找目标。
    ///
    /// 所以选择镜像 —— 让基类字段说真话，
    /// 这样任何写在 EntityBase 上的通用逻辑（承伤统计、受击闪白…）
    /// 对 Boss 和小怪的表现才会一致，不会出现"小怪有、Boss 没有"的诡异现象。
    /// </summary>
    private void MirrorHealthToBase()
    {
        if (bossState == null) return;

        maxHP = bossState.health.maxHP;
        SetCurrentHP(bossState.health.currentHP);
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
            bossState.bossMechanic.OnInterruptRequested -= HandleInterruptRequest;

            bossState.health.OnDeath -= Die;
            bossState.health.OnStatChanged -= MirrorHealthToBase;
        }
    }

    public override void TakeDamage(in DamageInfo info)
    {
        if (bossState != null)
        {
            // 整个载荷转发给黑板 —— 不再拆成 (int, DamageType)，
            // 这样以后 DamageInfo 加字段时本行不用改。
            bossState.bossMechanic.TakeDamage(info);
        }
    }
}
