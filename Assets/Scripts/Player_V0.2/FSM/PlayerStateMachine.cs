using Flandre.CombatSystem;
using UnityEngine;

/// <summary>
/// 玩家状态机 —— 现在只做三件事：持有卡带、切换卡带、驱动卡带。
///
/// ==========================================================
/// 【批次D 改动】上帝对象终于瘦下来了
///
/// 搬出去的东西：
///   IsGrounded / CanStand / SetColliderHeight  → PlayerSensor
///   groundCheck / ceilingCheck / hurtboxCore / groundLayer / 碰撞体备份 → PlayerSensor
///   SpawnProjectile / projectilePrefab / firePoint → PlayerWeaponEmitter
///   Jump 与 Fall 重复的悬停蓄力逻辑 → HoverChargeHandler
///
/// 演进过程：452 行（初始） → 约 300 行（批次A/B/C） → 现在约 230 行
///
/// 剩下的仍占篇幅的是那一堆「转发窗口」（moveSpeed / jumpForce / …）。
/// 它们是当初为了不动 11 张卡带而搭的转寄条，现在可以逐步拆掉了，
/// 不过那属于纯机械替换，收益低、风险也低，不急。
///
/// 【为了不破坏现有调用，本文件保留三个转发方法】
///   IsGrounded() / CanStand() / SetColliderHeight() → 转发给 PlayerSensor
/// 这样 11 张卡带里的 sm.IsGrounded() 一行都不用改。
/// ==========================================================
/// </summary>
public class PlayerStateMachine : MonoBehaviour
{
    public IState currentState;

    public IdleState idleState;
    public RunState runState;
    public JumpState jumpState;
    public FallState fallState;
    public DashState dashState;
    public CrouchState crouchState;
    public SlideState slideState;
    public FlyState flyState;

    public ComboState comboState;

    // 【已删除】chargeState —— P3 把蓄力从「状态」解耦成了「随身模块」，
    // 由 PlayerChargeSystem 接管，状态机从此再没切进去过。
    // 一个永远不会被 Enter 的状态卡带留在这里，只会让人以为蓄力还走状态机那条路。

    public HitState hitState;

    /// <summary>
    /// 强行打出弱化蓄力后的僵直。禁止一切输入，受击可打断（自然由 HandlePlayerHit 接管）。
    /// </summary>
    public ChargeStunState chargeStunState;

    // TODO: 阶段性接入 —— 当前按要求维持「0血不死」，故不实例化
    // public DeadState deadState;

    [Header("组件引用 (自动获取)")]
    public Animator anim;
    public Rigidbody2D rb;
    public TrailRenderer dashTrail;
    public PlayerController playerController;
    public PlayerState playerState;
    public LoadoutManager loadout;
    public PlayerCommandRouter commandRouter;
    public PlayerSensor sensor;

    // ==========================================================
    // 转发窗口
    // ==========================================================
    private PlayerMovementConfig MoveCfg => playerState != null ? playerState.movementConfig : null;
    private PlayerCombatConfig CombatCfg => playerState != null ? playerState.combatConfig : null;

    public float moveSpeed => playerState.stats.moveSpeed.Value;
    public int maxJumps => playerState.stats.maxJumps.IntValue;
    public float jumpForce => playerState.stats.jumpForce.Value;
    public float dashSpeed => playerState.stats.dashSpeed.Value;

    public float minJumpVelocity => MoveCfg.minJumpVelocity;
    public float dashDuration => MoveCfg.dashDuration;
    public float slideStartSpeedMultiplier => MoveCfg.slideStartSpeedMultiplier;
    public float slideDeceleration => MoveCfg.slideDeceleration;
    public float crouchSpeedMultiplier => MoveCfg.slideToCrouchSpeedMultiplier;
    public float crouchMoveSpeedMultiplier => MoveCfg.crouchMoveSpeedMultiplier;
    public float hoverChargeTime => MoveCfg.hoverChargeTime;
    public float flyManaCostPerSecond => MoveCfg.flyManaCostPerSecond;
    public float flyCancelJumpForce => MoveCfg.flyCancelJumpForce;
    public float flySpeed => MoveCfg.flySpeed;

    public float hitStunDuration => CombatCfg.hitStunDuration;
    public Vector2 hitKnockbackForce => CombatCfg.hitKnockbackForce;
    public float blinkInterval => CombatCfg.blinkInterval;

