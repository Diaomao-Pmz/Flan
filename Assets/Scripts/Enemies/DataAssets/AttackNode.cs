using System;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "NewAttackNode", menuName = "ScriptableObjects/AttackNode")]
public class AttackNode : ScriptableObject
{
    //ÊÇ·ñÎª¿Õ»÷
    public bool isAirAttack;
    //ÊÇ·ñÎªÔ¶¾àÀë¹¥»÷
    public bool isFarAttack;
    //¹¥»÷·½Ê½
    public string AttackType;
    [Tooltip("0~1s")]
    public float formationDuration = 0f;
    public string ChargeAttackAnimName;
    public string AttackAnimName;
    public string FinishAttackAnimName;
}
