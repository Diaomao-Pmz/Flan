using System;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "NewBulletNode", menuName = "ScriptableObjects/BulletNode")]
public class BulletNode : ActionNode
{
    [Header("--- 弹幕专属配置 ---")]
    //public bool isAirAttack; 暂时没用
    public string AttackType;
    [Tooltip("0~1s")]
    public float formationDuration = 0f;
}