    [Header("Jump Runtime")]
    public int jumpCount = 0;

    [Header("Dash / Slide 充能配置")]
    public ComboSkill dashSkill = new ComboSkill();
    public ComboSkill slideSkill = new ComboSkill();

    /// <summary>本次动作是否由「受身打断」触发。由路由器置位，问完宝石立刻清零</summary>
    [HideInInspector] public bool isBreakingHitStun = false;

    /// <summary>
    /// 【携带动量】滑铲的冲劲，可以跨状态传递给连招与蓄力。
    /// 只有滑铲/连招/蓄力会保留它，进入其他状态时由 ChangeState 统一清空。
    /// </summary>
    public readonly SlideMomentum slideMomentum = new SlideMomentum();

    /// <summary>上一个状态。用于「从哪来」这类判断</summary>
    public IState previousState { get; private set; }

    /// <summary>
    /// 【重力的唯一真相源】角色本来的 gravityScale，Awake 时记一次，全程只读。
    ///
    /// 【为什么必须有它】原先有 7 个地方写 originalGravity = sm.rb.gravityScale ——
    /// 都是「进来时是多少，出去时还回多少」。这在重力正常时没问题，
    /// 但只要某个状态在【重力已经被别人置 0】的时候进入，它就会把 0 当成原值存下来，
    /// 退出时把 0 还回去 —— 重力从此永久消失，角色飘在空中。
    ///
    /// 空中蓄力把角色钉住（gravityScale = 0）之后按 Shift 冲刺，踩的就是这条：
    /// DashState.Enter 存下 0 → Exit 还回 0 → 落不下来了。
    ///
    /// 比喻：每个人都「把桌子恢复成我来之前的样子」，
    ///       但前一个人已经把桌子掀翻了 —— 于是掀翻状态被一路传下去。
    ///       现在改成所有人都照同一张出厂照片复原。
    /// </summary>
    public float defaultGravityScale { get; private set; } = 1f;

    /// <summary>蓄力系统。P3 起蓄力由它接管，不再是状态</summary>
    public PlayerChargeSystem chargeSystem { get; private set; }

    /// <summary>
    /// 动画调度器 —— 全项目唯一允许调用 Animator.Play 的地方。
    ///
    /// 状态卡带不再直接播动画，而是通过它【声明意图】：
    ///     sm.animDriver.SetBase(PlayerAnimHash.Run);
    /// 本帧最终播什么由调度器结算（比如蓄力时可能被蓄力姿势覆盖）。
    /// </summary>
    public PlayerAnimationDriver animDriver { get; private set; }

    private ComboInputBuffer cachedInputBuffer;
    public ComboInputBuffer inputBuffer
    {
        get
        {
            if (cachedInputBuffer == null) cachedInputBuffer = GetComponent<ComboInputBuffer>();
            return cachedInputBuffer;
        }
    }

    // ---- 延迟切换机制 ----
    private bool isTransitioning = false;
    private IState pendingState = null;

    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerState = GetComponent<PlayerState>();
        loadout = GetComponent<LoadoutManager>();
        commandRouter = GetComponent<PlayerCommandRouter>();
        sensor = GetComponent<PlayerSensor>();
        anim = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();

        // 趁还没有任何状态跑过，把出厂重力拍下来
        if (rb != null) defaultGravityScale = rb.gravityScale;

        cachedInputBuffer = GetComponent<ComboInputBuffer>();
        chargeSystem = GetComponent<PlayerChargeSystem>();
        animDriver = GetComponent<PlayerAnimationDriver>();

        if (animDriver == null)
            Debug.LogError("[PlayerStateMachine] 缺少 PlayerAnimationDriver，所有动画都不会播放！", this);

        if (playerState != null)
        {
            playerState.EnsureInitialized();
            dashSkill.BindStats(playerState.stats);
            slideSkill.BindStats(playerState.stats);
        }
        else Debug.LogError("[PlayerStateMachine] 找不到 PlayerState！", this);

        if (sensor == null) Debug.LogError("[PlayerSensor] 缺失！地面检测将永远返回 false。", this);
        if (loadout == null) Debug.LogError("[LoadoutManager] 缺失！宝石系统不会生效。", this);
        if (commandRouter == null) Debug.LogError("[PlayerCommandRouter] 缺失！跳/冲/铲都不会响应。", this);

