using System.Collections;
using UnityEngine;

/// <summary>
/// Boss µÄÕ½¶·×´Ì¬¡£
///
/// ¡¾±¾´Î¸Ä¶¯¡¿Ëü²»ÔÙÈÏÊ¶ÈÎºÎ¾ßÌå¼¼ÄÜ¡£
/// Ô­ÏÈÕâÀïÊÇÒ»´® if (node is TeleportNode) / if (node is BulletNode) / else µÄ·ÖÖ§Á´£¬
/// Ã¿¼ÓÒ»ÖÖ¼¼ÄÜ¾ÍÒª»ØÀ´¸ÄÒ»´Î¡£ÏÖÔÚÖ»×öÈı¼şÊÂ£º³é¿¨ ¡ú ²éÖ´ĞĞÆ÷ ¡ú µÈËüÑİÍê¡£
///
/// ÃüÃûËµÃ÷£º±¾ÀàÊÇ IState£¨Õ½¶·×´Ì¬£©£¬ÕæÕı¸É»îµÄÊÇ¸÷¸ö IBossActionExecutor¡£
/// ÈôÈÕºóÖØ¹¹£¬½¨Òé¸ÄÃû BossCombatState£¬°Ñ Executor Ò»´ÊÈÃ¸øÖ´ĞĞÆ÷½Ó¿Ú¡£
/// </summary>
public class BossActionExecuter : IState
{
    BossController boss;
    Coroutine attackCoroutine;

    // ¼ÇÂ¼µ±Ç°ÕıÔÚÑİ³öµÄÖ´ĞĞÆ÷£¬±»´ò¶ÏÊ±ºÃÍ¨ÖªËüÊÕÌ¯
    IBossActionExecutor activeExecutor;

    public BossActionExecuter(BossController bc)
    {
        boss = bc;
    }

