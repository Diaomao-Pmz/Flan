using UnityEngine;
using System.Collections.Generic;
using Flandre.CombatSystem;

[CreateAssetMenu(fileName = "NewComboNode", menuName = "Flandre/Combat/Combo Node")]
public class ComboNode : ScriptableObject
{
    [Header("节点基础属性")]
    public string nodeName = "XXX";
    public InputCmd inputRequirement = InputCmd.LightAttack; // 触发此招所需的按键
    public float comboWindow = 0.5f;                         // 允许派生下一招的时间窗口

    [Header("动画与表现")]
    public string animName = "XXX"; // 必须与Animator里的状态名完全一致

    [Header("战斗与位移参数")]
    public Vector2 forwardThrust = new Vector2(0f, 0f); // 攻击时的自带位移

    [Header("Hitbox 判定参数")]
    public int damage = 0;

    [Tooltip("判定框的宽高大小")]
    public Vector2 hitboxSize = new Vector2(0f, 0f);

    [Tooltip("判定框相对于玩家中心的坐标偏移 (X正数为向面朝方向偏移)")]
    public Vector2 hitboxOffset = new Vector2(0f, 0f);

    [Tooltip("判定持续时间(秒)。填0为瞬间伤害，填0.5表示判定框存在0.5秒")]
    public float hitboxDuration = 0f;

    [Tooltip("对怪物造成的击退力度/削韧力")]
    public float knockbackForce = 0f;

    [Header("连招派生树 (拖入子节点)")]
    public List<ComboNode> childNodes = new List<ComboNode>();
}