        idleState = new IdleState(this);
        runState = new RunState(this);
        jumpState = new JumpState(this);
        fallState = new FallState(this);
        dashState = new DashState(this);
        crouchState = new CrouchState(this);
        slideState = new SlideState(this);
        flyState = new FlyState(this);

        comboState = new ComboState(this);
        hitState = new HitState(this);
        chargeStunState = new ChargeStunState(this);

        // TODO: 接入死亡时解除注释
        // deadState = new DeadState(this);

        if (dashTrail == null) dashTrail = GetComponentInChildren<TrailRenderer>();
    }

    void Start()
    {
        ChangeState(idleState);

        if (playerState != null && playerState.health != null)
        {
            playerState.health.OnPlayerHit -= HandlePlayerHit;
            playerState.health.OnPlayerHit += HandlePlayerHit;

            // TODO: 接入死亡时解除注释
            // playerState.health.OnPlayerDeath -= HandlePlayerDeath;
            // playerState.health.OnPlayerDeath += HandlePlayerDeath;
        }
    }

    void Update()
    {
        if (IsGrounded() && currentState != dashState && currentState != jumpState)
        {
            jumpCount = 0;
        }

        if (currentState != dashState) dashSkill.UpdateTimeout();
        if (currentState != slideState) slideSkill.UpdateTimeout();

        currentState?.Update();
    }

    void FixedUpdate()
    {
        // 接口已就位，下一批开始把速度写入迁移进来
        currentState?.FixedUpdate();
    }

    /// <summary>
    /// 切换状态。
    /// 在 Enter() 内部被再次调用时，新请求会排队，
    /// 等当前 Enter() 完整跑完后才执行 —— Exit() 永远不会在 Enter() 中途被触发。
    /// </summary>
    public void ChangeState(IState newState)
    {
        if (newState == null) return;

        if (isTransitioning)
        {
            pendingState = newState;
            return;
        }

        isTransitioning = true;
        previousState = currentState;
        currentState?.Exit();
        currentState = newState;
        ClearMomentumIfNeeded();
        currentState.Enter();
        isTransitioning = false;

        int guard = 0;
        while (pendingState != null)
        {
            IState next = pendingState;
            pendingState = null;

            isTransitioning = true;
            previousState = currentState;
            currentState.Exit();
            currentState = next;
            ClearMomentumIfNeeded();
            currentState.Enter();
            isTransitioning = false;

            if (++guard > 8)
            {
                Debug.LogError("[状态机] 连续切换超过 8 次，疑似状态循环，已强制中断。", this);
                pendingState = null;
                break;
            }
        }
    }

    /// <summary>
    /// 【携带动量的唯一清除点】
    ///
    /// 只有滑铲/连招/蓄力三个状态会保留动量，
    /// 进入其他任何状态（跳跃、冲刺、受击、待机…）都自动清空。
    ///
    /// 集中在这里做，是为了不需要在每张卡带里各写一遍 —— 也就不可能漏。
    /// 以后新增状态时默认就是"清空"，这是更安全的默认值。
    /// </summary>
    private void ClearMomentumIfNeeded()
    {
        // 滑铲 / 连招期间保留携带动量。
        // 蓄力不再是状态，所以改问蓄力系统 —— 蓄力中同样保留冲劲。
        bool keeps = currentState == slideState
                  || currentState == comboState
                  || (chargeSystem != null && chargeSystem.IsAnyCharging);

        if (!keeps) slideMomentum.Clear();
    }

    // ==========================================
    // 感官转发 (实现在 PlayerSensor，此处只转发，避免改动 11 张卡带)
    // ==========================================

    public bool IsGrounded() => sensor != null && sensor.IsGrounded();
    public bool CanStand() => sensor == null || sensor.CanStand();
    public void SetColliderHeight(bool isCrouching) => sensor?.SetColliderHeight(isCrouching);

    // ==========================================
    // 攻击 —— 动画事件入口
    // ==========================================

    public void OpenComboWindow()
    {
        if (currentState == comboState)
        {
            comboState.isCancelable = true;
            inputBuffer.TryAdvanceCombo();
        }
    }

    public void CloseComboWindow()
    {
        if (currentState == comboState) comboState.isCancelable = false;
    }

    /// <summary>
    /// 由动画事件调用：攻击动画彻底播完。
    ///
    /// 【P1a 改动】删掉了"还按着攻击键就进蓄力"这条路。
    ///
    /// 旧行为是效仿空洞骑士：按下先出一发普攻，动画放完还按着才转蓄力。
    /// 新规则把那一发普攻废除了 —— 蓄力改由 PlayerController 的
    /// 0.15 秒长按判定【直接】进入，不再经过普攻。
    ///
    /// 两条路留着任何一条都会出事：长按会先出普攻再进蓄力，
    /// 正是我们要废除的旧行为。
    /// </summary>
    public void OnAttackAnimationEnd()
    {
        if (currentState != comboState) return;

        inputBuffer.StartGracePeriod();

        // 强行打出的弱化蓄力 → 演完直接进僵直，而不是回到待机/跑动。
        // 判断放在这里（而不是 ComboState.Exit）是有意的：Exit 会在【所有】
        // 离开攻击的路径上跑，包括被冲刺主动取消 —— 那时状态机正在切去 DashState，
        // 从 Exit 里再发起一次切换会把冲刺顶掉。
        // 只有"自然演完"才该吃这个僵直。
        if (comboState.TryGetPendingStun(out float stunSeconds))
        {
            chargeStunState.SetDuration(stunSeconds);
            ChangeState(chargeStunState);
            return;
        }

        // ==========================================================
        // 【必须先查地面】空中招式演完不能直接回 Idle/Run。
        //
        // 原先这里只看方向键：在空中打完一招、手上还按着方向键 →
        // 切进 RunState → 而 RunState.Enter 里有一句 jumpCount = 0
        // （注释写的是"踩地跑动，刷新跳跃次数"）→ 二段跳在半空被还回来了。
        //
        // 【为什么一直没被发现】RunState.Update 第一件事就是
        // "不在地面就转 FallState"，所以跑动动画只闪一帧，肉眼看不见。
        // 但 Enter 已经执行过了，jumpCount 已经被清掉。
        // 装上 Echo 有了二段跳之后，这个一直存在的 bug 才浮出水面。
        //
        // 【同一件事本来有两条出口，另一条是对的】
        // ComboState.WarnAndExitOnTimeout（动画事件漏配时的兜底超时）写的是
        // "不在地面就转 FallState，否则 HandleLanding" —— 查了地面。
        // 两条路待遇不一致，这和文档 6.8 那条（一条失败路径硬 return、
        // 另一条走回退）是完全同一个形状的毛病。现在对齐。
        // ==========================================================
        if (!IsGrounded())
        {
            ChangeState(fallState);
            return;
        }

        if (Mathf.Abs(playerController.moveInput.x) > 0.1f) ChangeState(runState);
        else ChangeState(idleState);
    }

    // ==========================================
    // 击飞
    // ==========================================

    private void HandlePlayerHit(Vector2 knockbackDirection)
    {
        // 受击时清空预输入缓存，否则硬直结束后挨打前按的键会突然全部兑现
        commandRouter?.ClearBuffer();

        // 后摇也一并清掉：挨打已经是惩罚了，不该出来之后还被自己的后摇卡住
        inputBuffer?.ClearHandRecovery();

        // 【P3】蓄力被打断 —— 后果与未蓄满松手相同：什么都不放，连段清零
        chargeSystem?.CancelAll();

        // 弱化招演出期间预付、但还没兑现的逃逸冲劲也要丢掉。
        //
        // 挨打意味着这次僵直根本不会发生了。不丢的话那股冲劲会一直挂着，
        // 等【下一次】弱蓄进僵直时白送出去 —— 表现成"我什么都没按，
        // 人却自己滑出去了"，而且要恰好挨过一次打才复现。
        chargeStunState?.CancelPendingEscape();

        hitState.SetKnockbackForce(knockbackDirection);
        ChangeState(hitState);
        playerController.StartBlink(playerState.health.invulnerableDuration, blinkInterval);
    }

    // TODO: 接入死亡时解除注释
    // private void HandlePlayerDeath() => ChangeState(deadState);

    void OnDestroy()
    {
        if (playerState != null && playerState.health != null)
        {
            playerState.health.OnPlayerHit -= HandlePlayerHit;
            // playerState.health.OnPlayerDeath -= HandlePlayerDeath;
        }
    }
}