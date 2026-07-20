using UnityEngine;
using System.Collections.Generic;
using Flandre.CombatSystem;

namespace Flandre.CombatSystem
{
    public enum CastCondition
    {
        Anywhere,   // 海陆空均可
        GroundOnly, // 仅限地面
        AirOnly     // 仅限空中
    }

    public enum RequiredState
    {
        Any,
        IdleOrRun,
        Dash,
        Slide,
        Crouch
    }
}

[CreateAssetMenu(fileName = "NewComboNode", menuName = "Flandre/Combat/Combo Node")]
public class ComboNode : ScriptableObject
{
    [Header("节点基础属性")]
    public string nodeName = "XXX";
    public float comboWindow = 0.5f;

    [Header("触发限制条件 (Trigger Requirements)")]
    public CastCondition castCondition = CastCondition.Anywhere;
    public RequiredState requiredState = RequiredState.Any;

    [Tooltip("按键序列：例如'上+攻击'，Size填2，Element0填Up，Element1填LightAttack")]
    public List<InputCmd> inputSequence = new List<InputCmd>() { InputCmd.MainAttack };

    // ==========================================
    // 【新增】：蓄力系统参数
    // ==========================================
    [Header("蓄力设定 (Charge Settings)")]
    [Tooltip("这是否是一个需要长按蓄力的招式？")]
    public bool isChargeSkill = false;

    [Tooltip("需要蓄满多少秒才能释放？(仅在 isChargeSkill 为 true 时有效)")]
    public float requiredChargeTime = 1.0f;

    [Header("动画与表现")]
    [Tooltip("直接从 Project 窗口将动画文件 (Anim Clip) 拖入此处。请确保 Animator 状态机里的 State 名字与该动画文件名一致！")]
    public AnimationClip attackClip;
    public string animName => attackClip != null ? attackClip.name : string.Empty;
    public Vector2 forwardThrust = new Vector2(0f, 0f);

    [Header("Hitbox 判定参数")]
    public int damage = 0;
    public Vector2 hitboxSize = new Vector2(0f, 0f);
    public Vector2 hitboxOffset = new Vector2(0f, 0f);
    public float hitboxDuration = 0f;
    public float knockbackForce = 0f;

    [Header("连招派生树 (拖入子节点)")]
    public List<ComboNode> childNodes = new List<ComboNode>();
}