using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VariableInfo
{
    public string VariableName;
    public Type VariableType { get; }
    public object Value { get; }

    public VariableInfo(string variableName, Type variableType, object value)
    {
        VariableName = variableName;
        VariableType = variableType;
        Value = value;
    }
}
