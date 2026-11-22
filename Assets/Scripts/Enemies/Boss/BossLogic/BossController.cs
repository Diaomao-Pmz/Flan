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
    public BossKnockbackState KnockbackState { get; private set; }

    // 各专职部门的执行器引用
    public BAE_BulletEmitter BulletEmitter { get; private set; }
    public BAE_Teleporter Teleporter { get; private set; } // 【新增】传送专职执行器
    public BossState bossState { get; private set; }

    [Header("--- 移动风筝系统参数 ---")]
    // 注意：maintainDistance 已被彻底删除，由 MoveState 动态向 AI 索取
    // 移动速度已搬到 BossAIDecider 的 Ranged / Melee Tempo（每个姿态一套，另有二阶段倍率）
    public float wallCheckDistance = 1.5f;    // 撞墙射线检测距离
    public LayerMask wallLayer;               // 墙壁/死角的图层

    [Header("--- 被击退 ---")]
    [Tooltip("被玩家的招式击退时，滑多久（秒）。滑多远由招式决定（ComboNode 的 Boss Knockback Distance）。\n" +
             "速度由快到慢。招式还带硬直（Boss Hit Stun）时取两者里长的那个，结束后回到移动状态重新做决定。时间越长显得越「重」")]
    [Min(0.02f)]
    public float knockbackDuration = 0.25f;

    [Header("UI For Testing")]
    public TextMeshProUGUI bossStatusText;

    [Tooltip("显示 Boss 当前的移动意图（靠近 / 后退 / 到位 / 传送中 / 出招中）")]
    public TextMeshProUGUI movementStatusText;

    private string shownMovementStatus;

    /// <summary>刷新移动意图文字。内容没变就不重写，避免每帧重建文字网格</summary>
    public void SetMovementStatus(string text)
    {
        if (movementStatusText == null || text == shownMovementStatus) return;
        shownMovementStatus = text;
        movementStatusText.SetText(text);
    }

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
        KnockbackState = new BossKnockbackState(this);

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
            bossState.bossMechanic.OnShieldRecovered += HandleShieldRecovered;

            // 挨打 → 交给传送器判断要不要受击传送（概率、冷却、姿态都由 TeleportNode 配置）
            bossState.bossMechanic.OnHitTaken += HandleHitTaken;
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

    // 物理帧也转给当前状态（被击退靠它按精确距离推进；其余状态的 FixedUpdate 目前是空的）
    void FixedUpdate()
    {
        if (bossState != null && bossState.health.isDead) return;
        CurrentState?.FixedUpdate();
        TickPush();
    }

    // ==========================================================
    // 持续推动（玩家激光这类持续伤害，2026-11-21）
    // ==========================================================

    private Rigidbody2D body;
    private float pushDirection;
    private float pushSpeed;
    private float pushUntil;
    private readonly List<RaycastHit2D> pushHits = new List<RaycastHit2D>(8);

    /// <summary>正在被持续推着走</summary>
    public bool IsBeingPushed => pushSpeed > 0f && Time.time < pushUntil;

    /// <summary>
    /// 记下这次命中带来的推力：方向 = 远离攻击来源，维持 info.pushDuration 秒（连续命中就一直续上）。
    /// 眩晕中不推。不改状态、不打断出招。
    /// </summary>
    private void HandlePush(in DamageInfo info)
    {
        if (info.pushSpeed <= 0f || info.pushDuration <= 0f || IsStunned) return;

        float dx = transform.position.x - info.sourcePosition.x;
        pushDirection = Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : (LastAttackFacing != 0f ? -LastAttackFacing : 1f);
        pushSpeed = info.pushSpeed;
        pushUntil = Mathf.Max(pushUntil, Time.time + info.pushDuration);
    }

    /// <summary>
    /// 推力叠在 Boss 自己的移动之上：直接挪位置，不碰速度，所以走位 / 出招 / 被击退滑行都照常。
    /// 往墙（wallLayer）里推时只推到贴墙为止。
    /// </summary>
    private void TickPush()
    {
        if (!IsBeingPushed) return;
        if (IsStunned) { pushSpeed = 0f; return; }

        if (body == null && !TryGetComponent(out body)) return;

        float step = pushSpeed * Time.fixedDeltaTime;
        Vector2 dir = new Vector2(pushDirection, 0f);

        if (wallLayer.value != 0)
        {
            ContactFilter2D filter = new ContactFilter2D { useLayerMask = true, layerMask = wallLayer, useTriggers = false };
            body.Cast(dir, filter, pushHits, step + 0.02f);
            for (int i = 0; i < pushHits.Count; i++)
            {
                // 只认挡在推进方向上的面（脚下地面的法线朝上，不算）
                if (pushHits[i].normal.x * pushDirection > -0.5f) continue;
                step = Mathf.Min(step, Mathf.Max(0f, pushHits[i].distance - 0.02f));
            }
        }

        if (step > 0f) body.position += dir * step;
    }

    public void ChangeState(IState state)
    {
        if (CurrentState != null) CurrentState.Exit();
        CurrentState = state;
        CurrentState?.Enter();
    }

    private void HandleShieldBroken()
    {
        // 姿态切到近战：远程姿态下预约的意图（比如想拉远放弹幕）作废
        if (AI != null) AI.OnCombatModeChanged();
        ChangeState(StunState);
    }

    /// <summary>
    /// 护盾恢复 → 回到远程为主阶段。
    /// 正在放的那一招放完再说，不强行打断；下一次抽卡自然按远程姿态来。
    /// </summary>
    private void HandleShieldRecovered()
    {
        if (AI != null) AI.OnCombatModeChanged();
    }

    // --- 以下为事件响应的封装方法（为了方便 OnDestroy 时干净地注销） ---

    private void HandleHitTaken(DamageType type, BossCombatMode modeAtHit)
    {
        if (Teleporter != null) Teleporter.OnBossHit(type, modeAtHit);
    }

    /// <summary>
    /// 上一次近战 / 突进出手的朝向：+1 = 朝右，-1 = 朝左，0 = 还没出过手。
    /// Boss 没有常驻朝向（每招出手时临时转向玩家），所以靠这个记住"刚才打的是哪边"，
    /// 用来判断玩家是不是绕到了背后（套路候补招式的条件）。
    /// </summary>
    public float LastAttackFacing { get; private set; }

    /// <summary>执行器出手时登记朝向</summary>
    public void RecordAttackFacing(float sign)
    {
        if (sign != 0f) LastAttackFacing = Mathf.Sign(sign);
    }

    /// <summary>当前是否处于破盾眩晕中</summary>
    public bool IsStunned => CurrentState == StunState;

    /// <summary>
    /// 紧急收招：掐掉正在放的招、作废套路，回到移动状态。
    /// 给"勾了 Interrupt Action 的受击传送"这类外部打断使用。
    /// 眩晕中不处理（眩晕有自己的时长，不能被打断提前结束）。
    /// </summary>
    public void InterruptCurrentAction(string reason)
    {
        if (IsStunned) return;

        // 意图（单招 / 套路 / 兜底卡）全部作废，回到移动状态后重新定
        if (AI != null) AI.ClearIntent(reason);

        // CombatState.Exit 会停掉出招协程并通知执行器收摊
        if (CurrentState == CombatState) ChangeState(MoveState);
    }

    /// <summary>
    /// 近战挥击被玩家拼刀（MeleeSwingRunner 在结算伤害前发现的）。
    /// 只结束当前这一招：剩下的挥击由 Runner 自己收掉（它提前结束，出招协程照常走完 → Finish），
    /// 套路照常接下一步。套路 / 招式的冷却在出手时就开始算了，被拼掉不影响冷却。
    /// </summary>
    public void OnMeleeClashed(ComboNode by)
    {
        string who = by != null ? by.nodeName : "玩家";
        Debug.Log($"[Boss] 被 {who} 拼刀：只结束这一招，套路继续");
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
            bossState.bossMechanic.OnShieldRecovered -= HandleShieldRecovered;

            bossState.bossMechanic.OnHitTaken -= HandleHitTaken;
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
            if (bossState.health.isDead) return;
        }

        HandleBreakAndKnockback(info);
        HandlePush(info);
    }

    // ==========================================================
    // 打断 / 击退（2026-11-21）
    // ==========================================================

    /// <summary>Boss 的每类卡对应一个动作类别，和玩家招式的 Break Mask 对位</summary>
    public static ActionCategory CategoryOf(ActionNode node)
    {
        if (node is MeleeNode) return ActionCategory.Melee;
        if (node is BulletNode) return ActionCategory.Bullet;
        if (node is DashAttackNode) return ActionCategory.SpecialMelee;
        if (node is LaserNode) return ActionCategory.SpecialBullet;
        if (node is TeleportNode) return ActionCategory.Teleport;
        return ActionCategory.None;
    }

    /// <summary>
    /// 挨打后：能不能打断这一招？要不要被击退 / 硬直？
    ///   · 正在出招，且招式的 Break Mask 包含这一招的类别 → 打断这一招并作废整个套路
    ///     （传送永远打不断：任何 Break Mask 都不该勾 Teleport）
    ///   · 招式带击退距离或硬直时间，并且（打断了 / 本来就没在出招）→ 受击硬直：边滑边硬直，结束回移动状态
    ///   · 眩晕中不处理（眩晕有自己的时长和击飞）
    /// 距离 / 硬直由攻击方给（DamageInfo.knockbackForce.x、hitStun），怎么滑、滑多久由 Boss 自己决定。
    /// </summary>
    private void HandleBreakAndKnockback(in DamageInfo info)
    {
        if (IsStunned) return;

        ActionNode action = CurrentState == CombatState ? CombatState.CurrentAction : null;
        bool acting = action != null;
        bool broke = acting && info.CanBreak(CategoryOf(action));

        float distance = info.overrideKnockback ? Mathf.Max(0f, info.knockbackForce.x) : 0f;
        float stun = Mathf.Max(0f, info.hitStun);
        bool react = (distance > 0f || stun > 0f) && (!acting || broke);

        if (broke)
        {
            string what = string.IsNullOrEmpty(action.actionName) ? action.name : action.actionName;
            Debug.Log($"[Boss] 「{what}」被打断，套路作废");
            if (AI != null) AI.ClearIntent("被玩家打断");
        }

        if (react)
        {
            float dx = transform.position.x - info.sourcePosition.x;
            float dir = Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : (LastAttackFacing != 0f ? -LastAttackFacing : 1f);

            // 已经在受击硬直中也可以再被打（重新开始）
            KnockbackState.Setup(dir, distance, knockbackDuration, stun);
            ChangeState(KnockbackState);
        }
        else if (broke)
        {
            // CombatState.Exit 会停掉出招协程并通知执行器收摊
            ChangeState(MoveState);
        }
    }
}