    public void Enter()
    {
<<<<<<< Updated upstream
        // Ã¿´Î½øÈëÕ½¶·×´Ì¬Ê±£¬È·±£ÎïÀíËÙ¶È¹éÁã£¬×¨ĞÄ³öÕĞ£¨ÊµÏÖ"Éä»÷Ê±²»ÒÆ¶¯"£©
=======
        // é˜²å¾¡ï¼šä¸Šä¸€æ¬¡ DoCombat è‹¥æ²¡ yield å°±ç›´æ¥ç»“æŸï¼ˆSelectSkill è¿”å› nullï¼‰ï¼Œ
        // StartCoroutine è¿”å›åå¥æŸ„ä¼šè¢«èµ‹æˆä¸€ä¸ªå·²ç»“æŸçš„åç¨‹ï¼Œå¯¼è‡´è¿™é‡Œæ°¸è¿œä»¥ä¸º"è¿˜åœ¨å‡ºæ‹›"
        attackCoroutine = null;
        activeExecutor = null;

        boss.SetMovementStatus("[ç§»åŠ¨] å‡ºæ‹›ä¸­");

        // æ¯æ¬¡è¿›å…¥æˆ˜æ–—çŠ¶æ€æ—¶ï¼Œç¡®ä¿ç‰©ç†é€Ÿåº¦å½’é›¶ï¼Œä¸“å¿ƒå‡ºæ‹›ï¼ˆå®ç°"å°„å‡»æ—¶ä¸ç§»åŠ¨"ï¼‰
>>>>>>> Stashed changes
        Rigidbody2D rb = boss.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    public void Exit()
    {
        // ========================================================
        // ¡¾ºËĞÄ°²È«±£»¤¡¿£ºBoss ÔÚ³öÕĞÆÚ¼ä±»ÆÆ¶Ü£¨½øÈë StunState£©»ò×ª½×¶ÎÊ±£¬
        // Ç¿ÖÆÆş¶ÏĞ­³Ì²¢Í¨ÖªÖ´ĞĞÆ÷ÊÕÌ¯£¬·ÀÖ¹"ÔÎÁË»¹ÔÚÉä»÷/¼¤¹â»¹ÔÚÕÕ"µÄ Bug¡£
        // ========================================================
        if (attackCoroutine != null)
        {
            boss.StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        // ¡¾¸Ä¶¯¡¿²»ÔÙĞ´ËÀ BulletEmitter.StopAttack()£¬¸ÄÎªÍ¨Öªµ±Ç°Ö´ĞĞÆ÷¡£
        // ÕâÑùÒÔºó¼Ó¼¤¹â¡¢³å×²£¬´ò¶ÏÂß¼­²»ĞèÒªÔÙ¸ÄÕâÀï¡£
        if (activeExecutor != null)
        {
            activeExecutor.Cancel();
            activeExecutor = null;
        }
    }

    public void FixedUpdate()
    {

    }

    public void Update()
    {
<<<<<<< Updated upstream
        // µ±Âú×ã¹¥»÷Ìõ¼ş£¬ÇÒµ±Ç°Ã»ÓĞÕıÔÚÖ´ĞĞµÄ¹¥»÷Ğ­³ÌÊ±£¬Æô¶¯¹¥»÷
        if (boss.AI.canAttack && attackCoroutine == null)
        {
            attackCoroutine = boss.StartCoroutine(DoCombat());
        }
=======
        if (attackCoroutine != null) return;

        // è¿›å…¥æˆ˜æ–—çŠ¶æ€æ—¶æ„å›¾å·²ç»å°±ä½ï¼ˆMoveState åˆ¤æ–­è¿‡å°„ç¨‹ï¼‰ï¼Œç›´æ¥å‡ºæ‹›ã€‚
        // SelectSkill å‡ºæ‰‹å‰è¿˜ä¼šå†æ ¡éªŒä¸€æ¬¡ï¼Œä¸é€šè¿‡å°±è¿”å› null â†’ å›ç§»åŠ¨çŠ¶æ€é‡æ–°å®šæ„å›¾ã€‚
        attackCoroutine = boss.StartCoroutine(DoCombat());
>>>>>>> Stashed changes
    }

    IEnumerator DoCombat()
    {
<<<<<<< Updated upstream
        // 1. ÈÃ AI ³éÃ¤ºĞ£¨³é³öÀ´µÄÊÇ»ùÀà ActionNode£©
=======
        // 1. å…‘ç° AI åœ¨ç§»åŠ¨é˜¶æ®µå®šå¥½çš„æ„å›¾ï¼ˆæŠ½å‡ºæ¥çš„æ˜¯åŸºç±» ActionNodeï¼‰
>>>>>>> Stashed changes
        ActionNode actionNode = boss.AI.SelectSkill();

        if (actionNode == null)
        {
            Finish();
            yield break;
        }

<<<<<<< Updated upstream
        // 2. ²é±íÕÒµ½ÈÏÁìÕâÖÖ¿¨µÄÖ´ĞĞÆ÷¡£´óÄÔµ½´ËÎªÖ¹£¬²»¹ØĞÄËüÊÇÊ²Ã´¼¼ÄÜ¡£
=======
        // å¥—è·¯æ­¥éª¤çš„"å‡ºæ‹›å‰åœé¡¿"ï¼ˆå•æ‹›ä¸º 0ï¼‰ã€‚åœé¡¿æœŸé—´è¢«ç ´ç›¾ç­‰æ‰“æ–­æ—¶ï¼ŒExit ä¼šææ‰æœ¬åç¨‹
        float delay = boss.AI.NextActionDelay;
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);

            // åœé¡¿æœŸé—´ç©å®¶å¯èƒ½ç»•åˆ°èƒŒå / èµ·é£äº†ï¼šæŒ‰æ­¤åˆ»æƒ…å†µé‡æ–°æ£€æŸ¥è¿™ä¸€æ­¥çš„å€™è¡¥æ‹›å¼
            actionNode = boss.AI.RefreshStepChoice(actionNode);
        }

        // 2. æŸ¥è¡¨æ‰¾åˆ°è®¤é¢†è¿™ç§å¡çš„æ‰§è¡Œå™¨ã€‚å¤§è„‘åˆ°æ­¤ä¸ºæ­¢ï¼Œä¸å…³å¿ƒå®ƒæ˜¯ä»€ä¹ˆæŠ€èƒ½ã€‚
>>>>>>> Stashed changes
        IBossActionExecutor executor = boss.GetExecutorFor(actionNode);

        if (executor == null)
        {
            Debug.LogError(
                $"[BossActionExecuter] Ã»ÓĞÖ´ĞĞÆ÷ÈÏÁì {actionNode.GetType().Name}" +
                $"£¨¿¨Æ¬: {actionNode.name}£©¡£Çë¼ì²é¶ÔÓ¦ Executor ÊÇ·ñ¹ÒÔÚ Boss ÉíÉÏ¡£");
            yield return new WaitForSeconds(1f); // Í£¶ÙÒ»ÏÂ·ÀÖ¹¿Õ×ªË¢ÆÁ
            Finish();
            yield break;
        }

        if (boss.bossStatusText != null)
        {
            string label = string.IsNullOrEmpty(actionNode.actionName)
                ? actionNode.name
                : actionNode.actionName;
            if (!string.IsNullOrEmpty(boss.AI.LastPickRoutineName))
                label = $"[{boss.AI.LastPickRoutineName}] {label}";
            boss.bossStatusText.SetText(label);
        }

        // 3. ½»¸øÖ´ĞĞÆ÷ÑİÍê¡£
        //
        // ¡¾×¢Òâ¡¿ÕâÀïÊÇ yield return executor.Execute(...)£¬¶ø²»ÊÇ
        // yield return boss.StartCoroutine(executor.Execute(...))¡£
        // ºóÕß»áÅÉÉú³öÒ»¸ö¶ÀÁ¢µÄ×ÓĞ­³Ì£¬¸¸Ğ­³Ì±» StopCoroutine Ê±×ÓĞ­³Ì**²»»á**±»Á¬´øÍ£Ö¹£¬
        // ´ò¶ÏÊ±¾Í»á²ĞÁôÒ»¸ö»¹ÔÚÅÜµÄ¶¯×÷¡£Ö±½Ó yield return IEnumerator
        // Ïàµ±ÓÚ°ÑËüÄÚÁª½ø±¾Ğ­³Ì£¬¸¸Í£×Ó±ØÍ£¡£
        activeExecutor = executor;
        yield return executor.Execute(actionNode, boss.Context);
        activeExecutor = null;

        Finish();
    }

    /// <summary>ÊÕÎ²£ºÏÈÇåĞ­³Ì¾ä±úÔÙÇĞ×´Ì¬£¬±ÜÃâ Exit() °Ñ×Ô¼º¸øÍ£ÁË¡£</summary>
    private void Finish()
    {
        attackCoroutine = null;
        activeExecutor = null;

        // å¥—è·¯çš„ä¸‹ä¸€æ­¥å¯ä»¥é©¬ä¸Šæ¥ â†’ ç•™åœ¨æˆ˜æ–—çŠ¶æ€ï¼Œä¸‹ä¸€å¸§ Update ç›´æ¥å‡ºä¸‹ä¸€æ‹›ã€‚
        // ä¸è¿™ä¹ˆåšçš„è¯ï¼Œæ¯ä¸€æ­¥ä¹‹é—´éƒ½ä¼šå…ˆå›ç§»åŠ¨çŠ¶æ€èµ°ä¸€æ®µï¼Œå¥—è·¯å°±è¿ä¸èµ·æ¥äº†ã€‚
        if (boss.AI.HasImmediateFollowUp) return;

        boss.ChangeState(boss.MoveState);
    }
}