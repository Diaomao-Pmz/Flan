using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Flandre.CombatSystem;

[RequireComponent(typeof(BossState))]
[RequireComponent(typeof(BAE_Teleporter))] // ¡¾ĞÂÔö¡¿Ç¿ÖÆÒªÇó¹ÒÔØ´«ËÍÆ÷×é¼ş
public class BossController : EntityBase
{
    [Header("--- ±íÏÖ²ãÒıÓÃ ---")]
    public GameObject shieldVisual;
    public Animator animator { get; private set; }
    [SerializeField] Transform playerTransform;

    public BossAIDecider AI { get; private set; }

    // ×´Ì¬¶¨Òå
    public BossActionExecuter CombatState { get; private set; }
    public BossMoveState MoveState { get; private set; }
    public BossStunState StunState { get; private set; }

    // ¸÷×¨Ö°²¿ÃÅµÄÖ´ĞĞÆ÷ÒıÓÃ
    public BAE_BulletEmitter BulletEmitter { get; private set; }
    public BAE_Teleporter Teleporter { get; private set; } // ¡¾ĞÂÔö¡¿´«ËÍ×¨Ö°Ö´ĞĞÆ÷
    public BossState bossState { get; private set; }

<<<<<<< Updated upstream
    [Header("--- ÒÆ¶¯·çóİÏµÍ³²ÎÊı ---")]
    // ×¢Òâ£ºmaintainDistance ÒÑ±»³¹µ×É¾³ı£¬ÓÉ MoveState ¶¯Ì¬Ïò AI Ë÷È¡
    public float moveSpeed = 3f;              // ÒÆ¶¯ËÙ¶È
    public float wallCheckDistance = 1.5f;    // ×²Ç½ÉäÏß¼ì²â¾àÀë
    public LayerMask wallLayer;               // Ç½±Ú/ËÀ½ÇµÄÍ¼²ã
=======
    [Header("--- ç§»åŠ¨é£ç­ç³»ç»Ÿå‚æ•° ---")]
    // æ³¨æ„ï¼šmaintainDistance å·²è¢«å½»åº•åˆ é™¤ï¼Œç”± MoveState åŠ¨æ€å‘ AI ç´¢å–
    // ç§»åŠ¨é€Ÿåº¦å·²æ¬åˆ° BossAIDecider çš„ Ranged / Melee Tempoï¼ˆæ¯ä¸ªå§¿æ€ä¸€å¥—ï¼Œå¦æœ‰äºŒé˜¶æ®µå€ç‡ï¼‰
    public float wallCheckDistance = 1.5f;    // æ’å¢™å°„çº¿æ£€æµ‹è·ç¦»
    public LayerMask wallLayer;               // å¢™å£/æ­»è§’çš„å›¾å±‚
>>>>>>> Stashed changes

    [Header("UI For Testing")]
    public TextMeshProUGUI bossStatusText;

    [Tooltip("æ˜¾ç¤º Boss å½“å‰çš„ç§»åŠ¨æ„å›¾ï¼ˆé è¿‘ / åé€€ / åˆ°ä½ / ä¼ é€ä¸­ / å‡ºæ‹›ä¸­ï¼‰")]
    public TextMeshProUGUI movementStatusText;

    private string shownMovementStatus;

    /// <summary>åˆ·æ–°ç§»åŠ¨æ„å›¾æ–‡å­—ã€‚å†…å®¹æ²¡å˜å°±ä¸é‡å†™ï¼Œé¿å…æ¯å¸§é‡å»ºæ–‡å­—ç½‘æ ¼</summary>
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
    /// Ö»¶ÁÇé±¨×ÜÏß¡£ËùÓĞÖ´ĞĞÆ÷ÓëÌõ¼ş¶ÔÏó¹²ÓÃÍ¬Ò»·İ£¬±ÜÃâ¸÷×Ô FindObjectOfType¡£
    /// ÔÚ Start ÖĞ×é×°£¬È·±£¸÷×é¼şµÄ Awake ¶¼ÒÑÅÜÍê¡£
    /// </summary>
    public BossContext Context { get; private set; }

