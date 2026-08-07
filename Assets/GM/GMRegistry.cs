using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

public static class GMRegistry
{
    private sealed class CommandInfo
    {
        public string Name;
        public string Description;
        public MethodInfo Method;
        public ParameterInfo[] Parameters;
        public bool IsStatic;
    }

    private static readonly Dictionary<string, CommandInfo> Commands =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Init()
    {
        Commands.Clear();

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var asm in assemblies)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }

            foreach (var type in types)
            {
                if (type == null) continue;

                var methods = type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static |
                    BindingFlags.Instance);

                foreach (var method in methods)
                {
                    var attr = method.GetCustomAttribute<GMCommandAttribute>();
                    if (attr == null) continue;

                    Commands[attr.Name] = new CommandInfo
                    {
                        Name = attr.Name,
                        Description = attr.Description,
                        Method = method,
                        Parameters = method.GetParameters(),
                        IsStatic = method.IsStatic
                    };
                }
            }
        }

        Debug.Log($"[GM] registry initialized: {Commands.Count} commands");
    }

    public static IEnumerable<string> GetCommandNames()
    {
        return Commands.Keys.OrderBy(x => x);
    }

    public static bool Execute(string line, out string message)
    {
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            message = "empty command";
            return false;
        }

        var parts = SplitArgs(line);
        if (parts.Count == 0)
        {
            message = "empty command";
            return false;
        }

        var cmdName = parts[0];

        if (!Commands.TryGetValue(cmdName, out var cmd))
        {
            message = $"unknown command: {cmdName}";
            return false;
        }

        if (parts.Count - 1 != cmd.Parameters.Length)
        {
            message = $"arg mismatch: {cmdName} expects {cmd.Parameters.Length}, got {parts.Count - 1}";
            return false;
        }

        try
        {
            object target = null;
            if (!cmd.IsStatic)
            {
                target = FindTargetInstance(cmd.Method.DeclaringType);
                if (target == null)
                {
                    message = $"no active instance found for {cmd.Method.DeclaringType.Name}";
                    return false;
                }
            }

            var args = new object[cmd.Parameters.Length];
            for (int i = 0; i < cmd.Parameters.Length; i++)
            {
                args[i] = ConvertArg(parts[i + 1], cmd.Parameters[i].ParameterType);
            }

            var result = cmd.Method.Invoke(target, args);
            message = cmd.Method.ReturnType == typeof(void)
                ? "ok"
                : (result?.ToString() ?? "null");

            return true;
        }
        catch (TargetInvocationException e)
        {
            message = $"error: {e.InnerException?.GetBaseException().Message ?? e.GetBaseException().Message}";
            return false;
        }
        catch (Exception e)
        {
            message = $"error: {e.GetBaseException().Message}";
            return false;
        }
    }

    private static object FindTargetInstance(Type type)
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType(type);
#else
        return UnityEngine.Object.FindObjectOfType(type);
#endif
    }

    private static object ConvertArg(string raw, Type targetType)
    {
        if (targetType == typeof(string))
            return raw;

        if (targetType == typeof(int))
            return int.Parse(raw, CultureInfo.InvariantCulture);

        if (targetType == typeof(float))
            return float.Parse(raw, CultureInfo.InvariantCulture);

        if (targetType == typeof(double))
            return double.Parse(raw, CultureInfo.InvariantCulture);

        if (targetType == typeof(bool))
        {
            if (raw.Equals("1") || raw.Equals("on", StringComparison.OrdinalIgnoreCase) || raw.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;

            if (raw.Equals("0") || raw.Equals("off", StringComparison.OrdinalIgnoreCase) || raw.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;

            return bool.Parse(raw);
        }

        if (targetType.IsEnum)
            return Enum.Parse(targetType, raw, true);

        throw new NotSupportedException($"unsupported arg type: {targetType.Name}");
    }

    private static List<string> SplitArgs(string input)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}