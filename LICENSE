using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace KerbalismContractScienceBridge
{
    /// <summary>
    /// Small reflection helper with no dependency on Kerbalism.dll.
    ///
    /// Every lookup is explicit and fails closed. A Kerbalism update that
    /// renames a member will produce a diagnostic warning instead of silently
    /// completing unrelated contracts.
    /// </summary>
    internal static class ReflectionUtil
    {
        internal const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal const BindingFlags StaticFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static object Get(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            Type type = instance.GetType();

            PropertyInfo property = type.GetProperty(memberName, InstanceFlags);
            if (property != null)
                return property.GetValue(instance, null);

            FieldInfo field = type.GetField(memberName, InstanceFlags);
            return field != null ? field.GetValue(instance) : null;
        }

        internal static T Get<T>(object instance, string memberName, T fallback)
        {
            object value = Get(instance, memberName);
            return value is T ? (T)value : fallback;
        }

        internal static FieldInfo FindField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, InstanceFlags);
                if (field != null)
                    return field;
            }

            return null;
        }

        internal static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, InstanceFlags);
                if (property != null)
                    return property;
            }

            return null;
        }

        /// <summary>
        /// Finds a method by name only.
        ///
        /// REVIEW FIX: Type.GetMethod(name, flags) throws
        /// AmbiguousMatchException when more than one method shares the
        /// name -- which is exactly the situation for members like
        /// CheckVessel that commonly have more than one overload. Walking
        /// GetMethods() and matching by name avoids that exception. When a
        /// name is genuinely overloaded, callers that care about a specific
        /// signature should use FindMethod(type, name, parameterCount)
        /// instead so they get the right overload rather than an arbitrary
        /// one.
        /// </summary>
        internal static MethodInfo FindMethod(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                foreach (MethodInfo candidate in current.GetMethods(InstanceFlags))
                {
                    if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                        return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a method by name and exact parameter count, walking the
        /// type hierarchy. Use this for names that may be overloaded (e.g.
        /// CheckVessel(Vessel) vs. CheckVessel(Vessel, bool)) so the caller
        /// gets the specific overload it knows how to call, instead of
        /// whichever overload GetMethods() happens to return first.
        /// </summary>
        internal static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                foreach (MethodInfo candidate in current.GetMethods(InstanceFlags))
                {
                    if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
                        continue;

                    if (candidate.GetParameters().Length == parameterCount)
                        return candidate;
                }
            }

            return null;
        }

        internal static IEnumerable AsEnumerable(object value)
        {
            return value as IEnumerable;
        }
    }
}