    // ¡¾ĞÂÔö¡¿Node ÀàĞÍ ¡ú Ö´ĞĞÆ÷ µÄÓ³Éä±í¡£¼ÓĞÂ¼¼ÄÜ²»ÔÙĞèÒª¸ÄÈÎºÎ·ÖÅÉ´úÂë¡£
    private readonly Dictionary<Type, IBossActionExecutor> executors
        = new Dictionary<Type, IBossActionExecutor>();

    protected override void Awake()
    {
        base.Awake();

        // Í³Ò»»ñÈ¡¸÷×é¼şÒıÓÃ
        bossState = GetComponent<BossState>();
        animator = GetComponent<Animator>();
        AI = GetComponent<BossAIDecider>();
        BulletEmitter = GetComponent<BAE_BulletEmitter>();
        Teleporter = GetComponent<BAE_Teleporter>(); // »ñÈ¡´«ËÍÆ÷

        CombatState = new BossActionExecuter(this);
        MoveState = new BossMoveState(this);
        StunState = new BossStunState(this);   // ¸´ÓÃÊµÀı£¬²»ÔÙÃ¿´ÎÆÆ¶Ü new Ò»¸ö

        BuildExecutorRegistry();
    }

    /// <summary>
    /// É¨Ãè¹ÒÔÚ×Ô¼ºÉíÉÏµÄËùÓĞÖ´ĞĞÆ÷£¬½¨Á¢¡¸ÈÏÁì¹ØÏµ¡¹¡£
    /// Ö´ĞĞÆ÷Ö»Òª¹ÒÉÏÀ´¾Í»á±»×Ô¶¯·¢ÏÖ£¬²»ĞèÒªÔÚÕâÀïÖğ¸öµÇ¼Ç¡£
    /// </summary>
    private void BuildExecutorRegistry()
    {
        executors.Clear();

        foreach (IBossActionExecutor executor in GetComponents<IBossActionExecutor>())
        {
            Type nodeType = executor.NodeType;

            if (nodeType == null)
            {
                Debug.LogError($"[BossController] {executor.GetType().Name} µÄ NodeType Îª¿Õ£¬ÒÑÌø¹ı¡£", this);
                continue;
            }

            if (executors.ContainsKey(nodeType))
            {
                Debug.LogError(
                    $"[BossController] {nodeType.Name} ±»ÖØ¸´ÈÏÁì£º" +
                    $"{executors[nodeType].GetType().Name} Óë {executor.GetType().Name}¡£ºóÕßÒÑºöÂÔ¡£", this);
                continue;
            }

            executors[nodeType] = executor;
        }
    }

    /// <summary>°´¿¨Æ¬µÄÔËĞĞÊ±ÀàĞÍÕÒµ½¶ÔÓ¦Ö´ĞĞÆ÷¡£ÕÒ²»µ½·µ»Ø null¡£</summary>
    public IBossActionExecutor GetExecutorFor(ActionNode node)
    {
        if (node == null) return null;
        return executors.TryGetValue(node.GetType(), out IBossActionExecutor executor) ? executor : null;
    }

    /// <summary>Í¨ÖªËùÓĞÖ´ĞĞÆ÷Á¢¼´ÊÕÌ¯¡£ÆÆ¶Ü¡¢×ª½×¶Î¡¢ËÀÍöÊ±Í³Ò»µ÷ÓÃ¡£</summary>
    public void CancelAllExecutors()
    {
        foreach (IBossActionExecutor executor in executors.Values)
        {
            executor.Cancel();
        }
    }

