using System.Reflection;

namespace DSCC.Orbbec;

internal sealed class OrbbecSdkV2Reflection
{
    public OrbbecSdkV2Reflection(OrbbecSdkRuntimeInfo runtimeInfo)
    {
        RuntimeInfo = runtimeInfo;
        Assembly = runtimeInfo.ManagedWrapperAssembly
            ?? throw new OrbbecSdkUnavailableException(runtimeInfo);
    }

    public OrbbecSdkRuntimeInfo RuntimeInfo { get; }

    public Assembly Assembly { get; }

    public object CreateContext()
    {
        return Create("Orbbec.Context");
    }

    public object CreatePipeline(object device)
    {
        return Create("Orbbec.Pipeline", device);
    }

    public object CreateConfig()
    {
        return Create("Orbbec.Config");
    }

    public object Create(string typeName, params object?[] args)
    {
        var type = GetType(typeName);
        return Activator.CreateInstance(type, args)
            ?? throw new InvalidOperationException($"Could not create {typeName}.");
    }

    public Type GetType(string typeName)
    {
        return Assembly.GetType(typeName, throwOnError: true)
            ?? throw new InvalidOperationException($"Orbbec wrapper type '{typeName}' was not found.");
    }

    public object? Invoke(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(candidate => candidate.Name == methodName)
            .FirstOrDefault(candidate => ParametersMatch(candidate, args));

        if (method is null)
        {
            throw new MissingMethodException(target.GetType().FullName, methodName);
        }

        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    public object? InvokeStatic(string typeName, string methodName, params object?[] args)
    {
        var type = GetType(typeName);
        var method = type.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(candidate => candidate.Name == methodName)
            .FirstOrDefault(candidate => ParametersMatch(candidate, args));

        if (method is null)
        {
            throw new MissingMethodException(type.FullName, methodName);
        }

        try
        {
            return method.Invoke(null, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    public object EnumValue(string typeName, string valueName)
    {
        var type = GetType(typeName);
        return Enum.Parse(type, valueName);
    }

    public static void DisposeIfPossible(object? value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static bool ParametersMatch(MethodInfo method, IReadOnlyList<object?> args)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != args.Count)
        {
            return false;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            var arg = args[index];
            if (arg is null)
            {
                if (parameters[index].ParameterType.IsValueType &&
                    Nullable.GetUnderlyingType(parameters[index].ParameterType) is null)
                {
                    return false;
                }

                continue;
            }

            if (!parameters[index].ParameterType.IsInstanceOfType(arg))
            {
                return false;
            }
        }

        return true;
    }
}
