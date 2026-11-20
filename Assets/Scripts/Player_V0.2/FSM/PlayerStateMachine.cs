using Flandre.CombatSystem;
using UnityEngine;

/// <summary>
/// 玩家状态机 —— 现在只做三件事：持有卡带、切换卡带、驱动卡带。
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

    /// <summary>飞行中出招（边飞边打）。与 flyState 共用 flightMotor</summary>
    public FlyCombatState flyCombatState;

    /// <summary>
    /// 【飞行引擎】FlyState 与 FlyCombatState 共用：关重力、八方向移动、按秒扣蓝。
    /// 放在状态机上而不是某张卡带里，是为了两种飞行模式来回切换时"发动机不熄火"。
    /// </summary>
    public FlightMotor flightMotor { get; private set; }

    /// <summary>冲刺 / 滑铲穿过敌人的开关（每个物理帧由本类驱动）</summary>
    public PassThroughController passThrough { get; private set; }

    /// <summary>当前是否在飞（巡航或交战都算）</summary>
    public bool IsFlying => currentState != null
                            && (currentState == flyState || currentState == flyCombatState);

    public ComboState comboState;

    public HitState hitState;

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

    // ==========================================================
    // 冲刺 / 滑铲参数：配置里填「距离 + 时长」，速度反推（见 PlayerMovementConfig）。
    // 飞行中是否另用一套参数由 MovementConfig.flightUsesNormalActionParams 决定（默认共用）。
    // 下面这些属性已经把"现在该用哪一套"算好了，调用方直接读就行。
    // ==========================================================

    /// <summary>当前是否使用飞行专用的冲刺 / Ctrl 参数（飞行中，且配置里取消了"与普通一致"）</summary>
    private bool UseFlightActionParams
        => IsFlying && MoveCfg != null && !MoveCfg.flightUsesNormalActionParams;

    /// <summary>
    /// 冲刺速度加成的倍率：PlayerStats.dashSpeed 的「当前值 / 基础值」。没有宝石 / buff 时是 1。
    /// 只取比例、不直接用数值，这样在 Inspector 里改距离 / 时长能立刻生效。
    /// </summary>
    private float DashSpeedScale
    {
        get
        {
            var stat = playerState.stats.dashSpeed;
            return stat.BaseValue > 0.01f ? stat.Value / stat.BaseValue : 1f;
        }
    }

    /// <summary>冲刺速度 = 距离 / 时长 × 加成倍率。飞行参数分离时用飞行那一套距离 / 时长</summary>
    public float dashSpeed
        => (UseFlightActionParams ? MoveCfg.FlyDashBaseSpeed : MoveCfg.DashBaseSpeed) * DashSpeedScale;

    /// <summary>冲刺距离</summary>
    public float dashDistance
        => UseFlightActionParams ? MoveCfg.flyDashDistance : MoveCfg.dashDistance;

    /// <summary>冲刺冷却的基础值（未套冷却缩减）</summary>
    public float dashCooldown
        => UseFlightActionParams ? MoveCfg.flyDashCooldown : MoveCfg.dashCooldown;

    /// <summary>滑铲（飞行中是 Ctrl 不可选中）冷却的基础值</summary>
    public float slideCooldown
        => UseFlightActionParams ? MoveCfg.flySlideCooldown : MoveCfg.slideCooldown;

    public float minJumpVelocity => MoveCfg.minJumpVelocity;
    /// <summary>冲刺实际用时 = 配置的时长 / 加成倍率（距离不变，速度越快用时越短）</summary>
    public float dashDuration => dashDistance / Mathf.Max(0.01f, dashSpeed);

    /// <summary>滑铲时长（配置值）</summary>
    public float slideDuration => Mathf.Max(0.01f, MoveCfg.slideDuration);

    /// <summary>滑铲结束、转入蹲下时的速度</summary>
    private float SlideEndSpeed => moveSpeed * MoveCfg.slideToCrouchSpeedMultiplier;

    /// <summary>
    /// 滑铲初速度，由距离和时长反推：从 v0 匀减速到转蹲速度 vf，用时 T 滑过 d → v0 = 2d / T − vf。
    /// 「d / T」比 vf 还慢时没法减速，改成全程匀速 d / T。
    /// </summary>
    public float slideStartSpeed
    {
        get
        {
            float average = Mathf.Max(0.01f, MoveCfg.slideDistance) / slideDuration;
            float vf = SlideEndSpeed;
            return average <= vf ? average : 2f * average - vf;
        }
    }

    /// <summary>滑铲减速度 = (初速 − 转蹲速度) / 时长。匀速滑铲时为 0</summary>
    public float slideDeceleration
        => Mathf.Max(0f, slideStartSpeed - SlideEndSpeed) / slideDuration;

    public float crouchSpeedMultiplier => MoveCfg.slideToCrouchSpeedMultiplier;
    public float crouchMoveSpeedMultiplier => MoveCfg.crouchMoveSpeedMultiplier;
    public float flyManaCostPerSecond => playerState.stats.flyManaCostPerSecond.Value;   // 基础值来自 PlayerStateConfig，可被成长加成
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

    /// <summary>上一个状态。用于「从哪来」这类判断</summary>
    public IState previousState { get; private set; }

    /// <summary>
    /// 【重力的唯一真相源】角色本来的 gravityScale，Awake 时记一次，全程只读。
    /// </summary>
    public float defaultGravityScale { get; private set; } = 1f;

    /// <summary>蓄力系统。P3 起蓄力由它接管，不再是状态</summary>
    public PlayerChargeSystem chargeSystem { get; private set; }

    /// <summary>
    /// 动画调度器 —— 全项目唯一允许调用 Animator.Play 的地方。
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

            // 冷却改由 PlayerMovementConfig 提供（Dash / Slide Cooldown），覆盖 ComboSkill 上的 Total CD
            if (playerState.movementConfig != null)
            {
                dashSkill.BindCooldownSource(() => dashCooldown);
                slideSkill.BindCooldownSource(() => slideCooldown);
            }
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
        flyCombatState = new FlyCombatState(this);
        flightMotor = new FlightMotor(this);
        passThrough = new PassThroughController(rb, GetComponent<BoxCollider2D>());

        comboState = new ComboState(this);
        hitState = new HitState(this);

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
        // 先决定这一帧要不要穿过敌人，再让状态推动刚体
        if (passThrough != null)
        {
            LayerMask layers = MoveCfg != null ? MoveCfg.passThroughLayers : (LayerMask)0;
            passThrough.FixedTick(WantsPassThrough(), layers, Time.fixedDeltaTime);
        }

        // 接口已就位，下一批开始把速度写入迁移进来
        currentState?.FixedUpdate();
    }

    /// <summary>
    /// 此刻是否在做"可以穿过敌人"的动作：地面/空中冲刺、飞行八向冲刺、地面滑铲。
    /// 与无敌（无法选中）无关。
    /// </summary>
    private bool WantsPassThrough()
    {
        PlayerMovementConfig cfg = MoveCfg;
        if (cfg == null) return false;

        if (cfg.dashPassThrough)
        {
            if (currentState == dashState && dashState.IsDashing) return true;
            if (IsFlying && flightMotor != null && flightMotor.IsDashing) return true;
        }

        if (cfg.slidePassThrough && currentState == slideState && slideState.IsSliding) return true;

        return false;
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
    /// </summary>
    public void OnAttackAnimationEnd()
    {
        if (currentState != comboState) return;

        inputBuffer.StartGracePeriod();

        // 【实验】飞行中放出来的蓄力招：动画播完还没回飞行（这一招没有后摇段）→ 现在回
        if (comboState.TryResumeFlight(force: true)) return;

        // ==========================================================
        // 【必须先查地面】空中招式演完不能直接回 Idle/Run。
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