using UnityEngine;

/// <summary>
/// Boss µÄÖ»¶ÁÇé±¨×ÜÏß£¨¡¸¹¤ÅÆ¡¹£©¡£
///
/// ¡¾ÎªÊ²Ã´ĞèÒªËü¡¿
/// Ö´ĞĞÆ÷£¨½üÕ½¡¢¼¤¹â¡¢µ¯Ä»¡¢´«ËÍ£©ºÍÌõ¼ş¶ÔÏó£¨Íæ¼ÒÑªÁ¿µÍ¡¢¾àÀë¹ıÔ¶¡¢±»±ÆÇ½½Ç£©
/// ¶¼ĞèÒªÍ¬Ò»Åú×ÊÁÏ£ºÍæ¼ÒÔÚÄÄ¡¢Íæ¼ÒÑª¶àÉÙ¡¢Boss ºÚ°åÉÏ¼ÇÁËÊ²Ã´¡£
///
/// Èô²»Í³Ò»¹©¸ø£¬Ö»ÓĞÁ½ÌõÔã¸âµÄÂ·£º
///  1. ¸øÃ¿¸öĞÂÀà¶¼Ğ´Ò»±é Init(player, bossState, ...)£¬²ÎÊıÁĞ±íÔ½À´Ô½³¤£»
///  2. ¸÷×Ô FindObjectOfType&lt;PlayerState&gt;() ¡ª¡ª Ã¿´ÎÅĞ¶¨Ò»´ÎÈ«³¡ËÑË÷£¬
///     ¶øÌõ¼şÏµÍ³Ã¿Ö¡ÒªÅÜºÃ¼¸¸öÌõ¼ş£¬ĞÔÄÜ»á±À¡£
///
/// ÓÃ readonly struct + in ´«µİ£¬Áã¶Ñ·ÖÅä£¨Óë DamageInfo Í¬Àí£©¡£
/// </summary>
public readonly struct BossContext
{
    /// <summary>Íæ¼ÒÉíÉÏÊÜ»÷ºËĞÄµÄÎïÌåÃû¡£Ãé×¼µãÓÅÏÈÈ¡Ëü¡£</summary>
    public const string HurtboxName = "Hurtbox_Core";

    public readonly Transform boss;
    public readonly Transform player;
    public readonly BossState bossState;
    public readonly PlayerState playerState;
    public readonly BossController controller;

    // ¡¾ĞÂÔö¡¿Ãé×¼µãÏà¹Ø¡£ÔÚ¹¹ÔìÊ±½âÎöÒ»´Î²¢»º´æ£¬±ÜÃâÃ¿Ö¡ Find¡£
    private readonly Transform playerHurtbox;
    private readonly Collider2D playerCollider;

    // ç©å®¶çŠ¶æ€æœºï¼šè¯»"åœ¨ä¸åœ¨åœ°ä¸Š / åœ¨ä¸åœ¨é£"ï¼Œä¾›å¥—è·¯æ¡ä»¶ä½¿ç”¨
    private readonly PlayerStateMachine playerSM;

    /// <summary>
    /// ç©å®¶çš„èº«ä½“ç¢°æ’ç®±ï¼ˆæ ¹èŠ‚ç‚¹ä¸Šçš„ BoxCollider2Dï¼ŒPlayerSensor ç”¨çš„é‚£ä¸ªï¼Œè¹²ä¸‹ä¼šè¢«å‹æ‰ï¼‰ã€‚
    /// Boss è¿‘æˆ˜åœ¨ PlayerCombatConfig.meleeHitsBodyCollider å‹¾é€‰æ—¶æ‰“å®ƒã€‚
    /// </summary>
    public readonly Collider2D playerBodyCollider;

    public BossContext(BossController controller, Transform player)
    {
        this.controller = controller;
        this.boss = controller != null ? controller.transform : null;
        this.bossState = controller != null ? controller.bossState : null;

        this.player = player;
        // Íæ¼ÒµÄÅö×²Ìå³£¹ÒÔÚ×ÓÎïÌåÉÏ£¬ÓÃ InParent ¶µ×¡ÕâÖÖ½á¹¹
        this.playerState = player != null ? player.GetComponentInParent<PlayerState>() : null;

        this.playerHurtbox = FindHurtbox(player);
        this.playerCollider = player != null ? player.GetComponentInChildren<Collider2D>() : null;
        this.playerSM = playerState != null ? playerState.GetComponent<PlayerStateMachine>() : null;
        this.playerBodyCollider = playerState != null ? playerState.GetComponent<BoxCollider2D>() : null;
    }

    /// <summary>Boss è¿‘æˆ˜æ˜¯å¦æ”¹æ‰“ç©å®¶èº«ä½“ç¢°æ’ç®±ï¼ˆè¯» PlayerCombatConfig çš„å¼€å…³ï¼›è¯»ä¸åˆ°æ—¶æŒ‰é»˜è®¤"æ˜¯"ï¼‰</summary>
    public bool MeleeHitsPlayerBody
        => playerBodyCollider != null
           && (playerState == null || playerState.combatConfig == null || playerState.combatConfig.meleeHitsBodyCollider);

    /// <summary>ç©å®¶ç«™åœ¨åœ°ä¸Šï¼ˆæ‰¾ä¸åˆ°ç©å®¶çŠ¶æ€æœºæ—¶æŒ‰"åœ¨åœ°ä¸Š"ç®—ï¼‰</summary>
    public bool IsPlayerGrounded => playerSM == null || playerSM.IsGrounded();

    /// <summary>ç©å®¶æ­£åœ¨é£è¡Œï¼ˆå·¡èˆªæˆ–é£è¡Œæˆ˜æ–—ï¼‰</summary>
    public bool IsPlayerFlying => playerSM != null && playerSM.IsFlying;

    /// <summary>
    /// ç©å®¶ç»•åˆ°äº† Boss èƒŒåï¼šç©å®¶æ­¤åˆ»ç«™åœ¨ Boss ä¸Šä¸€æ¬¡è¿‘æˆ˜ / çªè¿›å‡ºæ‰‹æ–¹å‘çš„åä¾§ã€‚
    /// Boss è¿˜æ²¡å‡ºè¿‡è¿‘æˆ˜ / çªè¿›ï¼ˆæ²¡æœ‰æœå‘è®°å½•ï¼‰æ—¶è¿”å› falseã€‚
    /// ç©å®¶å‡ ä¹ä¸ Boss é‡å æ—¶ä¹Ÿç®—"ä¸åœ¨èƒŒå"ï¼Œé¿å…å·¦å³æŠ–åŠ¨ã€‚
    /// </summary>
    public bool IsPlayerBehindBoss
    {
        get
        {
            if (controller == null) return false;
            float facing = controller.LastAttackFacing;
            if (facing == 0f) return false;

            float side = HorizontalSignToPlayer;
            return side != 0f && side != facing;
        }
    }

    private static Transform FindHurtbox(Transform player)
    {
        if (player == null) return null;

        // ÏÈÕÒÖ±½Ó×ÓÎïÌå
        Transform direct = player.Find(HurtboxName);
        if (direct != null) return direct;

        // ÔÙÍùÏÂµİ¹éÕÒÒ»²ãÒÔÉÏµÄÇé¿ö
        Transform[] all = player.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == HurtboxName) return all[i];
        }

        return null;
    }

    /// <summary>Çé±¨ÊÇ·ñÍêÕû¿ÉÓÃ¡£Ö´ĞĞÆ÷ºÍÌõ¼şÔÚÊ¹ÓÃÇ°Ó¦ÏÈ¼ì²é¡£</summary>
    public bool IsValid => boss != null && player != null && bossState != null;

    /// <summary>
    /// ¡¾Ãé×¼µã¡¿ËùÓĞ¹¥»÷¶¼Ó¦¸ÃÃéÕâÀï£¬¶ø²»ÊÇ player.position¡£
    ///
    /// Ô­Òò£ºÍæ¼ÒµÄÖáĞÄµãÍ¨³£ÔÚ½Åµ×£¬Ö±½ÓÃé transform.position »áµ¼ÖÂ
    /// ¼¤¹â¡¢µ¯Ä»È«²¿´òÔÚµØÃæÉÏ¡£ÓÅÏÈÈ¡ Hurtbox_Core£¬
    /// Ã»ÓĞÔòÍË»ØÅö×²ÌåÖĞĞÄ£¬×î²î²ÅÓÃ½Åµ×¡£
    /// </summary>
    public Vector2 PlayerAimPoint
    {
        get
        {
            if (playerHurtbox != null) return playerHurtbox.position;
            if (playerCollider != null) return playerCollider.bounds.center;
            return player != null ? (Vector2)player.position : Vector2.zero;
        }
    }

    /// <summary>ÓëÍæ¼ÒµÄÖ±Ïß¾àÀë¡£Íæ¼Ò²»´æÔÚÊ±·µ»Ø float.MaxValue£¨ÊÓÎª¹»²»×Å£©¡£</summary>
    public float DistanceToPlayer =>
        (boss != null && player != null)
            ? Vector2.Distance(boss.position, player.position)
            : float.MaxValue;

    /// <summary>Íæ¼ÒÏà¶Ô Boss µÄË®Æ½·½Ïò£ºÓÒÎª +1£¬×óÎª -1¡£ÄÃ²»µ½Íæ¼ÒÊ±·µ»Ø 0¡£</summary>
    public float HorizontalSignToPlayer
    {
        get
        {
            if (boss == null || player == null) return 0f;
            float dx = player.position.x - boss.position.x;
            return Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : 0f;
        }
    }

    /// <summary>Íæ¼Òµ±Ç°ÑªÁ¿Õ¼±È 0~1¡£ÄÃ²»µ½Íæ¼ÒÊ±·µ»Ø 1£¨ÊÓÎªÂúÑª£¬Ìõ¼şÄ¬ÈÏ²»´¥·¢£©¡£</summary>
    public float PlayerHPRatio
    {
        get
        {
            if (playerState == null || playerState.health == null) return 1f;
            int max = playerState.health.maxHP;
            return max > 0 ? (float)playerState.health.currentHP / max : 1f;
        }
    }

    /// <summary>Boss ×ÔÉíÑªÁ¿Õ¼±È 0~1¡£</summary>
    public float BossHPRatio
    {
        get
        {
            if (bossState == null || bossState.health == null) return 1f;
            int max = bossState.health.maxHP;
            return max > 0 ? (float)bossState.health.currentHP / max : 1f;
        }
    }
}