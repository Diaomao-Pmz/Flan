using System;
using UnityEngine;

[Serializable]
public class GMCommandDefinition
{
    public string label;
    [TextArea]
    public string command;
}