    void Start()
    {
        // Í³Ò»³õÊ¼»¯¸÷²¿ÃÅ
        if (BulletEmitter != null) BulletEmitter.Init(playerTransform);
        if (Teleporter != null) Teleporter.Init(playerTransform);

        // ×é×°Ö»¶ÁÇé±¨×ÜÏß
        Context = new BossContext(this, playerTransform);

        // Í³Ò»¶©ÔÄºÚ°åÊÂ¼ş
        if (bossState != null)
        {
            bossState.bossMechanic.OnShieldBroken += HandleShieldBroken;
            bossState.bossMechanic.OnShieldBroken += HideShieldVisual;
            bossState.bossMechanic.OnShieldRecovered += ShowShieldVisual;
            bossState.bossMechanic.OnShieldRecovered += HandleShieldRecovered;

<<<<<<< Updated upstream
            // ¡¾¹Ø¼üĞŞ¸Ä¡¿½«±»¶¯·À·´´«ËÍÊÂ¼ş£¬Ö±½ÓÎ¯ÍĞ¸ø´«ËÍÆ÷Ö´ĞĞËæ»ú´«ËÍ²ßÂÔ
            bossState.bossMechanic.OnTeleportTriggered += TriggerPassiveTeleport;
=======
            // æŒ¨æ‰“ â†’ äº¤ç»™ä¼ é€å™¨åˆ¤æ–­è¦ä¸è¦å—å‡»ä¼ é€ï¼ˆæ¦‚ç‡ã€å†·å´ã€å§¿æ€éƒ½ç”± TeleportNode é…ç½®ï¼‰
            bossState.bossMechanic.OnHitTaken += HandleHitTaken;
>>>>>>> Stashed changes
            bossState.bossMechanic.OnPhase2Triggered += HandlePhase2;
            bossState.health.OnDeath += Die;
        }

        // Boss ¿ªÊ¼Ê±ÏÈ½øÈë·çóİÒÆ¶¯×´Ì¬
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
        // å§¿æ€åˆ‡åˆ°è¿‘æˆ˜ï¼šè¿œç¨‹å§¿æ€ä¸‹é¢„çº¦çš„æ„å›¾ï¼ˆæ¯”å¦‚æƒ³æ‹‰è¿œæ”¾å¼¹å¹•ï¼‰ä½œåºŸ
        if (AI != null) AI.OnCombatModeChanged();
        ChangeState(StunState);
    }

<<<<<<< Updated upstream
    // --- ÒÔÏÂÎªÊÂ¼şÏìÓ¦µÄ·â×°·½·¨£¨ÎªÁË·½±ã OnDestroy Ê±¸É¾»µØ×¢Ïú£© ---
=======
    /// <summary>
    /// æŠ¤ç›¾æ¢å¤ â†’ å›åˆ°è¿œç¨‹ä¸ºä¸»é˜¶æ®µã€‚
    /// æ­£åœ¨æ”¾çš„é‚£ä¸€æ‹›æ”¾å®Œå†è¯´ï¼Œä¸å¼ºè¡Œæ‰“æ–­ï¼›ä¸‹ä¸€æ¬¡æŠ½å¡è‡ªç„¶æŒ‰è¿œç¨‹å§¿æ€æ¥ã€‚
    /// </summary>
    private void HandleShieldRecovered()
    {
        if (AI != null) AI.OnCombatModeChanged();
    }

    // --- ä»¥ä¸‹ä¸ºäº‹ä»¶å“åº”çš„å°è£…æ–¹æ³•ï¼ˆä¸ºäº†æ–¹ä¾¿ OnDestroy æ—¶å¹²å‡€åœ°æ³¨é”€ï¼‰ ---
>>>>>>> Stashed changes

    private void HandleHitTaken(DamageType type, BossCombatMode modeAtHit)
    {
        if (Teleporter != null) Teleporter.OnBossHit(type, modeAtHit);
    }

    /// <summary>
    /// ä¸Šä¸€æ¬¡è¿‘æˆ˜ / çªè¿›å‡ºæ‰‹çš„æœå‘ï¼š+1 = æœå³ï¼Œ-1 = æœå·¦ï¼Œ0 = è¿˜æ²¡å‡ºè¿‡æ‰‹ã€‚
    /// Boss æ²¡æœ‰å¸¸é©»æœå‘ï¼ˆæ¯æ‹›å‡ºæ‰‹æ—¶ä¸´æ—¶è½¬å‘ç©å®¶ï¼‰ï¼Œæ‰€ä»¥é è¿™ä¸ªè®°ä½"åˆšæ‰æ‰“çš„æ˜¯å“ªè¾¹"ï¼Œ
    /// ç”¨æ¥åˆ¤æ–­ç©å®¶æ˜¯ä¸æ˜¯ç»•åˆ°äº†èƒŒåï¼ˆå¥—è·¯å€™è¡¥æ‹›å¼çš„æ¡ä»¶ï¼‰ã€‚
    /// </summary>
    public float LastAttackFacing { get; private set; }

