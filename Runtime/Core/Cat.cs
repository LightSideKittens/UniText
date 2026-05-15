using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// Conditional debug logging wrapper that compiles out when UNITEXT_DEBUG is not defined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All methods are marked with [Conditional("UNITEXT_DEBUG")] so calls are completely
    /// removed from release builds with no runtime overhead.
    /// </para>
    /// <para>
    /// Mirrors the UnityEngine.Debug API for easy replacement of debug logging calls.
    /// </para>
    /// </remarks>
    internal static class Cat
    {
        [Conditional("UNITEXT_DEBUG")]
        public static void Meow(object message)
        {
            Debug.Log(message);
        }


        [Conditional("UNITEXT_DEBUG")]
        public static void Meow(object message, Object context)
        {
            Debug.Log(message, context);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowFormat(string format, params object[] args)
        {
            Debug.LogFormat(format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowFormat(Object context, string format, params object[] args)
        {
            Debug.LogFormat(context, format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowFormat(LogType logType, LogOption logOptions, Object context, string format, params object[] args)
        {
            Debug.LogFormat(logType, logOptions, context, format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowWarn(object message)
        {
            Debug.LogWarning(message);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowWarn(object message, Object context)
        {
            Debug.LogWarning(message, context);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowWarnFormat(string format, params object[] args)
        {
            Debug.LogWarningFormat(format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowWarnFormat(Object context, string format, params object[] args)
        {
            Debug.LogWarningFormat(context, format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowError(object message)
        {
            Debug.LogError(message);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowError(object message, Object context)
        {
            Debug.LogError(message, context);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowErrorFormat(string format, params object[] args)
        {
            Debug.LogErrorFormat(format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowErrorFormat(Object context, string format, params object[] args)
        {
            Debug.LogErrorFormat(context, format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowException(System.Exception exception)
        {
            Debug.LogException(exception);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowException(System.Exception exception, Object context)
        {
            Debug.LogException(exception, context);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowAssertion(object message)
        {
            Debug.LogAssertion(message);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowAssertion(object message, Object context)
        {
            Debug.LogAssertion(message, context);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowAssertionFormat(string format, params object[] args)
        {
            Debug.LogAssertionFormat(format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void MeowAssertionFormat(Object context, string format, params object[] args)
        {
            Debug.LogAssertionFormat(context, format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void Assert(bool condition)
        {
            Debug.Assert(condition);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void Assert(bool condition, Object context)
        {
            Debug.Assert(condition, context);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void Assert(bool condition, object message)
        {
            Debug.Assert(condition, message);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void Assert(bool condition, object message, Object context)
        {
            Debug.Assert(condition, message, context);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void AssertFormat(bool condition, string format, params object[] args)
        {
            Debug.AssertFormat(condition, format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void AssertFormat(bool condition, Object context, string format, params object[] args)
        {
            Debug.AssertFormat(condition, context, format, args);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void DrawLine(Vector3 start, Vector3 end)
        {
            Debug.DrawLine(start, end);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            Debug.DrawLine(start, end, color);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void DrawLine(Vector3 start, Vector3 end, Color color, float duration)
        {
            Debug.DrawLine(start, end, color, duration);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void DrawLine(Vector3 start, Vector3 end, Color color, float duration, bool depthTest)
        {
            Debug.DrawLine(start, end, color, duration, depthTest);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void DrawRay(Vector3 start, Vector3 dir)
        {
            Debug.DrawRay(start, dir);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void DrawRay(Vector3 start, Vector3 dir, Color color)
        {
            Debug.DrawRay(start, dir, color);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void DrawRay(Vector3 start, Vector3 dir, Color color, float duration)
        {
            Debug.DrawRay(start, dir, color, duration);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void DrawRay(Vector3 start, Vector3 dir, Color color, float duration, bool depthTest)
        {
            Debug.DrawRay(start, dir, color, duration, depthTest);
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void Break()
        {
            Debug.Break();
        }

        [Conditional("UNITEXT_DEBUG")]
        public static void ClearDeveloperConsole()
        {
            Debug.ClearDeveloperConsole();
        }
    }
}
