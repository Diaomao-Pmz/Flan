using UnityEngine;

/// <summary>
/// ç ´ç›¾çœ©æ™•ã€‚
///
/// ã€æ”¹åŠ¨ã€‘åªæ™•å›ºå®šçš„ stunDuration ç§’ï¼Œç»“æŸåå›åˆ°ç§»åŠ¨çŠ¶æ€ â€”â€” æ­¤æ—¶æŠ¤ç›¾ä»æ˜¯ç¢çš„ï¼Œ
/// Boss è¿›å…¥ã€Œè¿‘æˆ˜ä¸ºä¸»é˜¶æ®µã€ï¼ŒæŒ‰è¿‘æˆ˜å§¿æ€çš„æƒé‡å‡ºæ‹›ã€‚
/// å›ç›¾ä¸å†ç”±æœ¬çŠ¶æ€è´Ÿè´£ï¼šå€’è®¡æ—¶åœ¨ BossMechanic.Tick é‡Œï¼Œä»ç ´ç›¾é‚£ä¸€åˆ»å¼€å§‹ç®—ã€‚
/// </summary>
public class BossStunState : IState
{
    private BossController boss;
    private float stunTimer;
    private float stunDuration;
    private Rigidbody2D rb;

    public BossStunState(BossController bc)
    {
        boss = bc;
        rb = boss.GetComponent<Rigidbody2D>();
    }

    public void Enter()
    {
<<<<<<< Updated upstream
        Debug.Log("[BossStunState] Boss »¤¶ÜÆÆËé£¬½øÈë»÷·ÉÆÆ·À×´Ì¬£¡");

        // 1. »ñÈ¡Ãæ°åÅäÖÃµÄ»Ö¸´Ê±¼ä
        currentRecoverTime = boss.bossState.bossMechanic.shieldRecoverTime;
        stunTimer = 0f;

        // 2. ²»ÔÙÊ¹ÓÃÊÜÖÊÁ¿Ó°ÏìµÄ AddForce£¬¶øÊÇÖ±½Ó¸³Óè¾ø¶ÔËÙ¶È£¡
        if (rb != null)
        {
            // ¶ÁÈ¡ÎÒÃÇÔÚ BossState ÀïÅäÖÃµÄ»÷·ÉËÙ¶È
            float knockupSpeed = boss.bossState.bossMechanic.stunKnockupSpeed;
            rb.linearVelocity = new Vector2(0f, knockupSpeed);
        }

        // 3. Ç¿ÖÆ´ò¶ÏÕıÔÚ·¢ÉäµÄµ¯Ä»
        if (boss.BulletEmitter != null) boss.BulletEmitter.StopAttack();

        // [TODO ÏÂÒ»²½¼¤»î] ¶©ÔÄÍæ¼ÒµÄÁ¬ÕĞÑÓÊ±ÊÂ¼ş
        // ComboInputBuffer.OnPlayerComboExecuted += ExtendStunTime; 
=======
        BossMechanic mech = boss.bossState.bossMechanic;

        stunDuration = mech.stunDuration;
        stunTimer = 0f;

        Debug.Log($"[BossStunState] æŠ¤ç›¾ç ´ç¢ï¼Œçœ©æ™• {stunDuration:F1} ç§’ï¼ˆ{mech.EffectiveRecoverTime:F1} ç§’åå›ç›¾ï¼‰");

        // ç›´æ¥èµ‹äºˆç»å¯¹é€Ÿåº¦ï¼Œä¸å—è´¨é‡å½±å“
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(0f, mech.stunKnockupSpeed);
        }

        // å¼ºåˆ¶æ‰“æ–­æ­£åœ¨å‘å°„çš„å¼¹å¹•
        if (boss.BulletEmitter != null) boss.BulletEmitter.StopAttack();
>>>>>>> Stashed changes
    }

    public void Update()
    {
        stunTimer += Time.deltaTime;

<<<<<<< Updated upstream
        // µ¹¼ÆÊ±½áÊø£¬»Ö¸´»¤¶Ü²¢»Øµ½Õ½¶·×´Ì¬
        if (stunTimer >= currentRecoverTime)
=======
        if (stunTimer >= stunDuration)
>>>>>>> Stashed changes
        {
            boss.ChangeState(boss.MoveState);
        }
    }

    public void FixedUpdate()
    {

    }

    public void Exit()
    {
<<<<<<< Updated upstream
        Debug.Log("[BossStunState] ÆÆ·À½áÊø£¬Boss »¤¶ÜÖØĞÂÉú³É£¡");
        // [TODO ÏÂÒ»²½¼¤»î] È¡Ïû¶©ÔÄ
        // ComboInputBuffer.OnPlayerComboExecuted -= ExtendStunTime;
    }

    // Áô¸øÏÂÒ»²½Íæ¼ÒComboµ÷ÓÃµÄ»Øµ÷
    private void ExtendStunTime()
    {
        float addTime = boss.bossState.bossMechanic.comboExtendDuration;
        currentRecoverTime += addTime;
        Debug.Log($"[BossStunState] Íæ¼ÒÁ¬ÕĞ×·¼Ó£¡ÆÆ·ÀÊ±¼äÑÓ³¤ {addTime} Ãë£¬µ±Ç°×ÜÊ±³¤: {currentRecoverTime}");
    }
}
=======
        Debug.Log("[BossStunState] çœ©æ™•ç»“æŸ" +
                  (boss.bossState.bossMechanic.isShieldBroken ? "ï¼Œè¿›å…¥è¿‘æˆ˜ä¸ºä¸»é˜¶æ®µ" : ""));
    }
}
>>>>>>> Stashed changes