    /// <summary>æ‰§è¡Œå™¨å‡ºæ‰‹æ—¶ç™»è®°æœå‘</summary>
    public void RecordAttackFacing(float sign)
    {
        if (sign != 0f) LastAttackFacing = Mathf.Sign(sign);
    }

    /// <summary>å½“å‰æ˜¯å¦å¤„äºç ´ç›¾çœ©æ™•ä¸­</summary>
    public bool IsStunned => CurrentState == StunState;

    /// <summary>
    /// ç´§æ€¥æ”¶æ‹›ï¼šææ‰æ­£åœ¨æ”¾çš„æ‹›ã€ä½œåºŸå¥—è·¯ï¼Œå›åˆ°ç§»åŠ¨çŠ¶æ€ã€‚
    /// ç»™"å‹¾äº† Interrupt Action çš„å—å‡»ä¼ é€"è¿™ç±»å¤–éƒ¨æ‰“æ–­ä½¿ç”¨ã€‚
    /// çœ©æ™•ä¸­ä¸å¤„ç†ï¼ˆçœ©æ™•æœ‰è‡ªå·±çš„æ—¶é•¿ï¼Œä¸èƒ½è¢«æ‰“æ–­æå‰ç»“æŸï¼‰ã€‚
    /// </summary>
    public void InterruptCurrentAction(string reason)
    {
        if (IsStunned) return;

        // æ„å›¾ï¼ˆå•æ‹› / å¥—è·¯ / å…œåº•å¡ï¼‰å…¨éƒ¨ä½œåºŸï¼Œå›åˆ°ç§»åŠ¨çŠ¶æ€åé‡æ–°å®š
        if (AI != null) AI.ClearIntent(reason);

        // CombatState.Exit ä¼šåœæ‰å‡ºæ‹›åç¨‹å¹¶é€šçŸ¥æ‰§è¡Œå™¨æ”¶æ‘Š
        if (CurrentState == CombatState) ChangeState(MoveState);
    }

    private void HideShieldVisual() { if (shieldVisual != null) shieldVisual.SetActive(false); }
    private void ShowShieldVisual() { if (shieldVisual != null) shieldVisual.SetActive(true); }

    private void HandlePhase2()
    {
        Debug.Log("[BossController] ´¥·¢¶ş½×¶Î£¡");

        // ¡¾¸Ä¶¯¡¿²»ÔÙÖ»Í£µ¯Ä»£¬Í¨ÖªËùÓĞÖ´ĞĞÆ÷ÊÕÌ¯
        CancelAllExecutors();

        if (TryGetComponent(out Rigidbody2D rb)) rb.linearVelocity = Vector2.zero;

        // ¡¾¹Ø¼üĞŞ¸Ä¡¿¶ş½×¶Î×ª³¡´«ËÍµ½ÖĞÑë£¬²»ÔÙĞ´ËÀ×ø±ê£¬Ö±½ÓÎ¯ÍĞ¸ø´«ËÍÆ÷
        if (Teleporter != null) Teleporter.ExecuteTeleport(TeleportTargetType.Center);

        if (AI != null) AI.SwitchToPhase2();

        // ¶ş½×¶Î×ª³¡ÊÇÒ»´ÎÇ¿ÖÆÖØÖÃ£ºÁ¢¿ÌÂú¶Ü£¬²¢°Ñ×´Ì¬»ú´ÓÆÆ·ÀÖĞ×§³öÀ´¡£
        // ²»×öÕâÁ½¼şÊÂµÄ»°£¬CurrentState »áÒ»Ö±Í£ÔÚ BossStunState ÉÏÊıÍêÊ£ÓàµÄÆÆ·ÀÊ±¼ä¡£
        bossState.bossMechanic.RecoverShield();
        ChangeState(MoveState);
    }

    protected override void Die()
    {
        Debug.Log("Boss±»»÷°ÜÁË£¡´¥·¢ËÀÍöÑİ³ö£¡");
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
            // ½«ÉËº¦ÇëÇó×ª·¢¸øºÚ°å (BossState) ´¦Àí
            bossState.bossMechanic.TakeDamage(info.amount, info.type);
        }
    }
}