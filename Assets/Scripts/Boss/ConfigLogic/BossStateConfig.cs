using UnityEngine;

/// <summary>
/// Boss 的体质：血量、护盾、破盾眩晕、二阶段阈值。
/// 拖到 Boss 身上 BossState 的 State Config 栏位。运行时只读（当前血量 / 当前护盾等运行时数值仍在 BossState 上）。
/// </summary>
[CreateAssetMenu(fileName = "BossStateConfig", menuName = "Flandre/Boss/State Config")]
public class BossStateConfig : ScriptableObject
{
    [Header("--- 生命 ---")]
    [Min(1)]
    public int maxHP = 100;

    [Tooltip("血量降到这个值或以下时进入二阶段")]
    public int phase2Threshold = 30;

    [Header("--- 护盾 ---")]
    [Min(0)]
    public int shieldMaxHP = 300;

    [Tooltip("玩家招式没填破盾值时，近战一下扣多少护盾")]
    public int meleeShieldDamage = 100;

    [Tooltip("玩家招式没填破盾值时，远程一下扣多少护盾")]
    public int rangedShieldDamage = 3;

    [Header("--- 破盾 ---")]
    [Tooltip("破盾后的眩晕时长 n（秒）。眩晕期间不出招；结束后进入近战为主阶段")]
    public float stunDuration = 2f;

    [Tooltip("回盾时间 x（秒），从【破盾那一刻】开始算。\n" +
             "近战为主阶段的实际时长 = x - 眩晕时长。x 小于眩晕时长时按眩晕时长算（晕完立刻回盾）")]
    public float shieldRecoverTime = 5f;

    [Tooltip("破盾眩晕时 Boss 被弹起的速度")]
    public float stunKnockupSpeed = 15f;

    [Tooltip("【预留，暂未生效】玩家连招追加破盾时间")]
    public float comboExtendDuration = 1f;
}
