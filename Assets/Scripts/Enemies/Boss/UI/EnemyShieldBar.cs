using UnityEngine;
using UnityEngine.UI;

// 1. È·±£ÕâÀïµÄÃû×ÖºÍÄãµÄÎÄ¼şÃû EnemyShieldBar.cs Ò»×Ö²»²î£¡
public class EnemyShieldBar : MonoBehaviour
{
    public BossState bossState;
    public Image shieldFillImage;

<<<<<<< Updated upstream
    // 2. Å×Æú Start£¬¸ÄÓÃ OnEnable¡£Ö»ÒªÕâ¸ö UI ÁÁ×Å£¬Ëü¾ÍÒ»¶¨ÄÜ¼àÌıµ½£¡
=======
    [Tooltip("ç ´ç›¾æœŸé—´ï¼ŒæŠ¤ç›¾æ¡æ˜¾ç¤ºå›ç›¾è¿›åº¦ï¼ˆä» 0 æ…¢æ…¢æ¶¨å›æ»¡ï¼‰ã€‚å…³æ‰åˆ™ç ´ç›¾æœŸé—´ä¿æŒç©ºæ¡")]
    public bool showRecoverProgress = true;

    // 2. æŠ›å¼ƒ Startï¼Œæ”¹ç”¨ OnEnableã€‚åªè¦è¿™ä¸ª UI äº®ç€ï¼Œå®ƒå°±ä¸€å®šèƒ½ç›‘å¬åˆ°ï¼
>>>>>>> Stashed changes
    void OnEnable()
    {
        if (bossState != null)
        {
            // ¶©ÔÄ»¤¶Ü×¨ÊôµÄË¢ĞÂ¹ã²¥
            bossState.bossMechanic.OnShieldStatChanged += RefreshShieldUI;
            RefreshShieldUI();
        }
    }

    // 3. Ñø³ÉºÃÏ°¹ß£ºUI Òş²Ø»òÏú»ÙÊ±£¬Á¢¿ÌÈ¡Ïû¼àÌı£¬·ÀÖ¹±¨´í
    void OnDisable()
    {
        if (bossState != null)
        {
            bossState.bossMechanic.OnShieldStatChanged -= RefreshShieldUI;
        }
    }

    // ç ´ç›¾æœŸé—´å›ç›¾è¿›åº¦æ¯å¸§éƒ½åœ¨å˜ï¼Œäº‹ä»¶é©±åŠ¨è¦†ç›–ä¸åˆ°ï¼Œæ‰€ä»¥è¿™æ®µæ—¶é—´é€å¸§åˆ·æ–°
    void Update()
    {
        if (!showRecoverProgress || bossState == null || shieldFillImage == null) return;
        if (!bossState.bossMechanic.isShieldBroken) return;

        shieldFillImage.fillAmount = bossState.bossMechanic.ShieldRecoverProgress;
    }

    private void RefreshShieldUI()
    {
        // ·À´ôÉè¼Æ£ºÈç¹ûÍ¼Æ¬¶ªÁË£¬Ö±½Ó return£¬²»ÈÃÓÎÏ·±ÀÀ£
        if (bossState == null || shieldFillImage == null) return;

        // ¼ÆËã°Ù·Ö±È²¢¸üĞÂ½ø¶ÈÌõ (ÄãµÄÕâ¾ä´úÂëĞ´µÃ·Ç³£±ê×¼£¡)
        float percent = (float)bossState.bossMechanic.shieldCurrentHP / bossState.bossMechanic.shieldMaxHP;
        shieldFillImage.fillAmount = percent;
    }
}