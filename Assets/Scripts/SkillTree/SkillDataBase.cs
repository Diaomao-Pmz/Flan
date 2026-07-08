using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewSkillDataBase", menuName = "ScriptableObjects/DataBase")]
public class SkillDataBase : ScriptableObject
{
    public List<SkillData> data = new List<SkillData> ();
}

[Serializable]
public class SkillData
{
    public SkillType SkillType;
    public int ManaCost;
    public SkillType RequiredSkillToUnlock;
}