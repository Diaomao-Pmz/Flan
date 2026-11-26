using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss 的传送设置：落点安检、受击传送列表。
/// 拖到 Boss 身上 BAE_Teleporter 的 Tp Config 栏位。运行时只读。
/// 传送点、场地中央、玩家引用是场景里的物体，仍在 BAE_Teleporter 组件上配。
/// </summary>
[CreateAssetMenu(fileName = "BossTpConfig", menuName = "Flandre/Boss/Tp Config")]
public class BossTpConfig : ScriptableObject
{
    [Header("--- 随机点传送 (RandomPoint) ---")]
    [Tooltip("小于此距离视为「就在原地」，会被排除，避免传了个寂寞。")]
    public float samePointThreshold = 0.1f;

    [Header("--- 落点安检（绕背 / 意图距离传送用）---")]
    [Tooltip("哪些图层算「不能落」：勾地面、墙。\n" +
             "落点（按 Boss 身体碰撞框大小）压到这些图层就换另一侧；两侧都不行则这次不传。\n" +
             "选 Nothing = 不检查地形（不推荐）")]
    public LayerMask landingBlockLayers;

    [Tooltip("落点不能和玩家身体重叠")]
    public bool avoidPlayerOverlap = true;

    [Tooltip("安检时把 Boss 碰撞框缩小这么多（每边，世界单位），\n" +
             "避免 Boss 本来就踩在地上时，脚底和地面「刚好接触」被误判为压到地面")]
    public float landingCheckInset = 0.05f;

    [Tooltip("勾选 = 落点脚下一定距离内必须有地面（Landing Block Layers 里的物体），否则不能落。\n" +
             "防止传送到地图外的半空。以后 Boss 要传送到空中时可以关掉")]
    public bool requireGroundBelow = true;

    [Tooltip("从落点身体底部往下找地面的最大距离（格）")]
    [Min(0.1f)]
    public float groundCheckDistance = 3f;

    [Tooltip("打印绕背传送的详细日志（起点、落点、传送后实际位置），排查卡地底问题用。稳定后可关")]
    public bool debugTeleport = true;

    [Header("--- 受击传送 (Hit Reactions) ---")]
    [Tooltip("挨打时会检查的传送卡（需勾选卡上的 React To Hit）。\n" +
             "从上往下检查，同一次挨打最多触发一张。\n" +
             "列表留空 = Boss 挨打永远不会被动传送")]
    public List<TeleportNode> hitReactions = new List<TeleportNode>();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (hitReactions == null) return;
        foreach (TeleportNode node in hitReactions)
        {
            if (node != null && !node.reactToHit)
            {
                Debug.LogWarning(
                    $"[BossTpConfig] 「{node.name}」在 Hit Reactions 列表里，但卡上没勾 React To Hit，不会触发。", this);
            }
        }
    }
#endif
}
