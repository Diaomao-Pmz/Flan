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
    public BossCinematicState CinematicState { get; private set; }

    /// <summary>演出导演（没挂就为 null：转二阶段走原来的做法，招式卡的「放完后播放演出」不生效）</summary>
    public BossCinematicDirector Cinematics { get; private set; }

    /// <summary>正在播演出</summary>
    public bool InCinematic => Cinematics != null && Cinematics.IsPlaying;

    // 各专职部门的执行器引用
    public BAE_BulletEmitter BulletEmitter { get; private set; }
    public BAE_Teleporter Teleporter { get; private set; } // 【新增】传送专职执行器
    public BAE_Dasher Dasher { get; private set; }         // 冲刺（纯位移）执行器；没挂就为 null，冲刺卡不生效
    public BossState bossState { get; private set; }

    [Header("--- 配置表 ---")]
    [Tooltip("基础移速、撞墙检测、被击退时长。必填：留空会在 Console 报错，先用默认数值")]
    [SerializeField] private BossMovementConfig movementConfig;

    private BossMovementConfig activeMovement;

    /// <summary>生效中的移动配置。没拖 → 报错，用默认数值</summary>
    public BossMovementConfig Movement
    {
        get
        {
            if (activeMovement == null)
            {
                if (movementConfig != null) activeMovement = movementConfig;
                else
                {
                    activeMovement = ScriptableObject.CreateInstance<BossMovementConfig>();
                    activeMovement.name = "MovementConfig（未配置，默认数值）";
                    Debug.LogError("[BossController] 没有拖 Movement Config！先用默认数值（墙的图层为空，撞墙检测不起作用）。", this);
                }
            }
            return activeMovement;
        }
    }

    /// <summary>基础移速（战斗节奏里 Move Speed 填 -1 的姿态用它）</summary>
    public float BaseMoveSpeed => Movement.baseMoveSpeed;

    /// <summary>撞墙射线检测距离</summary>
    public float wallCheckDistance => Movement.wallCheckDistance;

    /// <summary>墙壁 / 死角的图层</summary>
    public LayerMask wallLayer => Movement.wallLayer;

    /// <summary>被击退时滑多久（秒）</summary>
    public float knockbackDuration => Movement.knockbackDuration;

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
        Dasher = GetComponent<BAE_Dasher>();
        Cinematics = GetComponent<BossCinematicDirector>();

        CombatState = new BossActionExecuter(this);
        MoveState = new BossMoveState(this);
        StunState = new BossStunState(this);   // 复用实例，不再每次破盾 new 一个
        KnockbackState = new BossKnockbackState(this);
        CinematicState = new BossCinematicState(this);

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

        // Cancel 只停当前这一步的弹幕；转到后台的弹幕在这里一起停
        if (BulletEmitter != null) BulletEmitter.StopAttack();
    }

    void Start()
    {
        // 统一初始化各部门
        if (BulletEmitter != null) BulletEmitter.Init(playerTransform);
        if (Teleporter != null) Teleporter.Init(playerTransform);
        if (Dasher != null) Dasher.Init(playerTransform);

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

            // 没护盾的 Boss（State Config 护盾上限 = 0）：开场就不显示护盾特效
            if (!bossState.bossMechanic.HasShield) HideShieldVisual();
        }

        // Boss 开始时先进入风筝移动状态
        ChangeState(MoveState);
    }

    /// <summary>动作倍率：当前阶段节奏表的 Action Speed（招式执行快慢）</summary>
    public float ActionSpeed => AI != null ? AI.ActionSpeed : 1f;

    /// <summary>Boss 时钟的累计时间（按动作倍率走）。招式里要记「到某个时间点」时用它代替 Time.time</summary>
    public float ActionTime { get; private set; }

    void Update()
    {
        ActionTime += Time.deltaTime * ActionSpeed;

        // 真动画：出招时按动作倍率播放，其余时候原速
        if (animator != null) animator.speed = (CurrentState == CombatState || CurrentState == CinematicState) ? ActionSpeed : 1f;

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

    /// <summary>
    /// 朝「离玩家 minDistance ～ maxDistance」这个范围走：太近就远离玩家，太远就靠近，在范围里就站住。
    /// 只改水平速度。移动状态走位、开火时保持距离都用它。
    /// </summary>
    /// <returns>水平方向：+1 = 往右，-1 = 往左，0 = 站住</returns>
    public float DriveTowardDistance(float minDistance, float maxDistance, float speed)
    {
        Vector2 playerPos = playerTransform.position;
        Vector2 bossPos = transform.position;
        float distance = Vector2.Distance(playerPos, bossPos);
        float dirX = 0f;

        if (distance < minDistance)
            dirX = (bossPos.x > playerPos.x) ? 1f : -1f;   // 太近：远离玩家
        else if (distance > maxDistance)
            dirX = (playerPos.x > bossPos.x) ? 1f : -1f;   // 太远：靠近玩家

        if (body != null || TryGetComponent(out body))
            body.linearVelocity = new Vector2(dirX * speed, body.linearVelocity.y);

        return dirX;
    }

    /// <summary>只把水平速度清零（竖直速度保留，比如还在下落）</summary>
    public void StopHorizontal()
    {
        if (body != null || TryGetComponent(out body))
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
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

        // 演出不被打断（只有演出卡没开 Boss 无敌时才会在演出中破盾）：不眩晕，演完直接按近战姿态继续
        if (InCinematic)
        {
            Debug.Log("[Boss] 演出中破盾：不进入眩晕，演完按近战姿态继续");
            return;
        }
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
        if (InCinematic) return; // 演出中不做受击传送
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
    /// 玩家那一招填了 Clash Boss Stagger：先记下来，这一招收尾时（CombatState.Finish）原地停顿再接下一步。
    /// </summary>
    public void OnMeleeClashed(ComboNode by)
    {
        string who = by != null ? by.nodeName : "玩家";
        pendingClashStagger = by != null ? Mathf.Max(0f, by.clashBossStagger) : 0f;
        Debug.Log(pendingClashStagger > 0f
            ? $"[Boss] 被 {who} 拼刀：只结束这一招，停顿 {pendingClashStagger:F2} 秒后套路继续"
            : $"[Boss] 被 {who} 拼刀：只结束这一招，套路继续");
    }

    // 被拼刀后要停顿多久（这一招收尾时取走）
    private float pendingClashStagger;

    /// <summary>取走被拼刀留下的停顿时长（取完清零）。有停顿返回 true</summary>
    public bool TryConsumeClashStagger(out float seconds)
    {
        seconds = pendingClashStagger;
        pendingClashStagger = 0f;
        return seconds > 0f;
    }

    /// <summary>被拼刀后的停顿：原地不动、不出招，时间到了回移动状态接着套路（借用受击硬直状态，不滑行）</summary>
    public void EnterClashStagger(float seconds)
    {
        KnockbackState.Setup(1f, 0f, 0f, seconds);
        ChangeState(KnockbackState);
    }

    private void HideShieldVisual() { if (shieldVisual != null) shieldVisual.SetActive(false); }
    private void ShowShieldVisual() { if (shieldVisual != null) shieldVisual.SetActive(true); }

    private void HandlePhase2()
    {
        Debug.Log("[BossController] 触发二阶段！");

        // 正在播别的演出（例如招式放完后的演出）：先中止，转阶段优先
        if (InCinematic) Cinematics.Abort("转二阶段");

        // 【改动】不再只停弹幕，通知所有执行器收摊
        CancelAllExecutors();

        if (TryGetComponent(out Rigidbody2D rb)) rb.linearVelocity = Vector2.zero;

        if (AI != null) AI.SwitchToPhase2();

        // 二阶段转场是一次强制重置：立刻满盾，并把状态机从破防中拽出来。
        // 不做这两件事的话，CurrentState 会一直停在 BossStunState 上数完剩余的破防时间。
        bossState.bossMechanic.RecoverShield();

        // 配了转阶段演出：交给演出导演（传送到中央等都在演出卡里），演完自动回移动状态
        if (Cinematics != null && Cinematics.Phase2Cinematic != null)
        {
            Cinematics.Play(Cinematics.Phase2Cinematic);
            return;
        }

        // 没配演出：原来的做法 —— 瞬间传送到中央，马上开始二阶段
        if (Teleporter != null) Teleporter.ExecuteTeleport(TeleportTargetType.Center);
        ChangeState(MoveState);
    }

    protected override void Die()
    {
        Debug.Log("Boss被击败了！触发死亡演出！");
        if (InCinematic) Cinematics.Abort("Boss 死亡");
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
        // 演出期间无敌（演出卡上开了 Boss Invulnerable）：这一下完全不算
        if (Cinematics != null && Cinematics.BossInvulnerable) return;

        if (bossState != null)
        {
            // 将伤害请求转发给黑板 (BossState) 处理
            bossState.bossMechanic.TakeDamage(info.amount, info.type, info.shieldDamage);
            if (bossState.health.isDead) return;
        }

        // 演出不被打断：扣血扣盾照常，但不打断、不击退、不推
        if (InCinematic) return;

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